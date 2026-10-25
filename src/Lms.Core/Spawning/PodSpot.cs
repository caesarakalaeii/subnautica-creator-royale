using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Spawning
{
    /// <summary>Whether two escape pods are in the same place, within a tolerance.</summary>
    /// <remarks>
    ///     Planned spots are doubles but pods are stored as floats, so exact comparison fails after a
    ///     round trip. A metre is far above float error and far below any real separation.
    /// </remarks>
    public static class PodSpot
    {
        /// <summary>How close two pods have to be to be the same pod, in metres.</summary>
        public const double SameSpotMetres = 1;

        /// <summary>Whether these two are the same spot, allowing for float rounding.</summary>
        public static bool IsAt(WorldPoint spot, WorldPoint other) =>
            spot.DistanceTo(other) <= SameSpotMetres;

        /// <summary>Whether any of <paramref name="pods"/> is already on this spot.</summary>
        public static bool AnyAt(IReadOnlyList<WorldPoint> pods, WorldPoint spot)
        {
            foreach (WorldPoint pod in pods)
            {
                if (IsAt(pod, spot))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
