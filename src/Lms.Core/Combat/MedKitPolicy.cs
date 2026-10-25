using System;
using Lms.Core.Diagnostics;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     How often a first aid kit may be used. Zero switches the rule off.
    /// </summary>
    /// <remarks>
    ///     Enforced on the client, since healing is local to the player's own <c>LiveMixin</c>;
    ///     the server's copy decides nothing.
    /// </remarks>
    public sealed class MedKitPolicy
    {
        public MedKitPolicy(TimeSpan cooldown)
        {
            if (cooldown < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cooldown), cooldown,
                    "A cooldown does not run backwards. Write 0s to leave the game's own behaviour alone.");
            }

            Cooldown = cooldown;
        }

        /// <summary>How long after a kit before the next one may be used, in match time. Zero is off.</summary>
        public TimeSpan Cooldown { get; }

        /// <summary>Whether the rule does anything at all.</summary>
        public bool IsEnforced => Cooldown > TimeSpan.Zero;

        /// <summary>
        ///     Off, which is what a config that says nothing gets.
        /// </summary>
        public static MedKitPolicy Default { get; } = new MedKitPolicy(TimeSpan.Zero);

        public override string ToString() =>
            IsEnforced
                ? Readable.Line($"a first aid kit every {Cooldown.TotalSeconds:0.#}s")
                : "first aid kits have no cooldown";
    }
}
