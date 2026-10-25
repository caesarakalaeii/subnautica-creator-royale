using System;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>Where a following camera sits relative to the person it is filming.</summary>
    /// <remarks>
    ///     <see cref="Open"/> is the cut; <see cref="Keep"/> runs every frame after, clamping into a
    ///     distance band rather than pinning, so the caster can still orbit and dolly.
    /// </remarks>
    public static class CasterShot
    {
        /// <summary>How far out the cut puts the camera, in metres. Far enough that a Seamoth fits.</summary>
        public const double OpeningDistance = 12;

        /// <summary>How far above the player the cut puts the camera, in metres: a shallow downward angle.</summary>
        public const double OpeningHeight = 4;

        /// <summary>
        ///     The nearest the camera is allowed to be pushed, in metres, so it never ends up inside the
        ///     model.
        /// </summary>
        public const double ClosestKeep = 3;

        /// <summary>The furthest the camera may drift before it is pulled back, in metres. A loose leash.</summary>
        public const double FurthestKeep = 40;

        /// <summary>
        ///     A stable bearing along <c>-Z</c> for when the camera has none, e.g. <c>follow</c> right
        ///     after <c>lms look</c>; normalising a zero vector would yield NaN.
        /// </summary>
        private static readonly WorldPoint SomeBearing = new WorldPoint(0, 0, -1);

        /// <summary>Below this, a vector has no direction worth normalising.</summary>
        private const double TooShort = 0.001;

        /// <summary>
        ///     The cut: where the camera goes when an operator says to follow somebody. Keeps the
        ///     camera's current bearing and imposes only distance and height.
        /// </summary>
        public static WorldPoint Open(WorldPoint camera, WorldPoint target, double distance,
            double height)
        {
            if (!(distance > 0))
            {
                distance = OpeningDistance;
            }

            if (double.IsNaN(height) || double.IsInfinity(height))
            {
                height = OpeningHeight;
            }

            WorldPoint bearing = Bearing(camera, target);

            return new WorldPoint(
                target.X + bearing.X * distance,
                target.Y + height,
                target.Z + bearing.Z * distance);
        }

        /// <summary>
        ///     Every frame after the cut: moves the camera with the target since <paramref name="targetWas"/>,
        ///     then clamps it into the band. A camera already inside the band is left alone.
        /// </summary>
        public static WorldPoint Keep(WorldPoint camera, WorldPoint target, WorldPoint targetWas,
            double closest, double furthest)
        {
            if (!(closest > 0) || !(furthest > closest))
            {
                closest = ClosestKeep;
                furthest = FurthestKeep;
            }

            camera = new WorldPoint(
                camera.X + (target.X - targetWas.X),
                camera.Y + (target.Y - targetWas.Y),
                camera.Z + (target.Z - targetWas.Z));

            double away = camera.DistanceTo(target);

            if (away >= closest && away <= furthest)
            {
                return camera;
            }

            double wanted = away < closest ? closest : furthest;

            if (away < TooShort)
            {
                // Standing on them: use the same fallback bearing as Open.
                return new WorldPoint(
                    target.X + SomeBearing.X * wanted,
                    target.Y,
                    target.Z + SomeBearing.Z * wanted);
            }

            double scale = wanted / away;

            return new WorldPoint(
                target.X + (camera.X - target.X) * scale,
                target.Y + (camera.Y - target.Y) * scale,
                target.Z + (camera.Z - target.Z) * scale);
        }

        /// <summary>
        ///     The horizontal unit vector from the target towards the camera; height is imposed separately.
        /// </summary>
        private static WorldPoint Bearing(WorldPoint camera, WorldPoint target)
        {
            double x = camera.X - target.X;
            double z = camera.Z - target.Z;
            double flat = Math.Sqrt(x * x + z * z);

            return flat < TooShort
                ? SomeBearing
                : new WorldPoint(x / flat, 0, z / flat);
        }
    }
}
