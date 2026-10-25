namespace Lms.Core.Match
{
    /// <summary>What the clock is telling the caller about.</summary>
    public enum MatchClockEventKind
    {
        /// <summary>The warning window for the next phase has opened. Raised once per phase.</summary>
        PhaseWarning,

        /// <summary>A phase is due now: the wall starts closing on a smaller sphere .</summary>
        PhaseStarted,
    }
}
