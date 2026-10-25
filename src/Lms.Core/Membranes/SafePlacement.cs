using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Zones;

namespace Lms.Core.Membranes
{
    /// <summary>
    ///     Where to put a player the ring has to move: an ordered list of points inside the wall,
    ///     for the adapter to walk until one is clear of terrain.
    /// </summary>
    /// <remarks>
    ///     Replaces the game's own choice (<c>ToNormalMode(findNewPosition: false)</c>). Ordered by
    ///     smallest move; fallbacks favour upwards, since fields usually sit on a surface.
    /// </remarks>
    public static class SafePlacement
    {
        /// <summary>Offset directions, upwards first; this order breaks distance ties.</summary>
        private static readonly WorldPoint[] Directions =
        {
            new WorldPoint(0, 1, 0),
            new WorldPoint(0, 0, 1),
            new WorldPoint(1, 0, 0),
            new WorldPoint(0, 0, -1),
            new WorldPoint(-1, 0, 0),
            new WorldPoint(0, -1, 0),
        };

        /// <summary>How far out each ring of offsets sits, as a fraction of the room available.</summary>
        private static readonly double[] Reaches = { 0.5, 1 };

        /// <summary>Where to put a player the ring has just caught.</summary>
        /// <remarks>
        ///     A player taken out of a vehicle (<paramref name="leftAVehicle"/>) is measured from the
        ///     centre rather than their position, so they land clear of a hull that straddles the wall.
        /// </remarks>
        public static IReadOnlyList<WorldPoint> Landing(ZoneSphere wall, WorldPoint from, double margin, bool leftAVehicle) =>
            Inside(wall, leftAVehicle ? wall.Centre : from, margin);

        /// <summary>Candidate points inside the wall, nearest to <paramref name="from"/> first.</summary>
        /// <param name="wall">The ring as it stands right now.</param>
        /// <param name="from">Where the player is; candidates are ordered by distance from here.</param>
        /// <param name="margin">
        ///     How far inside the wall a candidate has to be, so physics does not push the player back out.
        /// </param>
        public static IReadOnlyList<WorldPoint> Inside(ZoneSphere wall, WorldPoint from, double margin)
        {
            if (!(margin >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(margin), margin, "A margin is a distance inside the wall.");
            }

            if (wall.Radius <= 0)
            {
                // A field that has gone has no inside; the player is already released.
                return new ReadOnlyCollection<WorldPoint>(new WorldPoint[0]);
            }

            double room = wall.Radius - margin;
            if (room < 0)
            {
                // A small field may be narrower than the margin; never offer points outside the wall.
                room = 0;
            }

            List<WorldPoint> candidates = new List<WorldPoint> { NearestPointInside(wall.Centre, from, room) };

            foreach (double reach in Reaches)
            {
                foreach (WorldPoint direction in Directions)
                {
                    candidates.Add(Offset(wall.Centre, direction, room * reach));
                }
            }

            candidates.Add(wall.Centre);

            // Equidistant candidates must keep the order above (upwards first).
            List<WorldPoint> ordered = new List<WorldPoint>(candidates);
            ordered.Sort((left, right) => from.DistanceTo(left).CompareTo(from.DistanceTo(right)));

            return new ReadOnlyCollection<WorldPoint>(Distinct(ordered));
        }

        /// <summary>The closest point to <paramref name="from"/> that is within <paramref name="room"/> of the centre.</summary>
        private static WorldPoint NearestPointInside(WorldPoint centre, WorldPoint from, double room)
        {
            double distance = centre.DistanceTo(from);
            if (distance <= room)
            {
                return from;
            }

            if (!(distance > 0))
            {
                // On the centre with no room: nowhere to point.
                return centre;
            }

            return centre.Towards(from, room / distance);
        }

        private static WorldPoint Offset(WorldPoint centre, WorldPoint direction, double distance) =>
            new WorldPoint(
                centre.X + direction.X * distance,
                centre.Y + direction.Y * distance,
                centre.Z + direction.Z * distance);

        /// <summary>Drops repeats (a small field collapses many candidates together), keeping order.</summary>
        private static List<WorldPoint> Distinct(List<WorldPoint> points)
        {
            List<WorldPoint> unique = new List<WorldPoint>(points.Count);
            foreach (WorldPoint point in points)
            {
                if (!unique.Contains(point))
                {
                    unique.Add(point);
                }
            }

            return unique;
        }
    }
}
