using System;
using System.Collections.Generic;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Runs one match at a time on the server, and the operator's control over each: start, pause,
    ///     skip, reset, next group.
    /// </summary>
    /// <remarks>
    ///     Server-only; clients use <see cref="MatchFollower"/>. Every command moves the
    ///     <see cref="MatchAnchor"/>, and the <see cref="MatchClock"/> is driven to the time it computes.
    /// </remarks>
    public sealed class MatchDirector
    {
        private static readonly IReadOnlyList<MatchClockEvent> NothingHappened = new MatchClockEvent[0];

        private MatchClockSettings settings;

        /// <summary>
        ///     The squeeze for a group number. Keyed on the group, not an attempt count, so a
        ///     <c>reset</c> replays the same arena.
        /// </summary>
        private Func<int, ZoneSchedule> squeeze;

        private ZoneSchedule? resolved;
        private int resolvedGroup;

        private MatchClock clock;
        private MatchAnchor anchor = MatchAnchor.Lobby;
        private int clockGroup = 1;
        private int clockGeneration = 1;

        /// <summary>
        ///     The most players this attempt has ever had standing at once. A match is only decided
        ///     once there were at least two, so a solo match never wins itself.
        /// </summary>
        private int mostStanding;

        /// <summary>World time the match was decided, or null while it is still anybody's.</summary>
        private TimeSpan? decidedAt;

        /// <summary>The latest world time anybody has told this director about.</summary>
        private TimeSpan now;

        /// <param name="clockSettings">The cadence, from the tournament config.</param>
        /// <param name="schedule">The authored squeeze, which also says how many phases a match has.</param>
        public MatchDirector(MatchClockSettings clockSettings, ZoneSchedule schedule)
            : this(clockSettings, OneSqueezeForEveryGroup(schedule))
        {
        }

        /// <param name="clockSettings">The cadence, from the tournament config.</param>
        /// <param name="squeeze">
        ///     The squeeze for a group, by group number. Called once per group and cached, so it may
        ///     do real work such as drawing from a destination pool.
        /// </param>
        public MatchDirector(MatchClockSettings clockSettings, Func<int, ZoneSchedule> squeeze)
        {
            settings = clockSettings ?? throw new ArgumentNullException(nameof(clockSettings));
            this.squeeze = squeeze ?? throw new ArgumentNullException(nameof(squeeze));

            clock = new MatchClock(settings, Schedule.PhaseCount);
        }

        /// <summary>Takes new rules, in the lobby only.</summary>
        /// <param name="clockSettings">The new cadence.</param>
        /// <param name="newSqueeze">The new squeeze, by group.</param>
        /// <returns>False when a match is running or paused, in which case nothing changed.</returns>
        /// <remarks>
        ///     Keeps the anchor (group and attempt), rebuilds the clock, and drops the cached schedule
        ///     so it is drawn again from the new rules.
        /// </remarks>
        public bool Adopt(MatchClockSettings clockSettings, Func<int, ZoneSchedule> newSqueeze)
        {
            if (clockSettings == null)
            {
                throw new ArgumentNullException(nameof(clockSettings));
            }

            if (newSqueeze == null)
            {
                throw new ArgumentNullException(nameof(newSqueeze));
            }

            if (clock.State != MatchClockState.NotStarted)
            {
                return false;
            }

            settings = clockSettings;
            squeeze = newSqueeze;

            // Before the clock: it is built from Schedule, which reads through `resolved`.
            resolved = null;
            resolvedGroup = 0;

            clock = new MatchClock(settings, Schedule.PhaseCount);

            return true;
        }

        /// <summary>
        ///     Waits in the lobby at a given game and attempt, for a server resuming an earlier
        ///     tournament. Only valid before the clock has moved.
        /// </summary>
        public void ResumeAt(int group, int generation)
        {
            if (clock.State != MatchClockState.NotStarted)
            {
                throw new InvalidOperationException("A tournament is picked up before its clock moves.");
            }

            anchor = MatchAnchor.LobbyAt(group, generation);
            Follow();
        }

        private static Func<int, ZoneSchedule> OneSqueezeForEveryGroup(ZoneSchedule schedule)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            return _ => schedule;
        }

        /// <summary>
        ///     The squeeze the group now playing runs, cached per group since <see cref="Zone"/> reads it
        ///     every tick.
        /// </summary>
        private ZoneSchedule Schedule
        {
            get
            {
                int group = anchor.Group;
                if (resolved == null || resolvedGroup != group)
                {
                    resolved = squeeze(group)
                        ?? throw new InvalidOperationException($"No squeeze was produced for group {group}.");
                    resolvedGroup = group;
                }

                return resolved;
            }
        }

        /// <summary>Which group is playing, counting from one.</summary>
        public int Group => anchor.Group;

        /// <summary>
        ///     Changes every time the match is replaced by a reset or a new group; per-player state
        ///     should be discarded when it moves.
        /// </summary>
        public int Generation => anchor.Generation;

        public MatchClockState State => clock.State;

        /// <summary>Match time so far. Paused time does not count.</summary>
        public TimeSpan Elapsed => clock.Elapsed;

        public int CompletedPhases => clock.CompletedPhases;

        /// <summary>
        ///     How many phases the config wrote. The match runs one more, the final collapse, which is
        ///     announced by name.
        /// </summary>
        public int TotalPhases => Schedule.AuthoredPhaseCount;

        /// <summary>Every phase the match runs, the collapse included. Use this for progress bars.</summary>
        public int PhaseCount => Schedule.PhaseCount;

        /// <summary>True once the authored squeeze is spent and the zone is closing to nothing.</summary>
        public bool IsCollapsing => CompletedPhases > Schedule.AuthoredPhaseCount;

        /// <summary>True once the wall has finished closing and there is nowhere left to stand.</summary>
        public bool IsClosed => Zone.Sphere.Radius <= 0;

        /// <summary>
        ///     True once the match has a result: one player left, or none. The wall stops here and the
        ///     victory window begins.
        /// </summary>
        public bool IsDecided => decidedAt.HasValue;

        /// <summary>The last one standing, or null when the wall took everybody.</summary>
        public string? Winner { get; private set; }

        /// <summary>Time until the next phase, for the HUD countdown. Zero once the schedule is spent.</summary>
        public TimeSpan UntilNextPhase => clock.UntilNextPhase;

        /// <summary>True between a warning and the phase it warned about.</summary>
        public bool IsWarningActive => clock.IsWarningActive;

        /// <summary>True once every phase has fired, the collapse included: the match is over.</summary>
        public bool AllPhasesFired => clock.AllPhasesFired;

        /// <summary>Whether the zone is allowed to hurt anybody: not in the lobby, not in a pause.</summary>
        public bool IsLive => clock.State == MatchClockState.Running;

        /// <summary>
        ///     Where the wall is, this instant; a pure function of the clock, cheap enough for every frame.
        /// </summary>
        public ZoneState Zone => Schedule.At(clock.CompletedPhases, clock.SinceLastPhase);

        /// <summary>Where the match is, in one line; the same sentence a client shows.</summary>
        public string Status => Broadcast().StatusAt(now);

        /// <summary>
        ///     The match as the clients are told it: the anchor, the phase and the geometry of the wall.
        ///     Cheap and pure; the rest of the schedule is never sent.
        /// </summary>
        public MatchBroadcast Broadcast()
        {
            TimeSpan startedAt = clock.Elapsed - clock.SinceLastPhase;
            TimeSpan? nextPhaseAt = clock.AllPhasesFired
                ? (TimeSpan?)null
                : clock.Elapsed + clock.UntilNextPhase;

            return new MatchBroadcast(anchor, Schedule.AuthoredPhaseCount, startedAt, nextPhaseAt,
                nextPhaseAt.HasValue ? nextPhaseAt.Value - settings.WarningLead : (TimeSpan?)null,
                Schedule.ShrinkAt(clock.CompletedPhases));
        }

        /// <summary>
        ///     Advances the match to a moment on the shared world clock and hands back what fell due.
        ///     Nothing is counted, so a slow timer tick cannot move a phase.
        /// </summary>
        public IReadOnlyList<MatchClockEvent> AdvanceTo(TimeSpan worldNow)
        {
            Now(worldNow);

            return Follow();
        }

        /// <summary>Advances by a delta, for callers with no shared clock, mostly tests.</summary>
        public IReadOnlyList<MatchClockEvent> Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "Match time does not run backwards.");
            }

            return AdvanceTo(now + delta);
        }

        /// <summary>
        ///     Tells the match who is still standing, and settles it if that is one of them or none.
        /// </summary>
        /// <param name="stillIn">Everybody the match is not finished with. Null when the caller has no roster.</param>
        /// <param name="awayWithinGrace">
        ///     How many of them are disconnected but still inside <c>disconnect.grace</c>. The match is
        ///     not decided while any are.
        /// </param>
        /// <param name="worldNow">The shared clock, on which the victory window is measured.</param>
        /// <remarks>
        ///     Deciding pauses the anchor, freezing wall, phases and damage at once; resume is then
        ///     refused. While waiting on an absentee the wall keeps closing, so keep the grace well
        ///     under <c>match.cycleLength</c>.
        /// </remarks>
        public void Standing(IReadOnlyList<string>? stillIn, int awayWithinGrace, TimeSpan worldNow)
        {
            if (awayWithinGrace < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(awayWithinGrace), awayWithinGrace,
                    "A count of the people the match is waiting for does not go negative.");
            }

            Now(worldNow);

            if (stillIn == null || decidedAt.HasValue || anchor.State != MatchClockState.Running)
            {
                return;
            }

            if (stillIn.Count > mostStanding)
            {
                mostStanding = stillIn.Count;
            }

            // Any absentee blocks a decision, even with nobody standing: they could still return and win.
            if (mostStanding < 2 || stillIn.Count > 1 || awayWithinGrace > 0)
            {
                return;
            }

            decidedAt = now;
            Winner = stillIn.Count == 1 ? stillIn[0] : null;

            anchor = anchor.Paused(now);
            Follow();
        }

        /// <summary>
        ///     Whether the victory window after <see cref="IsDecided"/> has passed and the match can be
        ///     written off.
        /// </summary>
        public bool IsSettled(TimeSpan worldNow) =>
            decidedAt.HasValue && worldNow - decidedAt.Value >= settings.VictoryWindow;

        /// <summary>Runs one admin command at a moment on the shared clock and says what it did.</summary>
        /// <remarks>
        ///     Advances to <paramref name="worldNow"/> first and discards events that fell due meanwhile;
        ///     callers that log events must advance and record them before calling this.
        /// </remarks>
        public AdminCommandResult Execute(AdminCommand command, TimeSpan worldNow)
        {
            AdvanceTo(worldNow);

            return Execute(command);
        }

        /// <summary>Runs one admin command and says what it did.</summary>
        public AdminCommandResult Execute(AdminCommand command)
        {
            // A decided match refuses everything that would move the clock; reset and next still work.
            if (decidedAt.HasValue && command != AdminCommand.Reset && command != AdminCommand.NextGroup)
            {
                return Refused(Winner == null
                    ? "the match is decided — the wall took everybody"
                    : $"the match is decided — {Winner} won it");
            }

            switch (command)
            {
                case AdminCommand.Start:
                    return Start();

                case AdminCommand.Pause:
                    return Pause();

                case AdminCommand.Resume:
                    return Resume();

                case AdminCommand.Skip:
                    return Skip();

                case AdminCommand.Reset:
                    anchor = anchor.Reset();
                    return Restart($"group {Group} reset");

                case AdminCommand.NextGroup:
                    anchor = anchor.NextGroup();
                    return Restart($"group {Group} up");

                default:
                    return new AdminCommandResult(false, Status, NothingHappened);
            }
        }

        private AdminCommandResult Start()
        {
            if (anchor.State == MatchClockState.Running)
            {
                return Refused("the match is already running");
            }

            if (anchor.State == MatchClockState.Paused)
            {
                return Refused("the match is paused, not waiting to start — resume it");
            }

            anchor = anchor.Started(now);
            Follow();

            // Mention the opening only when it differs from the cycle.
            string cadence = settings.OpeningLength == settings.CycleLength
                ? $"{TotalPhases} phases of {MatchTime.Countdown(settings.CycleLength)}"
                : $"a {MatchTime.Countdown(settings.OpeningLength)} opening and then " +
                  $"{TotalPhases} phases of {MatchTime.Countdown(settings.CycleLength)}";

            return Did($"group {Group} started: {cadence} " +
                       $"and then the collapse, opening {Schedule.Opening}, first in {MatchTime.Countdown(UntilNextPhase)}");
        }

        private AdminCommandResult Pause()
        {
            if (anchor.State != MatchClockState.Running)
            {
                return Refused("the match is not running");
            }

            anchor = anchor.Paused(now);
            Follow();

            return Did($"paused at {MatchTime.Countdown(Elapsed)}, " +
                       $"{(IsCollapsing ? "mid-collapse" : $"phase {CompletedPhases}/{TotalPhases} fired")}");
        }

        private AdminCommandResult Resume()
        {
            if (anchor.State != MatchClockState.Paused)
            {
                return Refused("the match is not paused");
            }

            anchor = anchor.Resumed(now);
            Follow();

            return Did($"resumed at {MatchTime.Countdown(Elapsed)}, next phase in {MatchTime.Countdown(UntilNextPhase)}");
        }

        /// <summary>Ages the anchor by what is left of the phase, so every client's wall moves together.</summary>
        private AdminCommandResult Skip()
        {
            if (anchor.State != MatchClockState.Running)
            {
                return Refused("the match is not running");
            }

            if (AllPhasesFired)
            {
                return Refused("the zone has closed — there is nothing left to skip to");
            }

            anchor = anchor.Skipped(clock.UntilNextPhase);

            IReadOnlyList<MatchClockEvent> events = Follow();
            ZonePhase phase = Schedule.Phases[CompletedPhases - 1];
            string what = phase.IsFinalCollapse ? "the final collapse" : $"phase {CompletedPhases}/{TotalPhases}";

            return new AdminCommandResult(true, $"{what} started early: {phase}", events);
        }

        private AdminCommandResult Restart(string what)
        {
            Follow();

            return Did($"{what} — waiting in the lobby, " +
                       $"{TotalPhases} phases then the collapse, opening {Schedule.Opening}");
        }

        /// <summary>
        ///     Brings the clock into line with the anchor, rebuilding it for a new match, and returns
        ///     what that made happen.
        /// </summary>
        private IReadOnlyList<MatchClockEvent> Follow()
        {
            if (clockGroup != anchor.Group || clockGeneration != anchor.Generation)
            {
                clock = new MatchClock(settings, Schedule.PhaseCount);
                clockGroup = anchor.Group;
                clockGeneration = anchor.Generation;

                // A new attempt has nobody standing and no result yet.
                mostStanding = 0;
                decidedAt = null;
                Winner = null;
            }

            if (anchor.State == MatchClockState.NotStarted)
            {
                if (clock.State != MatchClockState.NotStarted)
                {
                    clock.Reset();
                }

                return NothingHappened;
            }

            // The clock only advances while running; a paused anchor stopped ageing, so pause again after.
            clock.Start();
            clock.Resume();

            IReadOnlyList<MatchClockEvent> events = clock.AdvanceTo(anchor.ElapsedAt(now));

            if (anchor.State == MatchClockState.Paused)
            {
                clock.Pause();
            }

            return events;
        }

        private void Now(TimeSpan worldNow)
        {
            if (worldNow > now)
            {
                now = worldNow;
            }
        }

        private AdminCommandResult Did(string message) => new AdminCommandResult(true, message, NothingHappened);

        private AdminCommandResult Refused(string why) =>
            new AdminCommandResult(false, $"{why}. {Status}", NothingHappened);

    }
}
