using System;
using System.Globalization;

namespace Lms.Core.Match
{
    /// <summary>How a match's clock is written down for a person to read mid-match.</summary>
    internal static class MatchTime
    {
        /// <summary>Minutes and seconds, with minutes not wrapping at the hour: 75:00, not 15:00.</summary>
        internal static string Countdown(TimeSpan span) =>
            ((int)span.TotalMinutes).ToString("00", CultureInfo.InvariantCulture) + ":" +
            span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }
}
