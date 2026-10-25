using System;
using System.Collections.Generic;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     What the operator's console is told about this server, on a channel separate from the
    ///     broadcast so ghosts never reach the overlays.
    /// </summary>
    /// <remarks>
    ///     Carries the verb vocabulary from <see cref="Match.AdminCommands"/> so the browser's controls
    ///     cannot drift. Immutable and compared by value, because it is sent when it changes.
    /// </remarks>
    public sealed class ConsoleReport : IEquatable<ConsoleReport>
    {
        private static readonly BoothRulesOutcome[] NothingAboutTheRules = new BoothRulesOutcome[0];

        public ConsoleReport(DateTimeOffset at, IReadOnlyList<ConsolePresence> connected,
            IReadOnlyList<ConsoleVerb> verbs, IReadOnlyList<BoothCommandAnswer> answers,
            long ran, bool acceptsCommands,
            long rulesRevision = 0, IReadOnlyList<BoothRulesOutcome>? rules = null,
            string? running = null)
        {
            At = at;
            Connected = connected ?? throw new ArgumentNullException(nameof(connected));
            Verbs = verbs ?? throw new ArgumentNullException(nameof(verbs));
            Answers = answers ?? throw new ArgumentNullException(nameof(answers));
            Ran = ran;
            AcceptsCommands = acceptsCommands;
            RulesRevision = rulesRevision;
            Rules = rules ?? NothingAboutTheRules;
            Running = running ?? string.Empty;
        }

        /// <summary>When this was taken, in UTC wall-clock time.</summary>
        public DateTimeOffset At { get; }

        /// <summary>
        ///     Everybody connected, ghosts included, and whether the match counts them.
        /// </summary>
        public IReadOnlyList<ConsolePresence> Connected { get; }

        /// <summary>Every verb this build has, so the console's controls cannot drift from it.</summary>
        public IReadOnlyList<ConsoleVerb> Verbs { get; }

        /// <summary>What this server did about the commands it has run since the last report.</summary>
        public IReadOnlyList<BoothCommandAnswer> Answers { get; }

        /// <summary>The highest command id this server has run, this run.</summary>
        public long Ran { get; }

        /// <summary>
        ///     The config in force (not a pending one on disk), verbatim, for the console editor to
        ///     prefill. Empty when this server runs no tournament.
        /// </summary>
        public string Running { get; }

        /// <summary>Whether this server accepts booth commands.</summary>
        public bool AcceptsCommands { get; }

        /// <summary>
        ///     Which booth config revision this server is running. Zero means its own on-disk config.
        /// </summary>
        public long RulesRevision { get; }

        /// <summary>What this server did about the config revisions it has been sent.</summary>
        public IReadOnlyList<BoothRulesOutcome> Rules { get; }

        /// <summary>
        ///     Value equality over everything except <see cref="At"/>, so a newer timestamp alone is not a change.
        /// </summary>
        public bool Equals(ConsoleReport? other)
        {
            if (other == null)
            {
                return false;
            }

            if (Ran != other.Ran || AcceptsCommands != other.AcceptsCommands
                || RulesRevision != other.RulesRevision
                || !string.Equals(Running, other.Running, StringComparison.Ordinal)
                || Connected.Count != other.Connected.Count
                || Verbs.Count != other.Verbs.Count
                || Answers.Count != other.Answers.Count
                || Rules.Count != other.Rules.Count)
            {
                return false;
            }

            for (int i = 0; i < Rules.Count; i++)
            {
                if (Rules[i].Revision != other.Rules[i].Revision
                    || Rules[i].Adopted != other.Rules[i].Adopted
                    || Rules[i].Final != other.Rules[i].Final
                    || !string.Equals(Rules[i].Message, other.Rules[i].Message, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            for (int i = 0; i < Connected.Count; i++)
            {
                if (!Connected[i].Equals(other.Connected[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < Verbs.Count; i++)
            {
                if (!Verbs[i].Equals(other.Verbs[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < Answers.Count; i++)
            {
                if (Answers[i].Id != other.Answers[i].Id)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ConsoleReport);

        public override int GetHashCode() =>
            unchecked((Connected.Count * 397) ^ (Answers.Count * 31) ^ Ran.GetHashCode());
    }

    /// <summary>
    ///     One person connected to this server, and whether they are in the match.
    /// </summary>
    public readonly struct ConsolePresence : IEquatable<ConsolePresence>
    {
        public ConsolePresence(string name, bool inMatch)
        {
            Name = name ?? string.Empty;
            InMatch = inMatch;
        }

        /// <summary>The Nitrox player name.</summary>
        public string Name { get; }

        /// <summary>
        ///     False for a ghost: no pod, no wall, no ring, and not on the board.
        /// </summary>
        public bool InMatch { get; }

        public bool Equals(ConsolePresence other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal) && InMatch == other.InMatch;

        public override bool Equals(object? obj) => obj is ConsolePresence other && Equals(other);

        public override int GetHashCode() => unchecked((Name.GetHashCode() * 397) ^ InMatch.GetHashCode());

        public override string ToString() => InMatch ? Name : Name + " (ghost)";
    }

    /// <summary>
    ///     One admin verb and how the console should present it.
    /// </summary>
    /// <remarks>
    ///     <see cref="TakesASubject"/> adds a player-name field; <see cref="MustBeTyped"/> withholds a
    ///     button for destructive verbs such as <c>podclear all</c> and <c>round force</c>.
    /// </remarks>
    public readonly struct ConsoleVerb : IEquatable<ConsoleVerb>
    {
        public ConsoleVerb(string verb, bool takesASubject, bool mustBeTyped)
        {
            Verb = verb ?? string.Empty;
            TakesASubject = takesASubject;
            MustBeTyped = mustBeTyped;
        }

        public string Verb { get; }

        public bool TakesASubject { get; }

        public bool MustBeTyped { get; }

        public bool Equals(ConsoleVerb other) =>
            string.Equals(Verb, other.Verb, StringComparison.Ordinal)
            && TakesASubject == other.TakesASubject && MustBeTyped == other.MustBeTyped;

        public override bool Equals(object? obj) => obj is ConsoleVerb other && Equals(other);

        public override int GetHashCode() => Verb.GetHashCode();

        public override string ToString() => TakesASubject ? Verb + " <player>" : Verb;
    }
}
