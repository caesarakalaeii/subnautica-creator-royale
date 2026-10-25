using System;
using System.Collections.Generic;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Follows the match, asks Core where the wall is and what standing outside it costs, and
    ///     applies the answer: the screen effect, the damage, the elimination.
    /// </summary>
    /// <remarks>
    ///     Zone timing and geometry come from the server via <see cref="MatchFollower"/>; the kill
    ///     itself is applied locally.
    /// </remarks>
    internal sealed class ZoneEnforcer : MonoBehaviour
    {
        private const float MessageIntervalSeconds = 1f;

        /// <summary>
        ///     The damage type used for zone, arrears and prawn damage: the server-judged amount lands unmodified.
        /// </summary>
        /// <remarks>
        ///     <c>DamageSystem.CalculateDamage</c> reduces <c>Radiation</c> for suits and zeroes it in a
        ///     Prawn suit; <c>Starve</c> is excluded from the reinforced-suit reduction and has no case.
        /// </remarks>
        private const DamageType TournamentDamage = DamageType.Starve;

        private TournamentConfig? config;
        private MatchFollower? match;
        private SpectatorSession? spectator;
        private PlayerEventLog? events;
        private ZoneWatch? watch;
        private BiomeSampler? biomes;
        private PhaseAlarm? alarm;

        // Per-match state is reset when the server starts another match.
        private int matchGeneration;

        /// <summary>A bill the server sent for the time this client was away, until it is settled.</summary>
        /// <remarks>
        ///     It arrives on the packet pump during world sync, before <c>Player.main</c> exists, so it
        ///     is held and paid from <see cref="Update"/> on the main thread.
        /// </remarks>
        private ArrearsBroadcast? owed;

        private ZoneAssessment assessment;
        private MatchClockState reportedState = MatchClockState.NotStarted;
        private float nextMessageTime;
        private bool eliminated;
        private bool holdingExposure;
        private bool warnedAboutMissingSound;

        /// <summary>Whether the log has said which event the phase warning borrows.</summary>
        private bool namedTheSound;

        /// <summary>Which phase this player has already been warned in, or -1 for none.</summary>
        private int warnedInPhase = -1;

        /// <summary>
        ///     Whether a bite that took nothing has already been reported. Session-scoped, since its
        ///     causes are not reset by a new match.
        /// </summary>
        private bool reportedRefusedBite;

        /// <summary>Kept rather than read once, because the role changes mid-session.</summary>
        private LmsRuntime? runtime;

        /// <summary>Where the wall is, for the renderer.</summary>
        internal ZoneState Zone => match == null ? default : match.Zone;

        /// <summary>The local player's standing, for the readout.</summary>
        internal ZoneAssessment Assessment => assessment;

        /// <summary>
        ///     Whether the wall still applies to this player: not once the match is finished with them,
        ///     and never for a ghost. No session means it does.
        /// </summary>
        private bool TakesZoneDamage =>
            (spectator == null || spectator.TakesZoneDamage) && !IsGhost;

        /// <summary>Whether the server has told this client the match cannot see it.</summary>
        private bool IsGhost => runtime != null && runtime.IsGhost;

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            config = runtime?.Config;
            match = runtime?.Follower;
            spectator = runtime?.Spectator;
            events = runtime?.Events;
            biomes = GetComponent<BiomeSampler>();

            if (config == null || match == null)
            {
                enabled = false;
                return;
            }

            // Never unsubscribed: this component and the runtime share one DontDestroyOnLoad object.
            if (runtime != null)
            {
                runtime.Reconfigured += Reconfigure;
            }

            StartMatchState();
        }

        /// <summary>
        ///     Takes rules the server replaced, rebuilding the watch and alarm that are built from the config.
        /// </summary>
        /// <remarks>
        ///     Not <see cref="StartMatchState"/>: a config change does not end a match, and the server
        ///     only replaces rules between matches.
        /// </remarks>
        private void Reconfigure()
        {
            config = runtime?.Config;
            spectator = runtime?.Spectator;

            if (config == null)
            {
                return;
            }

            watch = new ZoneWatch(config.ZonePolicy);
            alarm = new PhaseAlarm(config.Clock.WarningLead);
        }

        private void Update()
        {
            // The mod initialises in the main menu, before there is a world or a shared clock.
            if (Player.main == null || match == null || !WorldClock.Available)
            {
                return;
            }

            if (matchGeneration != match.Generation)
            {
                StartMatchState();
            }

            TimeSpan delta = TimeSpan.FromSeconds(Time.deltaTime);

            Announce(match.AdvanceTo(WorldClock.Now));
            ReportLifecycle();

            // Before the live check: a bill can be owed while the match is paused.
            Settle();

            if (!match.IsLive || !TakesZoneDamage)
            {
                ReleaseExposure();
                assessment = default;
                return;
            }

            Warn();

            assessment = watch!.Update(Position(), match.Zone, delta);
            Apply(assessment);
        }

        /// <summary>
        ///     Says out loud what the match just did: a phase starting, or the warning before one.
        /// </summary>
        private void Announce(IReadOnlyList<MatchClockEvent> clockEvents)
        {
            foreach (MatchClockEvent clockEvent in clockEvents)
            {
                // A warning whose phase starts in the same batch (a clock skip) is skipped.
                if (clockEvent.Kind == MatchClockEventKind.PhaseWarning && Overtaken(clockEvents, clockEvent))
                {
                    continue;
                }

                Announce(clockEvent);
            }
        }

        private static bool Overtaken(IReadOnlyList<MatchClockEvent> events, MatchClockEvent warning)
        {
            foreach (MatchClockEvent other in events)
            {
                if (other.Kind == MatchClockEventKind.PhaseStarted && other.PhaseNumber == warning.PhaseNumber)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Throws away everything that belonged to the match that just ended.</summary>
        private void StartMatchState()
        {
            matchGeneration = match!.Generation;
            watch = new ZoneWatch(config!.ZonePolicy);
            alarm = new PhaseAlarm(config.Clock.WarningLead);
            assessment = default;
            eliminated = false;
            warnedInPhase = -1;
            reportedState = match.State;
            ReleaseExposure();

            Log.Info($"{LmsMod.LogPrefix} {match.Status}");
        }

        /// <summary>Logs one line per lifecycle change.</summary>
        private void ReportLifecycle()
        {
            if (match!.State == reportedState)
            {
                return;
            }

            reportedState = match.State;
            Log.Info($"{LmsMod.LogPrefix} {match.Status}");
        }

        private void Announce(MatchClockEvent clockEvent)
        {
            if (clockEvent.Kind == MatchClockEventKind.PhaseWarning)
            {
                // The countdown in Warn says this.
                return;
            }

            ZoneState zone = match!.Zone;
            Log.Info($"{LmsMod.LogPrefix} phase {clockEvent.PhaseNumber} at {clockEvent.AtElapsed}: {zone}");
            ErrorMessage.AddMessage($"LMS: phase {clockEvent.PhaseNumber} — the zone is closing to {zone.Sphere.Radius:0} m");
        }

        /// <summary>
        ///     Counts the wall down, louder as it gets closer, inside the server's warning window only.
        /// </summary>
        /// <remarks>
        ///     Sound only from the second rung down, so players do not learn to tune it out.
        /// </remarks>
        private void Warn()
        {
            if (alarm == null || !match!.IsWarningActive)
            {
                return;
            }

            AlarmLevel? level = alarm.Due(match.CompletedPhases + 1, match.UntilNextPhase, out int seconds);
            if (level == null)
            {
                return;
            }

            ErrorMessage.AddMessage(level == AlarmLevel.Immediate
                ? $"LMS: the zone closes in {seconds}s — MOVE"
                : $"LMS: the zone closes in {seconds}s");

            if (level != AlarmLevel.Notice)
            {
                PlayAlert();
            }
        }

        /// <summary>
        ///     Takes a bill the server computed for the time this client was away. The last one wins.
        /// </summary>
        internal void Owed(ArrearsBroadcast bill)
        {
            owed = bill;
        }

        /// <summary>
        ///     Takes the health a server-judged hit (e.g. a prawn arm) just cost this player, immediately.
        /// </summary>
        /// <remarks>
        ///     The amount is trusted as judged; only whether there is a living body still in the match
        ///     is checked, which only this client knows.
        /// </remarks>
        internal void Hurt(HurtBroadcast hit)
        {
            if (Player.main == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} {hit} arrived with no body on this client to take it off.");
                return;
            }

            LiveMixin live = Player.main.liveMixin;
            if (live == null || !live.IsAlive() || !TakesZoneDamage)
            {
                // An ordinary race with the player dying, not a fault.
                Log.Info($"{LmsMod.LogPrefix} dropped {hit}; the match is finished with this player.");
                return;
            }

            float before = live.health;
            live.TakeDamage((float)hit.Damage, Player.main.transform.position, TournamentDamage);

            // TakeDamage refuses silently, so log when the hit took nothing.
            if (live.health >= before && live.IsAlive())
            {
                GameModeUtils.GetGameMode(out GameModeOption mode, out GameModeOption cheats);
                Log.Warn(Readable.Line(
                    $"{LmsMod.LogPrefix} {hit} took nothing: health is still {live.health:0.#}. ",
                    $"invincible={live.invincible} invincibleInCreative={live.invincibleInCreative} ",
                    $"noAggression={GameModeUtils.IsInvisible()} mode={mode} cheats={cheats}."));
            }

            ErrorMessage.AddMessage($"LMS: {hit.By} hit you for {hit.Damage:0.#}");

            if (!live.IsAlive())
            {
                ReportElimination(hit.By);
            }
        }

        /// <summary>
        ///     Pays whatever is owed, once there is a match and a body to take it off.
        /// </summary>
        /// <remarks>
        ///     Dropped for a ghost, for a different match generation or group, for a player the match
        ///     is finished with, and (with a warning) when damage is disabled in this client's config.
        /// </remarks>
        private void Settle()
        {
            if (owed == null)
            {
                return;
            }

            // The zone broadcast precedes the bill on the same channel, so this is only a wait.
            if (!match!.HasMatch)
            {
                return;
            }

            ArrearsBroadcast bill = owed.Value;

            // The server never bills a ghost; this covers a bill in flight when `lms ghost` is typed.
            if (IsGhost)
            {
                owed = null;
                Log.Info($"{LmsMod.LogPrefix} dropped arrears ({bill}); the match cannot see you.");
                return;
            }

            // Checked here rather than cleared on a generation change, which would drop a bill that
            // arrived with the broadcast that changed it.
            if (bill.Generation != match.Generation || bill.Group != match.Group)
            {
                owed = null;
                Log.Info($"{LmsMod.LogPrefix} dropped arrears for match {bill.Group}.{bill.Generation} " +
                         $"({bill}); this client is in {match.Group}.{match.Generation} now.");
                return;
            }

            // Update returns before this when Player.main is null.
            LiveMixin live = Player.main.liveMixin;
            if (live == null)
            {
                return;
            }

            // Cleared before the damage so a throwing TakeDamage cannot charge every frame.
            owed = null;

            if (!live.IsAlive() || !TakesZoneDamage)
            {
                Log.Info($"{LmsMod.LogPrefix} dropped arrears ({bill}); the match is finished with this player.");
                return;
            }

            if (!config!.Flags.EnforceDamage)
            {
                // The server only bills with the flag on, so the two config copies disagree.
                Log.Warn($"{LmsMod.LogPrefix} the server charged arrears ({bill}) and " +
                         "flags.enforceDamage is off in this client's config; nothing was taken. " +
                         "The two copies of the tournament file do not agree.");
                ErrorMessage.AddMessage($"LMS: the zone would have taken {bill.Damage:0.#} for your time away (damage disabled)");
                return;
            }

            Log.Info($"{LmsMod.LogPrefix} paying arrears: {bill}");
            live.TakeDamage((float)bill.Damage, Player.main.transform.position, TournamentDamage);

            ErrorMessage.AddMessage(bill.IsFatal
                ? $"LMS: the zone got you while you were away — {bill.Damage:0.#} over {bill.Absence.TotalSeconds:0}s"
                : $"LMS: the zone took {bill.Damage:0.#} for the {bill.Absence.TotalSeconds:0}s you were outside it");

            if (!live.IsAlive())
            {
                ReportElimination();
            }
        }

        private static WorldPoint Position()
        {
            Vector3 p = Player.main.transform.position;
            return new WorldPoint(p.x, p.y, p.z);
        }

        private void Apply(ZoneAssessment current)
        {
            ApplyExposure(current.Exposure);

            switch (current.Verdict)
            {
                case ZoneVerdict.Warning:
                    // Inside the current zone but outside the next one.
                    Message("LMS: you are outside the next zone — move in");
                    RecordWarning();
                    break;

                case ZoneVerdict.Outside:
                    Message($"LMS: outside the zone by {current.DistanceOutside:0} m — {current.UntilNextTick.TotalSeconds:0.0}s to the next hit");
                    Bite(current);
                    break;
            }
        }

        /// <summary>Drives the game's radiation screen overlay from the zone exposure.</summary>
        /// <remarks>
        ///     <c>RadiationsScreenFXController</c> reads <c>Player.radiationAmount</c> every frame and the
        ///     Aurora also writes it, so this takes the max and resets to zero only once on release.
        /// </remarks>
        private void ApplyExposure(double exposure)
        {
            if (exposure > 0)
            {
                holdingExposure = true;
                Player.main.SetRadiationAmount(Mathf.Max(Player.main.radiationAmount, (float)exposure));
                return;
            }

            ReleaseExposure();
        }

        private void ReleaseExposure()
        {
            if (!holdingExposure || Player.main == null)
            {
                holdingExposure = false;
                return;
            }

            holdingExposure = false;
            Player.main.SetRadiationAmount(0f);
        }

        private void Bite(ZoneAssessment current)
        {
            // Damage lands only on frames where a tick falls due.
            if (current.Ticks <= 0)
            {
                return;
            }

            if (!config!.Flags.EnforceDamage)
            {
                Message($"LMS: the zone would have taken {current.Damage:0.#} (damage disabled)");
                return;
            }

            LiveMixin live = Player.main.liveMixin;
            if (live == null || !live.IsAlive())
            {
                ReportElimination();
                return;
            }

            float before = live.health;
            live.TakeDamage((float)current.Damage, Player.main.transform.position, TournamentDamage);
            ReportBiteTookNothing(live, before, current);
            RecordBites(current);

            if (!live.IsAlive())
            {
                ReportElimination();
            }
        }

        /// <summary>
        ///     Logs once per session when a bite that should have hurt did not.
        /// </summary>
        /// <remarks>
        ///     <c>LiveMixin.TakeDamage</c> refuses silently: <c>invincible</c> (set by Nitrox during
        ///     initial sync), creative invincibility, or an unexpected game mode. The log names which.
        /// </remarks>
        private void ReportBiteTookNothing(LiveMixin live, float before, ZoneAssessment current)
        {
            if (reportedRefusedBite || current.Damage <= 0 || live.health < before)
            {
                return;
            }

            reportedRefusedBite = true;

            GameModeUtils.GetGameMode(out GameModeOption mode, out GameModeOption cheats);

            // Readable formats invariantly regardless of the machine's culture.
            Log.Warn(Readable.Line(
                $"{LmsMod.LogPrefix} the zone bit for {current.Damage:0.#} and took nothing: ",
                $"health is still {live.health:0.#}. invincible={live.invincible} ",
                $"invincibleInCreative={live.invincibleInCreative} ",
                $"noAggression={GameModeUtils.IsInvisible()} mode={mode} cheats={cheats}. ",
                $"Nothing this wall does can eliminate anybody while that is true."));
        }

        /// <summary>
        ///     Queues one audit record per tick, after the damage has landed.
        /// </summary>
        /// <remarks>
        ///     Ticks within one frame share a single health reading.
        /// </remarks>
        private void RecordBites(ZoneAssessment current)
        {
            for (int tick = 0; tick < current.Ticks; tick++)
            {
                Record(AuditRecordKind.ZoneBite, current.DamagePerTick);
            }
        }

        /// <summary>Records a zone warning once per phase, not per frame or per crossing.</summary>
        private void RecordWarning()
        {
            int phase = match!.CompletedPhases;

            if (warnedInPhase == phase)
            {
                return;
            }

            warnedInPhase = phase;
            Record(AuditRecordKind.ZoneWarned);
        }

        /// <summary>
        ///     Adds a player event with the server's match time and phase and this client's position and biome.
        /// </summary>
        private void Record(AuditRecordKind kind, double? amount = null)
        {
            if (events == null || match == null)
            {
                return;
            }

            LiveMixin live = Player.main == null ? null : Player.main.liveMixin;

            events.Add(match.Generation, new PlayerEvent(kind, match.Elapsed, Position(),
                biomes == null ? null : biomes.Named,
                amount, live == null ? (double?)null : live.health,
                match.CompletedPhases > 0 ? match.CompletedPhases : (int?)null));
        }

        /// <summary>Logs and shows the local player's elimination, once.</summary>
        /// <param name="by">The player who finished them, or null for the zone.</param>
        /// <remarks>
        ///     The server learns of the elimination from the spectator session, not from here.
        /// </remarks>
        private void ReportElimination(string? by = null)
        {
            if (eliminated)
            {
                return;
            }

            eliminated = true;

            if (by != null)
            {
                Log.Info($"{LmsMod.LogPrefix} local player eliminated by {by}");
                ErrorMessage.AddMessage($"LMS: {by} finished you. You are out.");
                return;
            }

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} local player eliminated by the zone, {assessment.DistanceOutside:0} m outside"));
            ErrorMessage.AddMessage("LMS: the zone got you. You are out.");
        }

        private void Message(string text)
        {
            if (Time.time < nextMessageTime)
            {
                return;
            }

            nextMessageTime = Time.time + MessageIntervalSeconds;
            ErrorMessage.AddMessage(text);
        }

        /// <summary>Plays the phase warning sound at the player's chosen volume.</summary>
        /// <remarks>
        ///     Borrows a known field's sound: FMOD event paths live inside the banks, cannot be
        ///     verified offline, and a wrong path fails silently.
        /// </remarks>
        private void PlayAlert()
        {
            // Zero is silent and skips the play.
            float volume = Preferences.WarningVolume;
            if (volume <= 0f)
            {
                return;
            }

            FMODAsset alert = Player.main.infectionRevealSound;
            if (alert == null)
            {
                if (!warnedAboutMissingSound)
                {
                    warnedAboutMissingSound = true;
                    Log.Warn($"{LmsMod.LogPrefix} no alert sound available; warnings are on-screen only.");
                }

                return;
            }

            if (!namedTheSound)
            {
                // Once, so the log names which event the warning plays.
                namedTheSound = true;
                Log.Info(Readable.Line(
                    $"{LmsMod.LogPrefix} the phase warning plays '{alert.path}' ",
                    $"at volume {volume:0.##}. Borrowed, and never yet heard by anybody."));
            }

            FMODUWE.PlayOneShot(alert, Player.main.transform.position, volume);
        }
    }
}
