using System;

namespace Lms.Core.Match
{
    /// <summary>Tells a settings-tab button double-clicked from one pressed again.</summary>
    /// <remarks>
    ///     The same verb again within <see cref="Window"/> of the last one that ran is the same press;
    ///     a different verb is never held back.
    /// </remarks>
    public sealed class ButtonPresses
    {
        /// <summary>How long a repeat of the same button counts as the same press.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

        private AdminCommand? last;
        private TimeSpan lastAt;

        /// <summary>Whether this press should run, remembering it if it should.</summary>
        public bool Admit(AdminCommand command, TimeSpan now)
        {
            if (command == last && now - lastAt < Window)
            {
                return false;
            }

            last = command;
            lastAt = now;

            return true;
        }
    }
}
