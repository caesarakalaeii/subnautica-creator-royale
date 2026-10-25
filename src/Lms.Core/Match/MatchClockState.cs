namespace Lms.Core.Match
{
    /// <summary>Where a match clock is in its life.</summary>
    public enum MatchClockState
    {
        /// <summary>Built but not started. No time passes and no culls fall due.</summary>
        NotStarted,

        /// <summary>Running: ticks accrue and culls fall due on the cadence.</summary>
        Running,

        /// <summary>Paused by an admin. Ticks are ignored until resumed.</summary>
        Paused,
    }
}
