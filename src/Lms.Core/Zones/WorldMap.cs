using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>A place on a flat map, as a fraction of it. Origin top left, north up.</summary>
    /// <remarks>Fractions rather than pixels, so renderers of different sizes agree.</remarks>
    public readonly struct MapPoint : IEquatable<MapPoint>
    {
        internal MapPoint(double u, double v)
        {
            U = u;
            V = v;
        }

        /// <summary>Across, nought at the west edge and one at the east.</summary>
        public double U { get; }

        /// <summary>Down, nought at the north edge and one at the south.</summary>
        public double V { get; }

        /// <summary>Whether this falls inside the map rather than past its edge.</summary>
        public bool IsOnTheMap => U >= 0 && U <= 1 && V >= 0 && V <= 1;

        public bool Equals(MapPoint other) => U.Equals(other.U) && V.Equals(other.V);

        public override bool Equals(object? obj) => obj is MapPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return U.GetHashCode() * 397 ^ V.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####})", U, V);
    }

    /// <summary>
    ///     The world as a flat map: one north-up transform shared by the minimap and the caster map.
    /// </summary>
    /// <remarks>
    ///     <c>./scripts/make-minimap</c> writes its first row at world z = -2048, so its image is
    ///     south-up. Renderers flip the image (see <see cref="ImageIsSouthUp"/>), never the transform,
    ///     or the map comes out mirrored.
    /// </remarks>
    public static class WorldMap
    {
        /// <summary>How wide the world is, in metres, centred on the origin.</summary>
        /// <remarks>
        ///     Fixed by the game: its start-point map reads a normalised point as
        ///     <c>4096 * n - 2048</c>.
        /// </remarks>
        public const double SpanMetres = 4096;

        /// <summary>
        ///     Whether the image <c>./scripts/make-minimap</c> writes is stored upside down against
        ///     this transform. It is, and a renderer has to flip it.
        /// </summary>
        public const bool ImageIsSouthUp = true;

        /// <summary>Where a place falls on the map, with north at the top.</summary>
        /// <remarks>
        ///     Not clamped: a player past the world's edge is outside the wall, and
        ///     <see cref="MapPoint.IsOnTheMap"/> lets a renderer decide. Depth is dropped (plan view).
        /// </remarks>
        public static MapPoint NorthUp(WorldPoint where) =>
            new MapPoint((where.X + SpanMetres / 2) / SpanMetres,
                1 - (where.Z + SpanMetres / 2) / SpanMetres);

        /// <summary>A distance in metres as a fraction of the map, for a radius.</summary>
        /// <remarks>The same fraction on both axes, so a sphere draws as a circle.</remarks>
        public static double Fraction(double metres) => metres / SpanMetres;
    }
}
