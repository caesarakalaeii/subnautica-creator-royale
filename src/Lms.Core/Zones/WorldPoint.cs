using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>A point in the world, in metres, with no game types attached.</summary>
    /// <remarks>
    ///     Core cannot reference <c>UnityEngine.Vector3</c>; the adapter converts at the boundary.
    ///     Double rather than float: at a 4000 m world, float noise can flip a containment verdict.
    /// </remarks>
    public readonly struct WorldPoint : IEquatable<WorldPoint>
    {
        public WorldPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>East-west. The world spans roughly -2000 to 2000.</summary>
        public double X { get; }

        /// <summary>Height. Zero is the sea surface, so anywhere playable is negative.</summary>
        public double Y { get; }

        /// <summary>North-south. The world spans roughly -2000 to 2000.</summary>
        public double Z { get; }

        /// <summary>How deep this is, as the game says it: a positive number below the surface.</summary>
        public double Depth => -Y;

        public double DistanceTo(WorldPoint other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            double dz = Z - other.Z;

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>The point <paramref name="fraction"/> of the way from here to <paramref name="other"/>.</summary>
        public WorldPoint Towards(WorldPoint other, double fraction) =>
            new WorldPoint(
                X + (other.X - X) * fraction,
                Y + (other.Y - Y) * fraction,
                Z + (other.Z - Z) * fraction);

        public bool Equals(WorldPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj) => obj is WorldPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = hash * 397 ^ Y.GetHashCode();
                return hash * 397 ^ Z.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0}, {1:0}, {2:0})", X, Y, Z);
    }
}
