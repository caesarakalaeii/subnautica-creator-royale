using System;
using Lms.Core.Match;

namespace Lms.Core.Telemetry
{
    /// <summary>The match state the booth shows.</summary>
    /// <remarks>
    ///     <see cref="UntilNextPhase"/> is sent rather than derived by the browser because the server
    ///     owns the clock.
    /// </remarks>
    public sealed class MatchTelemetry
    {
        public MatchTelemetry(int group, int generation, MatchClockState state, TimeSpan elapsed,
            int phase, int phases, TimeSpan? untilNextPhase, int alive)
        {
            Group = group;
            Generation = generation;
            State = state;
            Elapsed = elapsed;
            Phase = phase;
            Phases = phases;
            UntilNextPhase = untilNextPhase;
            Alive = alive;
        }

        /// <summary>Which group's match.</summary>
        public int Group { get; }

        /// <summary>Which attempt at it. A reset makes a new one.</summary>
        public int Generation { get; }

        public MatchClockState State { get; }

        /// <summary>How far into the match, on the server's clock.</summary>
        public TimeSpan Elapsed { get; }

        /// <summary>Phases fired so far. Zero is the lobby.</summary>
        public int Phase { get; }

        /// <summary>How many there are, the final collapse included.</summary>
        public int Phases { get; }

        /// <summary>The countdown, or null when nothing is due: the lobby, a pause, the end.</summary>
        public TimeSpan? UntilNextPhase { get; }

        /// <summary>How many players are still in it.</summary>
        public int Alive { get; }
    }
}
