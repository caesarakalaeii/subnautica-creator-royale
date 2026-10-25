using System;
using System.Globalization;
using System.Text;

namespace Lms.Core.Diagnostics
{
    /// <summary>
    ///     Formats log and diagnostic lines with the invariant culture, so numbers read the same whatever
    ///     locale the writing machine uses. On-screen HUD text stays in the player's locale.
    /// </summary>
    public static class Readable
    {
        /// <summary>One line, formatted invariantly.</summary>
        public static string Line(FormattableString line) =>
            line.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        ///     Several fragments, each formatted invariantly and joined with nothing between them.
        /// </summary>
        /// <remarks>
        ///     <c>$"…" + $"…"</c> concatenates to a plain <see cref="string"/> before culture can be applied,
        ///     so multi-part lines pass the fragments as separate arguments instead.
        /// </remarks>
        public static string Line(params FormattableString[] parts)
        {
            StringBuilder line = new StringBuilder(parts.Length * 48);

            foreach (FormattableString part in parts)
            {
                line.Append(part.ToString(CultureInfo.InvariantCulture));
            }

            return line.ToString();
        }
    }
}
