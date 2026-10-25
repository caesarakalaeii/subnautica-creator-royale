using System.Text;

namespace Lms.Core.Wire
{
    /// <summary>
    ///     Escaping shared by every tab-separated line the mod writes (audit log, report envelope), so a
    ///     tab or newline in a player name cannot split one record into two.
    /// </summary>
    public static class WireText
    {
        /// <summary>Escapes backslash, tab, CR and LF; backslash first so the others are not double-escaped.</summary>
        public static string Escape(string value) => value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        /// <summary>Reverses <see cref="Escape"/>.</summary>
        public static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            StringBuilder unescaped = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 == value.Length)
                {
                    unescaped.Append(value[i]);
                    continue;
                }

                i++;
                switch (value[i])
                {
                    case 't':
                        unescaped.Append('\t');
                        break;

                    case 'r':
                        unescaped.Append('\r');
                        break;

                    case 'n':
                        unescaped.Append('\n');
                        break;

                    case '\\':
                        unescaped.Append('\\');
                        break;

                    default:
                        // Unknown escape: kept verbatim so a value from a newer writer survives an older reader.
                        unescaped.Append('\\').Append(value[i]);
                        break;
                }
            }

            return unescaped.ToString();
        }
    }
}
