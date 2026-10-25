using System;
using System.Collections.Generic;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Membranes;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     The boxing ring: watches the local player against every live stasis field, and switches
    ///     the ring's wall on for them once it has caught them.
    /// </summary>
    /// <remarks>
    ///     The wall is read from the bubble's mesh, since <c>StasisSphere</c>'s size field is private,
    ///     so the enforced wall matches the drawn one. Only the local player is held.
    /// </remarks>
    internal sealed class RingEnforcer : MonoBehaviour
    {
        /// <summary>
        ///     Interval of the fallback sweep for fields <see cref="RingFieldPatches"/> did not report.
        /// </summary>
        private const float RegistryIntervalSeconds = 5f;

        private const float EscapeMessageIntervalSeconds = 1f;

        private const float RefusalMessageIntervalSeconds = 1f;

        /// <summary>Slower than the escape message: re-boarding a Cyclops takes a few seconds anyway.</summary>
        private const float BoardingMessageIntervalSeconds = 3f;

        private static RingEnforcer active;
        private static RingPolicy sizing = RingPolicy.Default;

        private readonly List<StasisSphere> known = new List<StasisSphere>();
        private readonly List<Ring> rings = new List<Ring>();

        private RingPolicy policy = RingPolicy.Default;
        private SpectatorSession? spectator;
        private MatchFollower? match;
        private PlayerEventLog? events;
        private BiomeSampler? biomes;
        private RifleCooldown cooldown = new RifleCooldown(RingPolicy.Default.Limits);
        private float nextRegistryScan;
        private float nextRefusalMessage;

        /// <summary>Kept rather than read once, because the role changes mid-session.</summary>
        private LmsRuntime? runtime;

        /// <summary>Whether the server has told this client the match cannot see it.</summary>
        private bool IsGhost => runtime != null && runtime.IsGhost;

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            TournamentConfig? config = runtime?.Config;
            spectator = runtime?.Spectator;
            match = runtime?.Follower;
            events = runtime?.Events;
            biomes = GetComponent<BiomeSampler>();

            // Without containment the stasis rifle behaves exactly as in the base game.
            if (config == null || !config.Flags.EnforceContainment)
            {
                enabled = false;
                return;
            }

            policy = config.Ring;
            sizing = policy;
            cooldown = new RifleCooldown(policy.Limits);
            active = this;

            // Subscribed after the enable check: the patches below are applied once, so a client
            // that started with containment off stays off until relaunch.
            if (runtime != null)
            {
                runtime.Reconfigured += Reconfigure;
            }

            RingFieldPatches.Apply();
            RingBoardingGate.Apply();

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} rings are {policy.Limits.MinRadius:0.#}m for ",
                $"{policy.Limits.MinLifetime.TotalSeconds:0.#}s at a tap and {policy.Limits.MaxRadius:0.#}m for ",
                $"{policy.Limits.MaxLifetime.TotalSeconds:0.#}s at a full charge, ",
                $"costing {policy.EnergyCost:0.#} of a battery, ",
                $"landing {policy.PlacementMargin:0.#}m inside the wall."));

            StartCoroutine(ReportPowerRule());
        }

        /// <summary>
        ///     Takes rules the server replaced. Rebuilding the cooldown resets it.
        /// </summary>
        /// <remarks>
        ///     Turning <c>flags.enforceContainment</c> off does not take effect until relaunch, because
        ///     the Harmony patches are applied once.
        /// </remarks>
        private void Reconfigure()
        {
            TournamentConfig? config = runtime?.Config;
            spectator = runtime?.Spectator;

            if (config == null)
            {
                return;
            }

            policy = config.Ring;
            sizing = policy;
            cooldown = new RifleCooldown(policy.Limits);

            if (!config.Flags.EnforceContainment)
            {
                Log.Warn($"{LmsMod.LogPrefix} the new rules switch containment off, and this client " +
                         "has already patched the rifle. The ring keeps working until you relaunch.");
            }
        }

        private void OnDestroy()
        {
            foreach (Ring ring in rings)
            {
                ring.Shell.Dispose();
            }

            rings.Clear();
            Release();

            if (active == this)
            {
                active = null;
            }
        }

        /// <summary>
        ///     A field has just switched on, from <see cref="RingFieldPatches"/>. Static because a
        ///     Harmony patch has nowhere else to call.
        /// </summary>
        internal static void FieldEnabled(StasisSphere field)
        {
            if (active != null && active.enabled)
            {
                active.Track(field);
            }
        }

        /// <summary>
        ///     Sets what a full-charge ring costs the rifle's battery. The game charges it while the
        ///     trigger is held, so a partial charge costs proportionally less.
        /// </summary>
        internal static void Price(StasisRifle rifle)
        {
            if (rifle != null)
            {
                rifle.energyCost = (float)sizing.EnergyCost;
            }
        }

        /// <summary>
        ///     Whether the local player's rifle will fire, which it will not while their own ring
        ///     stands. Always true when the ring is disabled.
        /// </summary>
        internal static bool MayFire() => active == null || !active.enabled || active.cooldown.IsReady(Now());

        /// <summary>Records a shot, which locks the rifle for as long as the ring it leaves lives.</summary>
        /// <remarks>
        ///     Only the local rifle reaches here: remote fields arrive via
        ///     <c>BulletManager.ShootStasisSphere</c>, which calls <c>Shoot</c>, never <c>Fire</c>.
        /// </remarks>
        internal static void Fired(double charge)
        {
            if (active != null && active.enabled)
            {
                active.cooldown.Fired(Now(), charge);
                active.Record(AuditRecordKind.RingFired, charge);
            }
        }

        /// <summary>Says why the trigger did nothing, at most once a second.</summary>
        internal static void RefusedToFire()
        {
            if (active == null || !active.enabled || Time.time < active.nextRefusalMessage)
            {
                return;
            }

            active.nextRefusalMessage = Time.time + RefusalMessageIntervalSeconds;
            ErrorMessage.AddMessage(
                $"LMS: your ring is still up — {active.cooldown.Remaining(Now()).TotalSeconds:0.#}s");
        }

        /// <summary>The config's numbers, on the field the game is about to fire.</summary>
        internal static void Resize(StasisSphere field)
        {
            if (field == null)
            {
                return;
            }

            field.minRadius = (float)sizing.Limits.MinRadius;
            field.maxRadius = (float)sizing.Limits.MaxRadius;
            field.minTime = (float)sizing.Limits.MinLifetime.TotalSeconds;
            field.maxTime = (float)sizing.Limits.MaxLifetime.TotalSeconds;
        }

        /// <remarks>
        ///     LateUpdate so the player has finished moving and <c>StasisSphere.LateUpdate</c> has had
        ///     its chance to resize the bubble this frame.
        /// </remarks>
        private void LateUpdate()
        {
            if (Player.main == null)
            {
                // Between sessions: unlatch both gates so neither outlives the session.
                Release();
                cooldown.Ready();
                return;
            }

            Scan();

            WorldPoint position = Position();

            // Neither an eliminated player nor a ghost can be caged; fields still track and expire.
            bool catchable = (spectator == null || spectator.CanBeCaged) && !IsGhost;
            bool held = false;

            for (int index = rings.Count - 1; index >= 0; index--)
            {
                Ring ring = rings[index];
                ZoneSphere wall = WallOf(ring);

                if (ring.IsNew)
                {
                    // Skip the first frame: the size may not be this field's yet (see Ring.IsNew).
                    ring.IsNew = false;
                    if (wall.Radius > 0)
                    {
                        ring.Shell.PlaceAt(Centre(wall), (float)wall.Radius);
                    }

                    continue;
                }

                ContainmentAssessment assessment = ring.Watch.Update(position, wall);

                if (catchable)
                {
                    Apply(ring, assessment, wall);
                    held |= ring.Watch.IsContained;
                }
                else if (wall.Radius > 0)
                {
                    ring.Shell.PlaceAt(Centre(wall), (float)wall.Radius);
                }

                if (ring.Watch.IsFinished)
                {
                    ring.Shell.Dispose();
                    rings.RemoveAt(index);
                }
            }

            // Held by any ring is held. Static because the boarding gate's Harmony prefix reads it.
            RingHold.IsHeld = held;
        }

        private void Apply(Ring ring, ContainmentAssessment assessment, ZoneSphere wall)
        {
            if (wall.Radius > 0)
            {
                ring.Shell.PlaceAt(Centre(wall), (float)wall.Radius);
            }

            switch (assessment.Verdict)
            {
                case ContainmentVerdict.Caught:
                    // The shell becomes solid only now, for the player it has caught.
                    ring.Shell.SetHolding(true);
                    Record(AuditRecordKind.PlayerCaged);
                    ErrorMessage.AddMessage("LMS: caught in the ring — you are not leaving until it does");
                    Log.Info($"{LmsMod.LogPrefix} caught in a ring at {wall}");

                    Unride(ring, assessment, wall, sayItOnce: true);

                    break;

                case ContainmentVerdict.Escaped:
                    // The shell normally prevents this; the rule is the backstop.
                    bool leftAVehicle = ForcedPlacement.Dismount(
                        VehicleRule.For(assessment.IsContained, ForcedPlacement.RideOfLocalPlayer()));
                    ForcedPlacement.PutInside(wall, Position(), policy.PlacementMargin, leftAVehicle);

                    // Either the player got through the wall (a defect) or the wall shrank past them (expected).
                    bool physicsLost = assessment.Crossing.WentThroughTheWall;

                    if (physicsLost && Time.time >= ring.NextEscapeMessage)
                    {
                        ring.NextEscapeMessage = Time.time + EscapeMessageIntervalSeconds;
                        ErrorMessage.AddMessage("LMS: the ring still has you");
                        Log.Warn($"{LmsMod.LogPrefix} put back inside a ring the local player got out of: " +
                                 $"{assessment.Crossing.Kind}, wall {wall}");
                    }
                    else if (!physicsLost && !ring.ReportedTightening)
                    {
                        // Once per field: a full charge does this on most shrink steps.
                        ring.ReportedTightening = true;
                        Log.Info($"{LmsMod.LogPrefix} the ring is closing past the local player and " +
                                 $"pulling them in: wall {wall}");
                    }

                    break;

                case ContainmentVerdict.Held:
                    // Enforced every frame because boarding paths (hatch, dock, base trigger) cannot
                    // be enumerated; Player.currentSub is where all of them end up.
                    Unride(ring, assessment, wall, sayItOnce: false);
                    break;

                case ContainmentVerdict.Released:
                    Record(AuditRecordKind.PlayerReleased);
                    ring.Shell.SetHolding(false);
                    ErrorMessage.AddMessage("LMS: the ring is gone");
                    Log.Info($"{LmsMod.LogPrefix} a ring holding the local player has expired");
                    break;
            }
        }

        /// <summary>
        ///     Turns a held player back into a swimmer, wherever they have got to. A no-op for a swimmer.
        /// </summary>
        /// <remarks>
        ///     Places them at the ring's centre rather than the nearest inside point: a Cyclops straddles
        ///     a small field, so the nearest point would be the wall itself.
        /// </remarks>
        /// <param name="ring">The ring holding the player.</param>
        /// <param name="assessment">What the ring says about the player this frame.</param>
        /// <param name="wall">The ring's sphere.</param>
        /// <param name="sayItOnce">
        ///     True for the catch, which says it once; later ejections are rate-limited.
        /// </param>
        private void Unride(Ring ring, ContainmentAssessment assessment, ZoneSphere wall, bool sayItOnce)
        {
            if (!ForcedPlacement.Dismount(
                    VehicleRule.For(assessment.IsContained, ForcedPlacement.RideOfLocalPlayer())))
            {
                return;
            }

            ForcedPlacement.PutInside(wall, Position(), policy.PlacementMargin, leftAVehicle: true);

            if (sayItOnce)
            {
                ErrorMessage.AddMessage("LMS: the ring does not hold vehicles — you are out of yours");
                return;
            }

            if (Time.time < ring.NextBoardingMessage)
            {
                return;
            }

            ring.NextBoardingMessage = Time.time + BoardingMessageIntervalSeconds;
            ErrorMessage.AddMessage("LMS: the ring still has you — you are not taking that with you");
            Log.Info($"{LmsMod.LogPrefix} put a held player back out of what they climbed into: wall {wall}");
        }

        /// <summary>Finds stasis fields, and notices the frame one switches on.</summary>
        private void Scan()
        {
            if (Time.time >= nextRegistryScan)
            {
                nextRegistryScan = Time.time + RegistryIntervalSeconds;
                Register();
            }

            for (int index = known.Count - 1; index >= 0; index--)
            {
                StasisSphere field = known[index];
                if (field == null)
                {
                    known.RemoveAt(index);
                    continue;
                }

                // EnableField switches the renderer on and the field's death switches it off.
                MeshRenderer renderer = field.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.enabled)
                {
                    Track(field);
                }
            }
        }

        /// <summary>Warns once a session exists if the save does not charge power.</summary>
        /// <remarks>
        ///     With <c>NoEnergy</c> (creative), <c>EnergyMixin.ModifyCharge</c> spends nothing, so the
        ///     configured ring price is never charged.
        /// </remarks>
        private System.Collections.IEnumerator ReportPowerRule()
        {
            while (Player.main == null)
            {
                yield return null;
            }

            if (!GameModeUtils.RequiresPower())
            {
                Log.Warn(Readable.Line(
                    $"{LmsMod.LogPrefix} this save does not require power, so rings are free ",
                    $"whatever ring.energyCost says ({policy.EnergyCost:0.#})."));
            }
        }

        /// <summary>Starts holding the local player to account against one live field.</summary>
        private void Track(StasisSphere field)
        {
            if (field == null || Player.main == null || !field.gameObject.activeInHierarchy || Tracking(field))
            {
                return;
            }

            MeshRenderer renderer = field.GetComponent<MeshRenderer>();
            MeshFilter filter = field.GetComponent<MeshFilter>();

            if (renderer == null || filter == null || filter.sharedMesh == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} a stasis field has no mesh to read a wall from; it will not hold anybody.");
                return;
            }

            rings.Add(new Ring(field, renderer, filter, new ContainmentWatch(Position()),
                RingShell.Create($"LmsRingShell{rings.Count}")));
        }

        /// <summary>Registers every stasis field that belongs to a scene.</summary>
        /// <remarks>
        ///     <c>FindObjectsOfTypeAll</c> sees inactive bullets but also prefabs, whose renderer is
        ///     enabled on disk; a valid scene tells a live field from a prefab.
        /// </remarks>
        private void Register()
        {
            foreach (StasisSphere field in Resources.FindObjectsOfTypeAll<StasisSphere>())
            {
                if (field == null || known.Contains(field))
                {
                    continue;
                }

                // Also sizes fields the Shoot patch never saw.
                Resize(field);

                if (field.gameObject.scene.IsValid())
                {
                    known.Add(field);
                }
            }
        }

        private bool Tracking(StasisSphere field)
        {
            foreach (Ring ring in rings)
            {
                if (ring.Field == field)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Where this field's wall is this frame, or default if the field has gone.
        /// </summary>
        /// <remarks>
        ///     From the mesh's local bounds and scale, not <c>Renderer.bounds</c>, whose world AABB
        ///     grows with rotation. The smallest extent gives the sphere that fits inside what is drawn.
        /// </remarks>
        private static ZoneSphere WallOf(Ring ring)
        {
            // A destroyed shell means the scene, and the field with it, has gone.
            if (ring.Field == null || ring.Renderer == null || !ring.Renderer.enabled || !ring.Shell.IsAlive)
            {
                return default;
            }

            Mesh mesh = ring.Filter == null ? null : ring.Filter.sharedMesh;
            if (mesh == null)
            {
                return default;
            }

            Transform tr = ring.Field.transform;
            Bounds local = mesh.bounds;
            Vector3 scale = tr.lossyScale;

            float radius = Mathf.Min(local.extents.x, Mathf.Min(local.extents.y, local.extents.z))
                           * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            Vector3 centre = tr.TransformPoint(local.center);

            return new ZoneSphere(new WorldPoint(centre.x, centre.y, centre.z), radius);
        }

        private static Vector3 Centre(ZoneSphere wall) =>
            new Vector3((float)wall.Centre.X, (float)wall.Centre.Y, (float)wall.Centre.Z);

        /// <summary>
        ///     Queues one of the ring's audit events for the reporter.
        /// </summary>
        /// <remarks>
        ///     No actor: the field's owner lives in NitroxClient's bullet map, which the mod does not
        ///     reference. Events with no match running land in generation zero and the server drops them.
        /// </remarks>
        private void Record(AuditRecordKind kind, double? amount = null)
        {
            if (events == null || match == null || Player.main == null)
            {
                return;
            }

            LiveMixin live = Player.main.liveMixin;
            WorldPoint at = Position();

            events.Add(match.Generation, new PlayerEvent(kind, match.Elapsed, at,
                biomes == null ? null : biomes.Named,
                amount, live == null ? (double?)null : live.health,
                match.CompletedPhases > 0 ? match.CompletedPhases : (int?)null));
        }

        /// <summary>Lets go of the boarding gate. Held is a state of the player, not of this component.</summary>
        private static void Release() => RingHold.IsHeld = false;

        /// <summary>The rifle's clock. Scaled game time, because a stasis field ages on it too.</summary>
        private static TimeSpan Now() => TimeSpan.FromSeconds(Time.time);

        private static WorldPoint Position()
        {
            Vector3 p = Player.main.transform.position;
            return new WorldPoint(p.x, p.y, p.z);
        }

        /// <summary>One live field, and everything this client keeps about it.</summary>
        private sealed class Ring
        {
            internal Ring(StasisSphere field, MeshRenderer renderer, MeshFilter filter,
                ContainmentWatch watch, RingShell shell)
            {
                Field = field;
                Renderer = renderer;
                Filter = filter;
                Watch = watch;
                Shell = shell;
                IsNew = true;
            }

            internal StasisSphere Field { get; }

            internal MeshRenderer Renderer { get; }

            internal MeshFilter Filter { get; }

            /// <summary>
            ///     True for the frame a field is first seen. The game sizes a field in its own
            ///     <c>LateUpdate</c> and script order is arbitrary, so the transform may still be stale.
            /// </summary>
            internal bool IsNew { get; set; }

            internal ContainmentWatch Watch { get; }

            internal RingShell Shell { get; }

            internal float NextEscapeMessage { get; set; }

            /// <summary>When this field may next say it has taken somebody back out of a vehicle.</summary>
            internal float NextBoardingMessage { get; set; }

            /// <summary>Whether this field has already said it is closing past the player.</summary>
            internal bool ReportedTightening { get; set; }
        }
    }
}
