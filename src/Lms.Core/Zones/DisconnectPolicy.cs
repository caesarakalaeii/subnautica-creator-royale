using System;
using Lms.Core.Diagnostics;

namespace Lms.Core.Zones
{
    /// <summary>How long a disconnect is forgiven before the wall starts counting.</summary>
    /// <remarks>
    ///     A disconnected client runs no zone watch and Nitrox removes its body, so without this
    ///     pulling the cable would be cheaper than swimming. A grace longer than a match turns the
    ///     rule off; whether the bill costs health is governed by <c>flags.enforceDamage</c>.
    /// </remarks>
    public sealed class DisconnectPolicy
    {
        public DisconnectPolicy(TimeSpan grace)
        {
            if (grace < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(grace), grace,
                    "A grace period does not run backwards. Write 0s to forgive nothing.");
            }

            Grace = grace;
        }

        /// <summary>How much of an absence is forgiven, in match time so a pause does not spend it.</summary>
        public TimeSpan Grace { get; }

        /// <summary>Ninety seconds, used when the config does not set <c>disconnect.grace</c>.</summary>
        /// <remarks>
        ///     Long enough for a Nitrox reconnect (back to the main menu and a world re-sync), short
        ///     enough that nobody sits out a phase. Only time spent outside the wall is billed.
        /// </remarks>
        public static DisconnectPolicy Default { get; } = new DisconnectPolicy(TimeSpan.FromSeconds(90));

        public override string ToString() =>
            Grace > TimeSpan.Zero
                ? Readable.Line($"{Grace.TotalSeconds:0.#}s of a disconnect forgiven")
                : "a disconnect is forgiven nothing";
    }
}
