using System;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Per-player lock after a first aid kit is used, so a stack of kits cannot out-heal the zone.
    /// </summary>
    /// <remarks>
    ///     Measured in match time, so a pause does not spend the cooldown. Zero switches it off.
    /// </remarks>
    public sealed class MedKitCooldown
    {
        private readonly TimeSpan cooldown;

        private TimeSpan readyAt;

        /// <param name="cooldown">
        ///     How long after a kit before the next one may be used, in match time. Zero is off.
        /// </param>
        public MedKitCooldown(TimeSpan cooldown)
        {
            if (cooldown < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cooldown), cooldown,
                    "A cooldown does not run backwards. Write 0s to leave the game's own behaviour alone.");
            }

            this.cooldown = cooldown;
        }

        /// <summary>Whether the rule does anything at all.</summary>
        public bool IsEnforced => cooldown > TimeSpan.Zero;

        /// <summary>Whether a kit may be used now.</summary>
        public bool IsReady(TimeSpan matchNow) => !IsEnforced || matchNow >= readyAt;

        /// <summary>
        ///     Records a kit being used.
        /// </summary>
        /// <remarks>
        ///     Sets the ready time from <paramref name="matchNow"/> rather than extending it, so two
        ///     uses in the same instant lock only once.
        /// </remarks>
        public void Used(TimeSpan matchNow)
        {
            if (!IsEnforced)
            {
                return;
            }

            readyAt = matchNow + cooldown;
        }

        /// <summary>What is left of the lock, for the message on the player's own screen. Never negative.</summary>
        public TimeSpan Remaining(TimeSpan matchNow)
        {
            if (!IsEnforced)
            {
                return TimeSpan.Zero;
            }

            TimeSpan left = readyAt - matchNow;

            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>A new match. Per-player state belongs to the match rather than to the group.</summary>
        public void Ready()
        {
            readyAt = TimeSpan.Zero;
        }

        public override string ToString() =>
            IsEnforced
                ? Lms.Core.Diagnostics.Readable.Line($"a kit every {cooldown.TotalSeconds:0.#}s")
                : "kits have no cooldown";
    }
}
