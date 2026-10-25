using System;

namespace Lms.Core.Match
{
    /// <summary>Something the clock decided happened, for the adapter to apply and the audit log to record.</summary>
    public readonly struct MatchClockEvent
    {
        internal MatchClockEvent(MatchClockEventKind kind, int phaseNumber, TimeSpan atElapsed)
        {
            Kind = kind;
            PhaseNumber = phaseNumber;
            AtElapsed = atElapsed;
        }

        public MatchClockEventKind Kind { get; }

        /// <summary>Which phase this is about, counting from one.</summary>
        public int PhaseNumber { get; }

        /// <summary>
        ///     Match time this was scheduled for, not the tick that noticed it, so a frame hitch does
        ///     not move a phase in the audit log and a replay lands in the same place.
        /// </summary>
        public TimeSpan AtElapsed { get; }

        public override string ToString() => $"{Kind} #{PhaseNumber} at {AtElapsed}";
    }
}
