using System;
using Lms.Core.Diagnostics;

namespace Lms.Core.Audit
{
    /// <summary>Server-side audit settings: how often player positions are sampled.</summary>
    public sealed class AuditPolicy
    {
        public AuditPolicy(TimeSpan positionInterval)
        {
            if (positionInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(positionInterval), positionInterval,
                    "A cadence does not run backwards. Write 0s to stop sampling positions.");
            }

            PositionInterval = positionInterval;
        }

        /// <summary>How often each player's position is recorded, or zero for never.</summary>
        /// <remarks>Measured in match time, so a pause stops the cadence.</remarks>
        public TimeSpan PositionInterval { get; }

        /// <summary>Whether positions are sampled at all.</summary>
        public bool SamplesPositions => PositionInterval > TimeSpan.Zero;

        /// <summary>The default: every five seconds (about a megabyte an hour for 25 players).</summary>
        public static AuditPolicy Default { get; } = new AuditPolicy(TimeSpan.FromSeconds(5));

        public override string ToString() =>
            SamplesPositions
                ? Readable.Line($"positions every {PositionInterval.TotalSeconds:0.#}s")
                : "positions not sampled";
    }
}
