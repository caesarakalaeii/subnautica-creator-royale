using System;
using System.Collections.Generic;
using Lms.Core.Diagnostics;
using Lms.Core.Spawning;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Seats every contestant in the pod the placement assigns them (<c>lms podspawn</c>).
    /// </summary>
    /// <remarks>
    ///     Plans the layout fresh on every run, unlike <see cref="SpawnPlacement"/>, which caches it for
    ///     joins.
    /// </remarks>
    internal sealed class PodSpawnRun
    {
        private readonly SpawnPolicy policy;
        private readonly ZoneSphere arena;

        /// <summary>
        ///     Players in the match (not just connected); ghosts such as casters get no pod.
        /// </summary>
        private readonly Func<IReadOnlyList<LmsPlayerReading>> contestants;

        internal PodSpawnRun(SpawnPolicy policy, ZoneSphere arena,
            Func<IReadOnlyList<LmsPlayerReading>> contestants)
        {
            this.policy = policy;
            this.arena = arena;
            this.contestants = contestants;
        }

        /// <summary>
        ///     Runs one <c>podspawn</c> and says what it managed, for the operator and the log.
        /// </summary>
        internal PodCommandOutcome Run()
        {
            ILmsPodSeating? seating = LmsPods.Seating;

            if (seating == null)
            {
                return PodCommandOutcome.Nothing(
                    "there is no server here to place pods, so podspawn did nothing.");
            }

            if (!policy.OwnPod)
            {
                // Shared pod: there is no layout to seat anybody on.
                return PodCommandOutcome.Nothing(
                    "spawn.ownPod is off, so there is one pod between everybody and nothing to lay out.");
            }

            IReadOnlyList<LmsPlayerReading> present = contestants();

            if (present.Count == 0)
            {
                return PodCommandOutcome.Nothing(
                    "nobody in the match is connected, so there is nobody to seat.");
            }

            // Read once: a seat names its pod by its index in this list.
            IReadOnlyList<LmsPod> pods = seating.Pods;
            SpawnLayout layout = Layout(seating, PodPoints.Of(pods));
            PodSeatingPlan plan = PodSeating.Plan(layout.Pods, PodPoints.Readings(pods), Present(present));

            return Seat(seating, plan, layout, pods, present);
        }

        /// <summary>
        ///     The group's spots, planned fresh from the current world.
        /// </summary>
        /// <remarks>
        ///     Existing pods are not passed as <c>taken</c>, or the group would be planned around itself.
        ///     The circle still gets them because it recognises pods already on its spots.
        /// </remarks>
        private SpawnLayout Layout(ILmsPodSeating seating, IReadOnlyList<WorldPoint> pods)
        {
            if (policy.OnACircle)
            {
                return SpawnCircle.Plan(policy.CircleRadius, pods, policy.GroupSize, policy.MinSeparation);
            }

            return SpawnScatter.Plan(PodPoints.Of(seating.StartPoints), NoPods, arena,
                policy.GroupSize, policy.MinSeparation);
        }

        private static readonly WorldPoint[] NoPods = new WorldPoint[0];

        private PodCommandOutcome Seat(ILmsPodSeating seating, PodSeatingPlan plan, SpawnLayout layout,
            IReadOnlyList<LmsPod> pods, IReadOnlyList<LmsPlayerReading> present)
        {
            if (plan.Seats.Count == 0)
            {
                // Only a scatter can get here: no approved start point lies inside the arena.
                return PodCommandOutcome.Nothing(Readable.Line(
                    $"the placement found nowhere to put anybody: no start point the game approves ",
                    $"of lies inside {arena}. Widen zone.opening, or set spawn.circleRadius."));
            }

            // Make every pod first, then seat everybody: a teleport that arrives while the client is
            // moving EscapePod.main into its pod leaves the player outside it. MakePod takes the owner
            // because an unowned pod reads as every client's own. Seats use the plan's pod id, never
            // "the nearest pod".
            int made = 0;
            string?[] podFor = new string?[plan.Seats.Count];

            for (int index = 0; index < plan.Seats.Count; index++)
            {
                PodSeat seat = plan.Seats[index];

                if (seat.Pod is int existing)
                {
                    podFor[index] = pods[existing].Id;
                    continue;
                }

                podFor[index] = seating.MakePod(Vector(seat.Spot), (ushort)seat.Player);

                if (podFor[index] != null)
                {
                    made++;
                }
            }

            int seated = 0;
            List<string> moved = new List<string>(plan.Seats.Count);
            List<string> dropped = new List<string>(plan.Seats.Count);

            for (int index = 0; index < plan.Seats.Count; index++)
            {
                PodSeat seat = plan.Seats[index];
                string? pod = podFor[index];

                if (pod != null && seating.Seat((ushort)seat.Player, pod))
                {
                    seated++;
                    moved.Add(Name(present, seat.Player));
                }
                else
                {
                    // Player or pod gone by now: counted as left alone, so the round's roster check
                    // sees them.
                    dropped.Add(Name(present, seat.Player));
                }
            }

            string message = Readable.Line(
                $"{seated} player(s) onto their pods, {made} of which had to be made. ",
                $"{layout}");

            if (plan.Unseated > 0)
            {
                message += Readable.Line(
                    $" {plan.Unseated} left where they were: the plan is {plan.Seats.Count} spots ",
                    $"wide, so raise spawn.groupSize for a group this size.");
            }

            // Warn when the layout falls short: the fix is a config edit before the match.
            if (layout.MeetsTarget)
            {
                Log.Info($"{LmsServerMod.LogPrefix} podspawn: {message}");
            }
            else
            {
                Log.Warn($"{LmsServerMod.LogPrefix} podspawn: {message}. Widen spawn.circleRadius, ask " +
                         "for less in spawn.minSeparation, or clear out the pods a previous radius left " +
                         "behind — a derelict counts towards the separation because a player could be " +
                         "put in it.");
            }

            return new PodCommandOutcome(seated, message,
                Readable.Line($"{seated} seated, {made} pods made, {plan.Unseated} left alone"),
                moved, LeftAlone(present, plan, dropped));
        }

        /// <summary>
        ///     Players the plan did not seat: the overflow, plus moves refused because the player had
        ///     left. Matched by id, since the plan assigns spots in name order, not roster order.
        /// </summary>
        private static List<string> LeftAlone(IReadOnlyList<LmsPlayerReading> present, PodSeatingPlan plan,
            List<string> dropped)
        {
            List<string> left = dropped;

            foreach (LmsPlayerReading player in present)
            {
                if (!HasASpot(plan, player.Id))
                {
                    left.Add(player.Name);
                }
            }

            return left;
        }

        private static bool HasASpot(PodSeatingPlan plan, int id)
        {
            foreach (PodSeat seat in plan.Seats)
            {
                if (seat.Player == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<PlayerReading> Present(IReadOnlyList<LmsPlayerReading> present)
        {
            List<PlayerReading> readings = new List<PlayerReading>(present.Count);

            foreach (LmsPlayerReading player in present)
            {
                // Same conversion as TournamentHost.Readings, into Core's player shape.
                readings.Add(new PlayerReading(player.Id, player.Name,
                    new WorldPoint(player.X, player.Y, player.Z), player.Alive,
                    player.Health, player.Oxygen, player.Food, player.Water));
            }

            return readings;
        }

        private static string Name(IReadOnlyList<LmsPlayerReading> present, int id)
        {
            foreach (LmsPlayerReading player in present)
            {
                if (player.Id == id)
                {
                    return player.Name;
                }
            }

            throw new InvalidOperationException(
                $"the plan seated player {id}, who is not in the roster it was made from.");
        }

        private static NitroxVector3 Vector(WorldPoint point) =>
            new NitroxVector3((float)point.X, (float)point.Y, (float)point.Z);
    }
}
