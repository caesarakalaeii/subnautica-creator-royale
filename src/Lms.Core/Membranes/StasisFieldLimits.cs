using System;

namespace Lms.Core.Membranes
{
    /// <summary>
    ///     A stasis field's radius and lifetime at a tap and at full charge; the rifle lerps
    ///     between them on the normalised charge.
    /// </summary>
    /// <remarks>
    ///     Mirrors <c>StasisSphere.minRadius</c>, <c>maxRadius</c>, <c>minTime</c> and <c>maxTime</c>.
    ///     The prefab may override the class defaults, so the adapter reads them from the live
    ///     component; <see cref="GameDefaults"/> is only a fallback.
    /// </remarks>
    public sealed class StasisFieldLimits
    {
        public StasisFieldLimits(double minRadius, double maxRadius, TimeSpan minLifetime, TimeSpan maxLifetime)
        {
            if (!(minRadius > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(minRadius), minRadius, "A field with no radius is not a field.");
            }

            if (maxRadius < minRadius)
            {
                throw new ArgumentException("A full charge cannot leave a smaller ring than a tap.", nameof(maxRadius));
            }

            if (minLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minLifetime), minLifetime, "A field that ends the instant it lands has nobody in it.");
            }

            if (maxLifetime < minLifetime)
            {
                throw new ArgumentException("A full charge cannot last less time than a tap.", nameof(maxLifetime));
            }

            MinRadius = minRadius;
            MaxRadius = maxRadius;
            MinLifetime = minLifetime;
            MaxLifetime = maxLifetime;
        }

        /// <summary>
        ///     The values compiled into <c>StasisSphere</c>: 1 m for 4 s at a tap, 10 m for 20 s at
        ///     a full charge.
        /// </summary>
        public static StasisFieldLimits GameDefaults { get; } =
            new StasisFieldLimits(1, 10, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20));

        public double MinRadius { get; }

        public double MaxRadius { get; }

        public TimeSpan MinLifetime { get; }

        public TimeSpan MaxLifetime { get; }

        /// <summary>The radius a shot at this charge leaves: <c>Mathf.Lerp</c>, clamp included.</summary>
        internal double RadiusFor(double charge) => MinRadius + (MaxRadius - MinRadius) * Clamp01(charge);

        internal TimeSpan LifetimeFor(double charge) =>
            MinLifetime + TimeSpan.FromTicks((long)((MaxLifetime - MinLifetime).Ticks * Clamp01(charge)));

        private static double Clamp01(double value)
        {
            if (!(value > 0))
            {
                // Also catches NaN.
                return 0;
            }

            return value < 1 ? value : 1;
        }
    }
}
