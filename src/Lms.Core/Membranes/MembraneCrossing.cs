using System;
using Lms.Core.Zones;

namespace Lms.Core.Membranes
{
    /// <summary>What one frame of movement did to the ring's wall, and where it happened.</summary>
    /// <remarks>
    ///     Tests the whole segment, not just the endpoint, so a fast player cannot pass through a
    ///     small field between samples. The wall is treated as static for the frame; it shrinks by
    ///     under a metre a second.
    /// </remarks>
    public readonly struct MembraneCrossing
    {
        private MembraneCrossing(MembraneCrossingKind kind, WorldPoint? entry, WorldPoint? exit)
        {
            Kind = kind;
            Entry = entry;
            Exit = exit;
        }

        public MembraneCrossingKind Kind { get; }

        /// <summary>
        ///     Where the movement crossed inwards, for <see cref="MembraneCrossingKind.Entered"/>
        ///     and <see cref="MembraneCrossingKind.PassedThrough"/>. Null otherwise.
        /// </summary>
        public WorldPoint? Entry { get; }

        /// <summary>
        ///     Where the movement crossed outwards, for <see cref="MembraneCrossingKind.Exited"/>
        ///     and <see cref="MembraneCrossingKind.PassedThrough"/>; where a contained player is
        ///     brought back from. Null otherwise.
        /// </summary>
        public WorldPoint? Exit { get; }

        /// <summary>Whether the player was inside the wall when the frame began.</summary>
        public bool StartedInside => Kind == MembraneCrossingKind.Inside || Kind == MembraneCrossingKind.Exited;

        /// <summary>Whether the player is inside the wall now.</summary>
        public bool EndedInside => Kind == MembraneCrossingKind.Inside || Kind == MembraneCrossingKind.Entered;

        /// <summary>Whether the frame put the player in the ring at all, at the endpoints or in between.</summary>
        public bool TouchedTheRing => Kind != MembraneCrossingKind.Clear;

        /// <summary>Whether this frame's movement carried the player out through the wall.</summary>
        /// <remarks>
        ///     Distinguishes a physics escape (worth reporting) from the shrinking wall overtaking
        ///     a stationary player (normal). Both are <see cref="ContainmentVerdict.Escaped"/>.
        /// </remarks>
        public bool WentThroughTheWall =>
            Kind == MembraneCrossingKind.Exited || Kind == MembraneCrossingKind.PassedThrough;

        /// <summary>
        ///     What a move from <paramref name="from"/> to <paramref name="to"/> did to
        ///     <paramref name="wall"/>.
        /// </summary>
        public static MembraneCrossing Through(ZoneSphere wall, WorldPoint from, WorldPoint to)
        {
            // Ask the sphere so the crossing and the predicate agree about the surface.
            bool startedInside = wall.Contains(from);
            bool endedInside = wall.Contains(to);

            if (startedInside && endedInside)
            {
                // A sphere is convex, so there is no leaving and returning within one segment.
                return new MembraneCrossing(MembraneCrossingKind.Inside, null, null);
            }

            // A zero-radius field holds nobody; the maths below would find a crossing at its centre.
            if (wall.Radius <= 0)
            {
                return new MembraneCrossing(MembraneCrossingKind.Clear, null, null);
            }

            bool met = TryMeetings(wall, from, to, out double entersAt, out double leavesAt);

            if (startedInside)
            {
                // The far root, or the end of the frame if rounding at the surface lost it.
                return new MembraneCrossing(MembraneCrossingKind.Exited, null,
                    from.Towards(to, met ? leavesAt : 1));
            }

            if (endedInside)
            {
                return new MembraneCrossing(MembraneCrossingKind.Entered,
                    from.Towards(to, met ? entersAt : 0), null);
            }

            if (!met)
            {
                return new MembraneCrossing(MembraneCrossingKind.Clear, null, null);
            }

            // Both ends outside, but the segment passes through the field.
            return new MembraneCrossing(MembraneCrossingKind.PassedThrough,
                from.Towards(to, entersAt), from.Towards(to, leavesAt));
        }

        public override string ToString() => Kind.ToString();

        /// <summary>
        ///     Where the segment meets the sphere, as fractions along it. False if it misses, or if
        ///     both meetings fall outside this frame's travel.
        /// </summary>
        /// <remarks>Roots are clamped to [0, 1] so rounding cannot drop an endpoint already judged inside.</remarks>
        private static bool TryMeetings(ZoneSphere wall, WorldPoint from, WorldPoint to,
            out double entersAt, out double leavesAt)
        {
            entersAt = 0;
            leavesAt = 0;

            double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
            double fx = from.X - wall.Centre.X, fy = from.Y - wall.Centre.Y, fz = from.Z - wall.Centre.Z;

            double a = dx * dx + dy * dy + dz * dz;
            if (a <= 0)
            {
                // No movement: the predicate above already decided.
                return false;
            }

            double b = 2 * (fx * dx + fy * dy + fz * dz);
            double c = fx * fx + fy * fy + fz * fz - wall.Radius * wall.Radius;

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                return false;
            }

            // A tangent (double root) counts as touching, consistent with the surface being inside.
            double root = Math.Sqrt(discriminant);
            double first = (-b - root) / (2 * a);
            double last = (-b + root) / (2 * a);

            if (first > 1 || last < 0)
            {
                return false;
            }

            entersAt = Clamp01(first);
            leavesAt = Clamp01(last);
            return true;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
