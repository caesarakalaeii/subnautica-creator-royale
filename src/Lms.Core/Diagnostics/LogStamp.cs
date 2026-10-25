using System;
using System.Globalization;

namespace Lms.Core.Diagnostics
{
    /// <summary>
    ///     The prefix on every log line the mod writes: UTC wall time for correlating with other records,
    ///     plus the server-synchronised world clock, which is exact across clients and so is used for sorting.
    /// </summary>
    public static class LogStamp
    {
        /// <summary>The tag every line starts with.</summary>
        public const string Name = "LMS";

        /// <summary>
        ///     The prefix for one line.
        /// </summary>
        /// <param name="utcNow">Current time; converted to UTC rather than trusted to be in it.</param>
        /// <param name="world">
        ///     World time, or null in the main menu. Null rather than zero, because zero is a real world time.
        /// </param>
        public static string Prefix(DateTimeOffset utcNow, TimeSpan? world)
        {
            string when = utcNow.ToUniversalTime()
                .ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

            if (!world.HasValue)
            {
                return "[" + Name + " " + when + "Z]";
            }

            // One decimal: finer than the tick that writes most lines, short enough for a multi-day world.
            string seconds = world.Value.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

            return "[" + Name + " " + when + "Z w" + seconds + "]";
        }
    }
}
