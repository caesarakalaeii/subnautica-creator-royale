using System;
using System.Collections.Generic;
using Lms.Core.Diagnostics;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Gives every player their own escape pod (and so their own fabricator), on a circle or
    ///     scattered across the game's approved start points in the opening zone.
    /// </summary>
    /// <remarks>
    ///     The whole group's layout is planned on the first pod request, so separation is consistent.
    ///     Circle spots are computed rather than found, so the server cannot tell whether they are over
    ///     water.
    /// </remarks>
    internal sealed class SpawnPlacement : ILmsSpawnPlacement
    {
        private readonly SpawnPolicy policy;
        private readonly ZoneSphere arena;

        private int placed;
        private bool warned;

        /// <summary>
        ///     The group's spots, planned once on the first request, when the real candidates and the
        ///     world's own pods are first visible.
        /// </summary>
        private SpawnLayout? layout;

        /// <summary>How many of the plan's spots have been handed out.</summary>
        private int handedOut;

        internal SpawnPlacement(SpawnPolicy policy, ZoneSphere arena)
        {
            this.policy = policy;
            this.arena = arena;
        }

        /// <summary>One player per pod (stock Nitrox uses fifty).</summary>
        public int PlayersPerPod => 1;

        public NitroxVector3? NextPod(IReadOnlyList<NitroxVector3> candidates, IReadOnlyList<NitroxVector3> taken)
        {
            IReadOnlyList<WorldPoint> points = Points(candidates);
            IReadOnlyList<WorldPoint> already = Points(taken);

            WorldPoint? chosen = FromPlan(points, already)
                                 // Past the plan (overflow or a taken spot): place one at a time
                                 // on an approved start point, even for a circle.
                                 ?? SpawnScatter.Next(points, already, arena, policy.MinSeparation);

            if (chosen == null)
            {
                // No approved start point in the arena: fall back to Nitrox's placement, and warn once
                // since everyone spawning together looks like the feature failing.
                if (!warned)
                {
                    warned = true;
                    Log.Warn($"{LmsServerMod.LogPrefix} no start point the game approves of lies inside " +
                             $"{arena}, so escape pods are Nitrox's to place. Widen zone.opening or move " +
                             "its centre towards the shallows.");
                }

                return null;
            }

            WorldPoint point = chosen.Value;

            // One log line per pod, to tell spread pods from Nitrox-placed ones.
            placed++;
            string separation = taken.Count == 0
                ? "the first"
                : Readable.Line($"{Nearest(point, taken):0} m from the nearest of {taken.Count}");

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} escape pod {placed} at ",
                $"({point.X:0}, {point.Y:0}, {point.Z:0}), ",
                $"{separation}"));

            return new NitroxVector3((float)point.X, (float)point.Y, (float)point.Z);
        }

        /// <summary>
        ///     The next spot from the group's plan, making it on the first call. Spots that already hold
        ///     a pod are skipped.
        /// </summary>
        private WorldPoint? FromPlan(IReadOnlyList<WorldPoint> candidates, IReadOnlyList<WorldPoint> taken)
        {
            if (layout == null)
            {
                layout = policy.OnACircle
                    ? SpawnCircle.Plan(policy.CircleRadius, taken, policy.GroupSize, policy.MinSeparation)
                    : SpawnScatter.Plan(candidates, taken, arena, policy.GroupSize, policy.MinSeparation);

                Announce(layout, candidates);
            }

            while (handedOut < layout.Pods.Count)
            {
                WorldPoint planned = layout.Pods[handedOut];
                handedOut++;

                if (!Occupied(planned, taken))
                {
                    return planned;
                }
            }

            return null;
        }

        /// <summary>
        ///     Logs what the plan achieved, once, on the first join; warns with placement-specific advice
        ///     when it misses the requested separation.
        /// </summary>
        private void Announce(SpawnLayout planned, IReadOnlyList<WorldPoint> candidates)
        {
            string where = policy.OnACircle
                ? Readable.Line($"a circle {policy.CircleRadius:0} m round the origin")
                : arena.ToString();

            string line = $"{LmsServerMod.LogPrefix} escape pods planned for {where}: {planned}.";

            if (planned.MeetsTarget)
            {
                Log.Info(line);
            }
            else if (policy.OnACircle)
            {
                Log.Warn(line + " A circle this small cannot hold the group that far apart. Widen " +
                         "spawn.circleRadius, or ask for less in spawn.minSeparation.");
            }
            else
            {
                Log.Warn(line + " The arena is short of start points the game approves of, so pods " +
                         "will be closer together than spawn.minSeparation asks. Widen zone.opening " +
                         "or move its centre towards the shallows.");
            }

            if (policy.OnACircle)
            {
                WarnAboutTheCircle(planned, candidates);
            }
        }

        /// <summary>
        ///     Warns, once, about circle spots outside the opening zone, and logs how far spots are from
        ///     the nearest approved start point as a hint that one may be over land.
        /// </summary>
        /// <remarks>
        ///     Spots outside the wall are still used: a player with no pod is worse.
        /// </remarks>
        private void WarnAboutTheCircle(SpawnLayout planned, IReadOnlyList<WorldPoint> candidates)
        {
            int outside = 0;
            foreach (WorldPoint pod in planned.Pods)
            {
                if (!arena.Contains(pod))
                {
                    outside++;
                }
            }

            if (outside > 0)
            {
                Log.Warn(Readable.Line(
                    $"{LmsServerMod.LogPrefix} {outside} of {planned.Pods.Count} pods on the circle ",
                    $"start outside {arena}, so those players are outside the wall before they have ",
                    $"moved. Shrink spawn.circleRadius or widen zone.opening."));
            }

            if (candidates.Count == 0)
            {
                return;
            }

            // Log a range: the game's sample can be very sparse, so only an outlier is meaningful.
            double closest = double.PositiveInfinity;
            double furthest = 0;

            foreach (WorldPoint pod in planned.Pods)
            {
                double nearest = Nearest(pod, candidates);
                closest = Math.Min(closest, nearest);
                furthest = Math.Max(furthest, nearest);
            }

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} the circle's spots sit {closest:0} to {furthest:0} m from ",
                $"the nearest of {candidates.Count} start points the game approves of. One spot far ",
                $"outside that spread is one over the Aurora, an island, or the map's edge — the ",
                $"circle does not consult that map and cannot tell. ./scripts/check-zone can."));
        }

        /// <summary>
        ///     Whether a planned spot already has a pod on it, within a metre: planned spots are double
        ///     but saved pods come back as float.
        /// </summary>
        private static bool Occupied(WorldPoint spot, IReadOnlyList<WorldPoint> taken) =>
            PodSpot.AnyAt(taken, spot);

        private static double Nearest(WorldPoint point, IReadOnlyList<NitroxVector3> taken) =>
            Nearest(point, Points(taken));

        private static double Nearest(WorldPoint point, IReadOnlyList<WorldPoint> taken)
        {
            double nearest = double.PositiveInfinity;
            foreach (WorldPoint other in taken)
            {
                double distance = point.DistanceTo(other);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        private static IReadOnlyList<WorldPoint> Points(IReadOnlyList<NitroxVector3> vectors)
        {
            List<WorldPoint> points = new List<WorldPoint>(vectors.Count);
            foreach (NitroxVector3 vector in vectors)
            {
                points.Add(new WorldPoint(vector.X, vector.Y, vector.Z));
            }

            return points;
        }
    }
}
