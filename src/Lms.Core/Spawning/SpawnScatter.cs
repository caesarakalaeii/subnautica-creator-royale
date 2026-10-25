using System;
using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Spreads escape pods across the game's own valid start points.</summary>
    /// <remarks>
    ///     Stock Nitrox drops each new pod within fifty metres of the last. Candidates come from the
    ///     game's start-point texture (<c>RandomStartGenerator</c>), since the server cannot see terrain.
    /// </remarks>
    public static class SpawnScatter
    {
        /// <summary>Plans a whole group's pods at once and reports the closest pair.</summary>
        /// <param name="candidates">Valid start points from the game's own map, in any order.</param>
        /// <param name="taken">Pods already in the world, which a plan has to work around.</param>
        /// <param name="arena">The opening sphere; candidates outside it are excluded.</param>
        /// <param name="wanted">How many pods to plan for: the size of a group.</param>
        /// <param name="minSeparation">How far apart they are wanted. A target, never a veto.</param>
        /// <remarks>
        ///     Greedy farthest-point, tried from every eligible start; the layout with the largest
        ///     closest pair wins. O(candidates² × wanted), not the exact max-min optimum. Deterministic:
        ///     ties go to the earlier candidate.
        /// </remarks>
        public static SpawnLayout Plan(
            IReadOnlyList<WorldPoint> candidates,
            IReadOnlyList<WorldPoint> taken,
            ZoneSphere arena,
            int wanted,
            double minSeparation)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (taken == null)
            {
                throw new ArgumentNullException(nameof(taken));
            }

            if (wanted < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(wanted), wanted,
                    "A group has nought or more players in it.");
            }

            List<WorldPoint> eligible = Eligible(candidates, taken, arena);

            if (wanted == 0 || eligible.Count == 0)
            {
                return new SpawnLayout(new WorldPoint[0], wanted, 0, minSeparation);
            }

            List<WorldPoint>? best = null;
            double bestSeparation = double.NegativeInfinity;

            foreach (WorldPoint start in eligible)
            {
                List<WorldPoint> layout = Grow(start, eligible, taken, wanted);
                double separation = Closest(layout, taken);

                if (separation > bestSeparation)
                {
                    bestSeparation = separation;
                    best = layout;
                }
            }

            return new SpawnLayout(best!, wanted, bestSeparation, minSeparation);
        }

        /// <summary>
        ///     Grows one layout from a first pod, each next one furthest from everything so far,
        ///     including pods already in the world.
        /// </summary>
        /// <remarks>Shared with <see cref="SpawnCircle"/> so both modes hand out spots in the same order.</remarks>
        internal static List<WorldPoint> Grow(WorldPoint start, List<WorldPoint> eligible,
            IReadOnlyList<WorldPoint> taken, int wanted)
        {
            List<WorldPoint> layout = new List<WorldPoint>(wanted) { start };

            while (layout.Count < wanted)
            {
                WorldPoint? next = null;
                double bestRoom = double.NegativeInfinity;

                foreach (WorldPoint candidate in eligible)
                {
                    if (Contains(layout, candidate))
                    {
                        continue;
                    }

                    double room = Math.Min(RoomAround(candidate, layout), RoomAround(candidate, taken));

                    // Strictly better, so the earlier candidate wins a tie.
                    if (room > bestRoom)
                    {
                        bestRoom = room;
                        next = candidate;
                    }
                }

                if (next == null)
                {
                    // Out of start points: return fewer pods rather than fail.
                    break;
                }

                layout.Add(next.Value);
            }

            return layout;
        }

        /// <summary>Candidates inside the arena that nobody is already sitting on.</summary>
        private static List<WorldPoint> Eligible(IReadOnlyList<WorldPoint> candidates,
            IReadOnlyList<WorldPoint> taken, ZoneSphere arena)
        {
            List<WorldPoint> eligible = new List<WorldPoint>(candidates.Count);

            foreach (WorldPoint candidate in candidates)
            {
                if (arena.Contains(candidate) && !Contains(taken, candidate) && !Contains(eligible, candidate))
                {
                    eligible.Add(candidate);
                }
            }

            return eligible;
        }

        /// <summary>
        ///     Distance between the closest two points in a layout, counting the world's own pods.
        ///     Zero when there is nothing to compare.
        /// </summary>
        internal static double Closest(IReadOnlyList<WorldPoint> layout, IReadOnlyList<WorldPoint> taken)
        {
            double closest = double.PositiveInfinity;

            for (int i = 0; i < layout.Count; i++)
            {
                for (int j = i + 1; j < layout.Count; j++)
                {
                    closest = Math.Min(closest, layout[i].DistanceTo(layout[j]));
                }

                closest = Math.Min(closest, RoomAround(layout[i], taken));
            }

            return double.IsPositiveInfinity(closest) ? 0 : closest;
        }

        /// <summary>
        ///     Picks the next pod's spot: the candidate inside the arena that is furthest from every
        ///     pod already placed.
        /// </summary>
        /// <param name="candidates">Valid start points from the game's own map, in any order.</param>
        /// <param name="taken">Where the pods already placed are.</param>
        /// <param name="arena">The opening sphere; candidates outside it are excluded.</param>
        /// <param name="minSeparation">How far apart pods are wanted. A target, never a veto.</param>
        /// <returns>
        ///     Null when no candidate is inside the arena; the caller then leaves placement to Nitrox.
        /// </returns>
        public static WorldPoint? Next(
            IReadOnlyList<WorldPoint> candidates,
            IReadOnlyList<WorldPoint> taken,
            ZoneSphere arena,
            double minSeparation)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (taken == null)
            {
                throw new ArgumentNullException(nameof(taken));
            }

            WorldPoint? best = null;
            double bestRoom = double.NegativeInfinity;

            foreach (WorldPoint candidate in candidates)
            {
                if (!arena.Contains(candidate) || Contains(taken, candidate))
                {
                    continue;
                }

                double room = RoomAround(candidate, taken);

                // Strictly better, so an earlier candidate wins a tie (deterministic per seed).
                if (room > bestRoom)
                {
                    bestRoom = room;
                    best = candidate;
                }

                if (room >= minSeparation && taken.Count == 0)
                {
                    // Nothing placed yet, so every candidate scores the same: take the first.
                    break;
                }
            }

            return best;
        }

        /// <summary>How far the nearest already-placed pod is, or infinity when this is the first.</summary>
        private static double RoomAround(WorldPoint candidate, IReadOnlyList<WorldPoint> taken)
        {
            double nearest = double.PositiveInfinity;

            foreach (WorldPoint pod in taken)
            {
                double distance = candidate.DistanceTo(pod);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        private static bool Contains(IReadOnlyList<WorldPoint> points, WorldPoint point)
        {
            foreach (WorldPoint each in points)
            {
                if (each.Equals(point))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
