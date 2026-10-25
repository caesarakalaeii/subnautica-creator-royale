using System;
using System.Collections.Generic;

namespace Lms.Core.Match
{
    /// <summary>Drives the phase cadence for one match.</summary>
    /// <remarks>
    ///     Never reads a wall clock: the owner drives it to a match time, so it is testable and
    ///     replayable. A skip moves the <see cref="MatchAnchor"/> and <see cref="MatchDirector"/> rebuilds this clock.
    /// </remarks>
    public sealed class MatchClock
    {
        private static readonly IReadOnlyList<MatchClockEvent> NothingHappened = new MatchClockEvent[0];

        private readonly MatchClockSettings settings;
        private readonly int? totalPhases;

        private TimeSpan elapsed;
        private TimeSpan nextPhaseAt;
        private bool warningRaised;

        /// <param name="settings">The cadence this match runs on.</param>
        /// <param name="totalPhases">
        ///     How many phases the schedule has, after which the clock stops scheduling them. Null
        ///     means open-ended.
        /// </param>
        public MatchClock(MatchClockSettings settings, int? totalPhases = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (totalPhases < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalPhases), totalPhases, "A match cannot have fewer than no phases.");
            }

            this.totalPhases = totalPhases;
            nextPhaseAt = settings.OpeningLength;
        }

        public MatchClockState State { get; private set; } = MatchClockState.NotStarted;

        /// <summary>Match time so far. Paused time does not count.</summary>
        public TimeSpan Elapsed => elapsed;

        /// <summary>How many phases have fired.</summary>
        public int CompletedPhases { get; private set; }

        /// <summary>
        ///     How long ago the current phase was scheduled to start, which the zone shrinks from.
        ///     Measured from the schedule, not the tick, so every client draws the same wall.
        /// </summary>
        public TimeSpan SinceLastPhase
        {
            get
            {
                TimeSpan age = elapsed - (nextPhaseAt - CurrentWindow);
                return age > TimeSpan.Zero ? age : TimeSpan.Zero;
            }
        }

        /// <summary>
        ///     How long the window now running lasts: the opening before any phase has fired, the
        ///     cycle after that. <see cref="SinceLastPhase"/> depends on this being the right one.
        /// </summary>
        private TimeSpan CurrentWindow =>
            CompletedPhases == 0 ? settings.OpeningLength : settings.CycleLength;

        /// <summary>
        ///     True once the schedule has no phases left. Time keeps passing: the fight in the last
        ///     sphere is the rest of the match, and only the closing is over.
        /// </summary>
        public bool AllPhasesFired => totalPhases.HasValue && CompletedPhases >= totalPhases.Value;

        /// <summary>Time until the next phase, for the HUD countdown. Zero once the schedule is spent.</summary>
        public TimeSpan UntilNextPhase
        {
            get
            {
                if (AllPhasesFired)
                {
                    return TimeSpan.Zero;
                }

                TimeSpan remaining = nextPhaseAt - elapsed;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        /// <summary>
        ///     True between a warning and the phase it warned about. Stays true while paused: the
        ///     phase is still coming, and the HUD should keep saying so.
        /// </summary>
        public bool IsWarningActive => warningRaised;

        /// <summary>Starts the match. Ignored if it is already running — admin buttons get double-pressed.</summary>
        public void Start()
        {
            if (State == MatchClockState.NotStarted)
            {
                State = MatchClockState.Running;
            }
        }

        /// <summary>Pauses a running match. Ignored otherwise.</summary>
        public void Pause()
        {
            if (State == MatchClockState.Running)
            {
                State = MatchClockState.Paused;
            }
        }

        /// <summary>Resumes a paused match. Ignored otherwise.</summary>
        public void Resume()
        {
            if (State == MatchClockState.Paused)
            {
                State = MatchClockState.Running;
            }
        }

        /// <summary>Returns the clock to its pre-match state, phase count included.</summary>
        public void Reset()
        {
            State = MatchClockState.NotStarted;
            elapsed = TimeSpan.Zero;
            nextPhaseAt = settings.OpeningLength;
            warningRaised = false;
            CompletedPhases = 0;
        }

        /// <summary>
        ///     Moves the clock to a match time from the <see cref="MatchAnchor"/>, and returns what fell due.
        /// </summary>
        /// <remarks>
        ///     A target in the past is ignored: the synced world clock can step back a few milliseconds,
        ///     and rewinding would fire a phase twice.
        /// </remarks>
        public IReadOnlyList<MatchClockEvent> AdvanceTo(TimeSpan matchTime)
        {
            TimeSpan delta = matchTime - elapsed;

            return delta > TimeSpan.Zero ? Advance(delta) : NothingHappened;
        }

        /// <summary>Advances the clock and returns what fell due, in order.</summary>
        /// <param name="delta">Time since the last tick. A delta longer than a cycle is fine.</param>
        public IReadOnlyList<MatchClockEvent> Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "Match time does not run backwards.");
            }

            if (State != MatchClockState.Running || delta == TimeSpan.Zero)
            {
                return NothingHappened;
            }

            TimeSpan target = elapsed + delta;

            if (AllPhasesFired)
            {
                elapsed = target;
                return NothingHappened;
            }

            List<MatchClockEvent> events = new List<MatchClockEvent>();

            // A loading screen or frame hitch can hand over a delta covering several cycles.
            while (true)
            {
                TimeSpan warningAt = nextPhaseAt - settings.WarningLead;
                if (!warningRaised && target >= warningAt)
                {
                    warningRaised = true;
                    events.Add(new MatchClockEvent(MatchClockEventKind.PhaseWarning, CompletedPhases + 1, warningAt));
                }

                if (target < nextPhaseAt)
                {
                    break;
                }

                CompletedPhases++;
                events.Add(new MatchClockEvent(MatchClockEventKind.PhaseStarted, CompletedPhases, nextPhaseAt));
                nextPhaseAt += settings.CycleLength;
                warningRaised = false;

                if (AllPhasesFired)
                {
                    break;
                }
            }

            elapsed = target;
            return events;
        }
    }
}
