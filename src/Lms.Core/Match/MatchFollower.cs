using System;
using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>The client's side of the match: it follows one rather than running one.</summary>
    /// <remarks>
    ///     Answers from the last <see cref="MatchBroadcast"/> and the shared world clock, with the same
    ///     surface as <see cref="MatchDirector"/>. Phases are announced when the server says; warnings
    ///     fire locally from the broadcast's warning time.
    /// </remarks>
    public sealed class MatchFollower
    {
        private static readonly IReadOnlyList<MatchClockEvent> NothingHappened = new MatchClockEvent[0];

        private readonly List<MatchClockEvent> pending = new List<MatchClockEvent>();

        private MatchBroadcast latest;
        private bool told;

        /// <summary>The latest world time anybody has told this follower about.</summary>
        private TimeSpan now;

        /// <summary>The highest phase already announced, so the same one is not announced twice.</summary>
        private int announcedPhase;

        /// <summary>The phase whose warning has been announced. Zero means none has.</summary>
        private int warnedPhase;

        /// <summary>Whether the server has said anything about a match yet.</summary>
        public bool HasMatch => told;

        public int Group => told ? latest.Group : 0;

        /// <summary>
        ///     Changes every time the match is replaced by a reset or a new group; per-player state
        ///     should be discarded when it moves.
        /// </summary>
        public int Generation => told ? latest.Generation : 0;

        public MatchClockState State => told ? latest.State : MatchClockState.NotStarted;

        /// <summary>Whether the zone is allowed to hurt anybody: not in the lobby, not in a pause.</summary>
        public bool IsLive => told && latest.IsLive;

        public int CompletedPhases => told ? latest.CompletedPhases : 0;

        /// <summary>How many phases the config wrote, which is what a player counts.</summary>
        public int TotalPhases => told ? latest.TotalPhases : 0;

        /// <summary>
        ///     How many contestants are still in, as the server counted them, or null when the server
        ///     did not say (shown as a dash; zero means the match is over).
        /// </summary>
        public int? Alive => told ? latest.Alive : null;

        /// <summary>Where the wall is closing to, by name, or null when the server withholds it.</summary>
        public string? Destination => told ? latest.Destination : null;

        /// <summary>True once the authored squeeze is spent and the zone is closing to nothing.</summary>
        public bool IsCollapsing => told && latest.IsCollapsing;

        /// <summary>True once the wall has finished closing and there is nowhere left to stand.</summary>
        public bool IsClosed => told && Zone.Sphere.Radius <= 0;

        /// <summary>Match time so far. Paused time does not count.</summary>
        public TimeSpan Elapsed => told ? latest.ElapsedAt(now) : TimeSpan.Zero;

        /// <summary>Time until the next phase, for the HUD countdown.</summary>
        public TimeSpan UntilNextPhase => told ? latest.UntilNextPhaseAt(now) : TimeSpan.Zero;

        /// <summary>True between a warning and the phase it warned about.</summary>
        public bool IsWarningActive => told && latest.IsWarningActiveAt(now);

        /// <summary>
        ///     Where the wall is, this instant. Default until the server has said something; guard with
        ///     <see cref="HasMatch"/>.
        /// </summary>
        public ZoneState Zone => told ? latest.ZoneAt(now) : default(ZoneState);

        /// <summary>Where the match is, in one line.</summary>
        public string Status =>
            told ? latest.StatusAt(now) : "no match: the server has not said what the zone is doing";

        /// <summary>
        ///     Takes what the server said. The first broadcast, and any for a different match, is
        ///     adopted silently so a late joiner does not replay past phase announcements.
        /// </summary>
        public void Receive(MatchBroadcast broadcast)
        {
            bool replaced = !told
                || latest.Group != broadcast.Group
                || latest.Generation != broadcast.Generation;

            if (replaced)
            {
                pending.Clear();
                announcedPhase = broadcast.CompletedPhases;
                warnedPhase = 0;
            }
            else if (broadcast.CompletedPhases > announcedPhase)
            {
                // Announce only the current phase, not every phase passed through since the last broadcast.
                announcedPhase = broadcast.CompletedPhases;
                pending.Add(new MatchClockEvent(MatchClockEventKind.PhaseStarted,
                    broadcast.CompletedPhases, broadcast.PhaseStartedAt));
            }

            told = true;
            latest = broadcast;
        }

        /// <summary>Moves the follower to a moment on the shared clock and hands back what to announce.</summary>
        /// <remarks>
        ///     Not monotonic, unlike <see cref="MatchClock.AdvanceTo"/>: a new session can start at an
        ///     earlier world time, and announcements here are not driven by elapsed time.
        /// </remarks>
        public IReadOnlyList<MatchClockEvent> AdvanceTo(TimeSpan worldNow)
        {
            now = worldNow;

            if (!told)
            {
                return NothingHappened;
            }

            List<MatchClockEvent>? events = null;

            if (pending.Count > 0)
            {
                events = new List<MatchClockEvent>(pending);
                pending.Clear();
            }

            int upcoming = latest.CompletedPhases + 1;
            if (warnedPhase != upcoming && latest.WarningAt.HasValue && latest.IsWarningActiveAt(now))
            {
                warnedPhase = upcoming;
                events = events ?? new List<MatchClockEvent>(1);
                events.Add(new MatchClockEvent(MatchClockEventKind.PhaseWarning, upcoming, latest.WarningAt.Value));
            }

            return events ?? NothingHappened;
        }

        /// <summary>
        ///     Throws away the match when this client leaves its session. The mod outlives sessions, so
        ///     otherwise a stale wall would be drawn over the menu and the next session.
        /// </summary>
        public void Forget()
        {
            told = false;
            latest = default(MatchBroadcast);
            pending.Clear();
            announcedPhase = 0;
            warnedPhase = 0;
        }
    }
}
