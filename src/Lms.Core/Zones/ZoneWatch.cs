using System;

namespace Lms.Core.Zones
{
    /// <summary>Watches one player against the wall and decides, tick by tick, what it costs them.</summary>
    /// <remarks>
    ///     Tick-driven and pure: the adapter supplies the sampled position, the computed zone and
    ///     the frame delta.
    /// </remarks>
    public sealed class ZoneWatch
    {
        private readonly ZonePolicy policy;

        private TimeSpan sinceLastTick;
        private bool wasOutside;

        public ZoneWatch(ZonePolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>Assesses one frame and returns the verdict and any damage due.</summary>
        /// <param name="position">Where the player is, this frame.</param>
        /// <param name="zone">Where the wall is, from <see cref="ZoneSchedule.At"/>.</param>
        /// <param name="delta">Time since the last assessment.</param>
        public ZoneAssessment Update(WorldPoint position, ZoneState zone, TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "Match time does not run backwards.");
            }

            double outside = zone.Sphere.DistanceOutside(position);

            // Contains rather than "distance is zero": a closed zone holds nobody, even at its centre.
            if (zone.Sphere.Contains(position))
            {
                // Re-entering resets the interval, so briefly dipping out is not punished.
                sinceLastTick = TimeSpan.Zero;
                wasOutside = false;

                // No warning for the final collapse: everyone is outside a zero-radius sphere.
                bool warned = zone.NextSphere.HasValue
                              && zone.NextSphere.Value.Radius > 0
                              && !zone.NextSphere.Value.Contains(position);
                return new ZoneAssessment(warned ? ZoneVerdict.Warning : ZoneVerdict.Inside,
                    0, 0, 0, 0, TimeSpan.Zero);
            }

            if (!wasOutside)
            {
                wasOutside = true;
                sinceLastTick = TimeSpan.Zero;
            }

            sinceLastTick += delta;

            // A loop so a long frame cannot skip ticks: damage depends on time outside, not framerate.
            int ticks = 0;
            while (sinceLastTick >= policy.DamageInterval)
            {
                sinceLastTick -= policy.DamageInterval;
                ticks++;
            }

            double exposure = outside / policy.FullExposureAt;
            if (exposure > 1)
            {
                exposure = 1;
            }

            return new ZoneAssessment(ZoneVerdict.Outside, outside, exposure,
                ticks, zone.DamagePerTick, policy.DamageInterval - sinceLastTick);
        }
    }
}
