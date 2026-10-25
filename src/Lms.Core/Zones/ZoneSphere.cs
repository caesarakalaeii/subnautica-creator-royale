using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>The safe volume at one moment: a centre and a radius, in three dimensions.</summary>
    /// <remarks>A true sphere, not a cylinder: depth counts exactly as much as horizontal distance.</remarks>
    public readonly struct ZoneSphere : IEquatable<ZoneSphere>
    {
        public ZoneSphere(WorldPoint centre, double radius)
        {
            if (!(radius >= 0) || double.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius,
                    "A zone cannot have a negative radius, and a wall has to be somewhere.");
            }

            Centre = centre;
            Radius = radius;
        }

        public WorldPoint Centre { get; }

        /// <summary>Metres from the centre to the wall.</summary>
        public double Radius { get; }

        /// <summary>
        ///     Whether a player at this point is safe. The surface counts as inside, so a player on
        ///     the boundary does not flicker on rounding noise.
        /// </summary>
        /// <remarks>A closed zone (radius zero) holds nobody, not even its exact centre.</remarks>
        public bool Contains(WorldPoint point) => Radius > 0 && Centre.DistanceTo(point) <= Radius;

        /// <summary>
        ///     How wide the wall is at one depth: the circle where this sphere meets the horizontal
        ///     plane at <paramref name="y"/>, or zero when the plane misses it.
        /// </summary>
        /// <remarks>
        ///     <see cref="Radius"/> is the equator, so a plan view drawn from it overstates how far
        ///     inside a player is away from the centre's depth. Zero means nowhere at that depth is
        ///     inside, which is not the same as the zone having closed.
        /// </remarks>
        public double RadiusAtHeight(double y)
        {
            if (Radius <= 0)
            {
                return 0;
            }

            double offset = y - Centre.Y;
            double squared = Radius * Radius - offset * offset;

            return squared <= 0 ? 0 : Math.Sqrt(squared);
        }

        /// <summary>Whether the whole of <paramref name="other"/> fits inside this sphere.</summary>
        /// <remarks>
        ///     The battle-royale invariant the validator checks per phase: the safe volume never
        ///     jumps somewhere a safe player cannot reach.
        /// </remarks>
        public bool Contains(ZoneSphere other) => Centre.DistanceTo(other.Centre) + other.Radius <= Radius;

        /// <summary>How far past the wall a player is, or zero if they are inside it.</summary>
        public double DistanceOutside(WorldPoint point)
        {
            double beyond = Centre.DistanceTo(point) - Radius;
            return beyond > 0 ? beyond : 0;
        }

        /// <summary>
        ///     The sphere <paramref name="fraction"/> of the way through a shrink from
        ///     <paramref name="from"/> to <paramref name="to"/>. Clamped, so an overrun holds at the
        ///     destination rather than shrinking past it.
        /// </summary>
        public static ZoneSphere Between(ZoneSphere from, ZoneSphere to, double fraction)
        {
            if (fraction <= 0)
            {
                return from;
            }

            if (fraction >= 1)
            {
                return to;
            }

            return new ZoneSphere(
                from.Centre.Towards(to.Centre, fraction),
                from.Radius + (to.Radius - from.Radius) * fraction);
        }

        public bool Equals(ZoneSphere other) => Centre.Equals(other.Centre) && Radius.Equals(other.Radius);

        public override bool Equals(object? obj) => obj is ZoneSphere other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Centre.GetHashCode() * 397 ^ Radius.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} r{1:0}m", Centre, Radius);
    }
}
