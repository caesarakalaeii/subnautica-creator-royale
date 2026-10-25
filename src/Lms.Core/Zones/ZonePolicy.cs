using System;

namespace Lms.Core.Zones
{
    /// <summary>The knobs on being outside the wall, from the server config.</summary>
    public sealed class ZonePolicy
    {
        public ZonePolicy(TimeSpan damageInterval, double fullExposureAt)
        {
            if (damageInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(damageInterval), damageInterval,
                    "A tick every no time is continuous damage, which is not what a tick is.");
            }

            if (!(fullExposureAt > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(fullExposureAt), fullExposureAt,
                    "The screen has to have somewhere to ramp to, in metres past the wall.");
            }

            DamageInterval = damageInterval;
            FullExposureAt = fullExposureAt;
        }

        /// <summary>How often a player outside the wall is bitten. Two seconds by default.</summary>
        public TimeSpan DamageInterval { get; }

        /// <summary>How many metres past the wall the screen effect saturates. Visual only.</summary>
        public double FullExposureAt { get; }
    }
}
