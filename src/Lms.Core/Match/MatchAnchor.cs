using System;
using System.Globalization;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Where a match sits on the world clock: the whole of what every client has to agree about.
    /// </summary>
    /// <remarks>
    ///     Elapsed match time is <c>now - epoch - pausedFor</c> against Nitrox's synced world time, so
    ///     a late joiner computes the same answer. The server owns it and broadcasts it.
    /// </remarks>
    public readonly struct MatchAnchor : IEquatable<MatchAnchor>
    {
        /// <summary>What an anchor's text form starts with, so a line carrying one can be recognised.</summary>
        public const string KeyPrefix = "LmsMatch.";

        private const int Fields = 8;

        private MatchAnchor(long sequence, int group, int generation, MatchClockState state,
            TimeSpan epoch, TimeSpan pausedFor, TimeSpan pausedAt)
        {
            Sequence = sequence;
            Group = group;
            Generation = generation;
            State = state;
            Epoch = epoch;
            PausedFor = pausedFor;
            PausedAt = pausedAt;
        }

        /// <summary>A tournament nobody has started: group one, waiting.</summary>
        public static MatchAnchor Lobby { get; } =
            new MatchAnchor(0, 1, 1, MatchClockState.NotStarted, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        /// <summary>
        ///     Waiting in the lobby at a given game and attempt: where a server resuming an earlier
        ///     tournament starts.
        /// </summary>
        public static MatchAnchor LobbyAt(int group, int generation)
        {
            if (group < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(group), group, "Groups are numbered from 1.");
            }

            if (generation < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(generation), generation,
                    "Generations are numbered from 1.");
            }

            return new MatchAnchor(0, group, generation, MatchClockState.NotStarted,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }

        /// <summary>Counts every move an operator has made: a version number for logs.</summary>
        public long Sequence { get; }

        public int Group { get; }

        public int Generation { get; }

        public MatchClockState State { get; }

        /// <summary>World time the match started at. Meaningless while it is in the lobby.</summary>
        public TimeSpan Epoch { get; }

        /// <summary>How much world time this match has spent paused, in total.</summary>
        public TimeSpan PausedFor { get; }

        /// <summary>World time the current pause began. Meaningless unless paused.</summary>
        public TimeSpan PausedAt { get; }

        /// <summary>Match time at a given world time, which is the only question anybody asks.</summary>
        public TimeSpan ElapsedAt(TimeSpan now)
        {
            if (State == MatchClockState.NotStarted)
            {
                return TimeSpan.Zero;
            }

            TimeSpan at = State == MatchClockState.Paused ? PausedAt : now;
            TimeSpan elapsed = at - Epoch - PausedFor;

            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }

        /// <summary>Starts the match now. A match already running is left alone.</summary>
        public MatchAnchor Started(TimeSpan now)
        {
            NotBeforeTheWorld(now);

            return State == MatchClockState.Running
                ? this
                : new MatchAnchor(Sequence + 1, Group, Generation, MatchClockState.Running,
                    now - ElapsedAt(now), PausedFor, TimeSpan.Zero);
        }

        /// <summary>Stops the clock. Anything but a running match is left alone.</summary>
        public MatchAnchor Paused(TimeSpan now)
        {
            NotBeforeTheWorld(now);

            return State != MatchClockState.Running
                ? this
                : new MatchAnchor(Sequence + 1, Group, Generation, MatchClockState.Paused,
                    Epoch, PausedFor, now);
        }

        /// <summary>Banks the pause and runs again. Anything but a paused match is left alone.</summary>
        public MatchAnchor Resumed(TimeSpan now)
        {
            NotBeforeTheWorld(now);

            if (State != MatchClockState.Paused)
            {
                return this;
            }

            TimeSpan paused = now - PausedAt;

            return new MatchAnchor(Sequence + 1, Group, Generation, MatchClockState.Running,
                Epoch, PausedFor + (paused > TimeSpan.Zero ? paused : TimeSpan.Zero), TimeSpan.Zero);
        }

        /// <summary>Makes the match older by <paramref name="by"/>, which brings the next phase forward.</summary>
        public MatchAnchor Skipped(TimeSpan by)
        {
            if (by < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(by), by, "A skip does not run backwards.");
            }

            return new MatchAnchor(Sequence + 1, Group, Generation, State, Epoch - by, PausedFor, PausedAt);
        }

        /// <summary>The same group again, from the lobby, as a new generation.</summary>
        public MatchAnchor Reset() =>
            new MatchAnchor(Sequence + 1, Group, Generation + 1, MatchClockState.NotStarted,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        /// <summary>The next group, from the lobby.</summary>
        public MatchAnchor NextGroup() =>
            new MatchAnchor(Sequence + 1, Group + 1, Generation + 1, MatchClockState.NotStarted,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        /// <summary>Whether a string is an anchor's text form.</summary>
        public static bool IsAnchorKey(string key) =>
            key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

        /// <summary>
        ///     The anchor as one field of text with no tabs or spaces. A skipped match can have a
        ///     negative epoch, hence the minus sign.
        /// </summary>
        public string ToKey() =>
            KeyPrefix +
            Sequence.ToString(CultureInfo.InvariantCulture) + "." +
            Group.ToString(CultureInfo.InvariantCulture) + "." +
            Generation.ToString(CultureInfo.InvariantCulture) + "." +
            Letter(State) + "." +
            Milliseconds(Epoch) + "." +
            Milliseconds(PausedFor) + "." +
            Milliseconds(PausedAt);

        /// <summary>Reads an anchor back. Extra trailing fields are ignored so an older client still follows.</summary>
        public static bool TryParse(string key, out MatchAnchor anchor)
        {
            anchor = Lobby;

            if (!IsAnchorKey(key))
            {
                return false;
            }

            string[] parts = key.Split('.');
            if (parts.Length < Fields)
            {
                return false;
            }

            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
                || sequence < 0
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int group)
                || group < 1
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation)
                || generation < 1
                || !TryReadState(parts[4], out MatchClockState state)
                || !TryReadTime(parts[5], out TimeSpan epoch)
                || !TryReadTime(parts[6], out TimeSpan pausedFor)
                || !TryReadTime(parts[7], out TimeSpan pausedAt))
            {
                return false;
            }

            anchor = new MatchAnchor(sequence, group, generation, state, epoch, pausedFor, pausedAt);

            return true;
        }

        public bool Equals(MatchAnchor other) =>
            Sequence == other.Sequence
            && Group == other.Group
            && Generation == other.Generation
            && State == other.State
            && Epoch == other.Epoch
            && PausedFor == other.PausedFor
            && PausedAt == other.PausedAt;

        public override bool Equals(object? obj) => obj is MatchAnchor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Sequence.GetHashCode();
                hash = hash * 397 ^ Group;
                hash = hash * 397 ^ Generation;
                hash = hash * 397 ^ (int)State;
                return hash * 397 ^ Epoch.GetHashCode();
            }
        }

        public override string ToString() => $"group {Group}.{Generation} {State} (#{Sequence})";

        private static void NotBeforeTheWorld(TimeSpan now)
        {
            if (now < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(now), now,
                    "The world clock does not run before the world started.");
            }
        }

        private static string Milliseconds(TimeSpan span) =>
            ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        private static bool TryReadTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds))
            {
                return false;
            }

            time = TimeSpan.FromMilliseconds(milliseconds);

            return true;
        }

        private static string Letter(MatchClockState state)
        {
            switch (state)
            {
                case MatchClockState.Running:
                    return "R";

                case MatchClockState.Paused:
                    return "P";

                default:
                    return "L";
            }
        }

        private static bool TryReadState(string text, out MatchClockState state)
        {
            switch (text)
            {
                case "R":
                    state = MatchClockState.Running;
                    return true;

                case "P":
                    state = MatchClockState.Paused;
                    return true;

                case "L":
                    state = MatchClockState.NotStarted;
                    return true;

                default:
                    state = MatchClockState.NotStarted;
                    return false;
            }
        }
    }
}
