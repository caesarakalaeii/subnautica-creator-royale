using System;
using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Places every pod on one circle around the origin, at sea level (<c>y = 0</c>).</summary>
    /// <remarks>
    ///     An alternative to <see cref="SpawnScatter"/>: <c>n</c> pods on radius <c>r</c> are exactly
    ///     <c>2r·sin(π/n)</c> apart on any world. The server cannot see terrain, so a spot may be on
    ///     land or the Aurora; the caller reports how far each is from the game's own start points.
    /// </remarks>
    public static class SpawnCircle
    {
        /// <summary>Plans a whole group's pods on one circle and reports the closest pair.</summary>
        /// <param name="radius">Metres from the origin to every pod. From <c>spawn.circleRadius</c>.</param>
        /// <param name="taken">Pods already in the world, which count towards the separation.</param>
        /// <param name="wanted">How many pods to place: the size of a group.</param>
        /// <param name="minSeparation">How far apart they are wanted. A target, never a veto.</param>
        /// <remarks>
        ///     Deterministic: the first spot is due north and the rest run clockwise. Spots are handed
        ///     out in farthest-point order, so a smaller group still spreads out. Always fits the whole
        ///     group; if <see cref="SpawnLayout.MeetsTarget"/> is false, widen the radius.
        /// </remarks>
        public static SpawnLayout Plan(
            double radius,
            IReadOnlyList<WorldPoint> taken,
            int wanted,
            double minSeparation)
        {
            if (taken == null)
            {
                throw new ArgumentNullException(nameof(taken));
            }

            if (!(radius > 0) || double.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius,
                    "A circle of pods has to have a radius, or it is one pod with everybody in it.");
            }

            if (wanted < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(wanted), wanted,
                    "A group has nought or more players in it.");
            }

            if (wanted == 0)
            {
                return new SpawnLayout(new WorldPoint[0], wanted, 0, minSeparation);
            }

            List<WorldPoint> circumference = Circumference(radius, wanted);

            // A pod already on one of these spots is that spot, so it is not measured against itself.
            List<WorldPoint> elsewhere = Elsewhere(taken, circumference);

            // On a circle every start gives the same closest pair, so start due north.
            List<WorldPoint> layout = SpawnScatter.Grow(circumference[0], circumference, elsewhere, wanted);

            return new SpawnLayout(layout, wanted, SpawnScatter.Closest(layout, elsewhere), minSeparation);
        }

        /// <summary>The pods in the world that are not on one of the circle's own spots.</summary>
        private static List<WorldPoint> Elsewhere(IReadOnlyList<WorldPoint> taken,
            IReadOnlyList<WorldPoint> circumference)
        {
            List<WorldPoint> elsewhere = new List<WorldPoint>(taken.Count);

            foreach (WorldPoint pod in taken)
            {
                if (!PodSpot.AnyAt(circumference, pod))
                {
                    elsewhere.Add(pod);
                }
            }

            return elsewhere;
        }

        private static List<WorldPoint> Circumference(double radius, int count)
        {
            List<WorldPoint> points = new List<WorldPoint>(count);

            for (int index = 0; index < count; index++)
            {
                double bearing = 2 * Math.PI * index / count;

                // Sine on X and cosine on Z: starts due north (+Z) and runs clockwise like a compass.
                points.Add(new WorldPoint(radius * Math.Sin(bearing), 0, radius * Math.Cos(bearing)));
            }

            return points;
        }
    }
}
