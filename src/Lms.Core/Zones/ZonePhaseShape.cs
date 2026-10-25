using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>
    ///     A <see cref="ZonePhase"/> without a centre: radius, window and damage. Used when the
    ///     destination is drawn, so pacing stays authored and only the direction varies.
    /// </summary>
    public sealed class ZonePhaseShape
    {
        public ZonePhaseShape(double radius, TimeSpan shrinkFor, double damagePerTick)
        {
            if (!(radius > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius,
                    "A phase closes to a sphere with something in it; the close to nothing is the collapse, and nobody authors that.");
            }

            if (shrinkFor < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(shrinkFor), shrinkFor, "A zone does not close backwards.");
            }

            if (!(damagePerTick > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(damagePerTick), damagePerTick,
                    "A phase that does no damage is a phase with no zone in it.");
            }

            Radius = radius;
            ShrinkFor = shrinkFor;
            DamagePerTick = damagePerTick;
        }

        /// <summary>How wide the wall stands once this phase has finished closing.</summary>
        public double Radius { get; }

        /// <summary>How long the wall takes to travel, inside the cycle it belongs to.</summary>
        public TimeSpan ShrinkFor { get; }

        /// <summary>Health taken from a player outside the wall, once per <c>zone.damageInterval</c>.</summary>
        public double DamagePerTick { get; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "r{0:0} m over {1:0}s, {2:0.#}/tick",
                Radius, ShrinkFor.TotalSeconds, DamagePerTick);
    }
}
