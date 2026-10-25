using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Audit;

namespace Lms.Core.Results
{
    /// <summary>The tournament table: several matches folded into one order.</summary>
    /// <remarks>
    ///     Built from ledgers, which are built from the audit log, so it can be rebuilt from the file.
    ///     The caller chooses which matches to fold; replays use the last attempt at each group.
    ///     Ordered by points, then wins, then best single result, then name (ordinal).
    /// </remarks>
    public sealed class Standings
    {
        private readonly Dictionary<string, StandingsEntry> byName;
        private readonly ReadOnlyCollection<StandingsEntry> entries;
        private readonly ReadOnlyCollection<GameResult> games;

        private Standings(List<StandingsEntry> ordered, Dictionary<string, StandingsEntry> lookup,
            int matches, List<GameResult> folded)
        {
            entries = new ReadOnlyCollection<StandingsEntry>(ordered);
            byName = lookup;
            Matches = matches;
            games = new ReadOnlyCollection<GameResult>(folded);
        }

        /// <summary>How many matches the table was folded from.</summary>
        public int Matches { get; }

        /// <summary>
        ///     Every match the table was folded from, per player, in fold order. Never null.
        /// </summary>
        public IReadOnlyList<GameResult> Games => games;

        /// <summary>Leader first.</summary>
        public IReadOnlyList<StandingsEntry> Entries => entries;

        /// <summary>Folds the matches given, in the order given.</summary>
        public static Standings Of(IEnumerable<MatchLedger> ledgers, ScoringRules rules)
        {
            if (ledgers == null)
            {
                throw new ArgumentNullException(nameof(ledgers));
            }

            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            Dictionary<string, StandingsEntry> lookup = new Dictionary<string, StandingsEntry>(StringComparer.Ordinal);
            List<StandingsEntry> ordered = new List<StandingsEntry>();
            List<GameResult> folded = new List<GameResult>();
            int matches = 0;

            foreach (MatchLedger ledger in ledgers)
            {
                matches++;

                // Same entries and scores as the table, so per-game sums always agree with it.
                List<GameResult.GameLine> lines = new List<GameResult.GameLine>();
                foreach (LedgerEntry line in ledger.Entries)
                {
                    lines.Add(new GameResult.GameLine(line.Name, rules.PointsFor(line),
                        line.Placement, line.Kills, line.Outlasted));
                }

                folded.Add(new GameResult(ledger.Group, ledger.Generation, lines));

                foreach (LedgerEntry line in ledger.Entries)
                {
                    StandingsEntry? entry;
                    if (!lookup.TryGetValue(line.Name, out entry))
                    {
                        entry = new StandingsEntry(line.Name);
                        lookup.Add(line.Name, entry);
                        ordered.Add(entry);
                    }

                    entry.Add(line, rules.PointsFor(line));
                }
            }

            ordered.Sort(Leading);
            Place(ordered);

            return new Standings(ordered, lookup, matches, folded);
        }

        /// <summary>Folds the last attempt at every group the log holds, in the order the groups played.</summary>
        public static Standings Of(AuditReplay replay, ScoringRules rules) =>
            Of(Ledgers(MatchesOf(replay), finishedOnly: false), rules);

        /// <summary>Folds only the groups that have finished, last attempt each.</summary>
        /// <remarks>
        ///     The table changes once per match rather than on every elimination. A group being
        ///     replayed drops out until the replay ends.
        /// </remarks>
        public static Standings OfFinished(AuditReplay replay, ScoringRules rules) =>
            OfFinished(MatchesOf(replay), rules);

        /// <summary>
        ///     The same, over matches from more than one log (e.g. after a server restart). A later
        ///     attempt at a group replaces an earlier one whichever log it is in.
        /// </summary>
        public static Standings OfFinished(IEnumerable<MatchAudit> matches, ScoringRules rules) =>
            Of(Ledgers(matches, finishedOnly: true), rules);

        private static IEnumerable<MatchAudit> MatchesOf(AuditReplay replay) =>
            replay?.Matches ?? throw new ArgumentNullException(nameof(replay));

        /// <summary>The last attempt at every group the log holds, in the order the groups played.</summary>
        private static List<MatchLedger> Ledgers(IEnumerable<MatchAudit> matches, bool finishedOnly)
        {
            if (matches == null)
            {
                throw new ArgumentNullException(nameof(matches));
            }

            Dictionary<int, MatchAudit> lastAttempt = new Dictionary<int, MatchAudit>();
            List<int> groups = new List<int>();

            foreach (MatchAudit match in matches)
            {
                if (!lastAttempt.ContainsKey(match.Group))
                {
                    groups.Add(match.Group);
                }
                else if (!match.Supersedes)
                {
                    continue;
                }

                lastAttempt[match.Group] = match;
            }

            List<MatchLedger> ledgers = new List<MatchLedger>();
            foreach (int group in groups)
            {
                MatchAudit match = lastAttempt[group];

                if (finishedOnly && !match.HasEnded)
                {
                    continue;
                }

                ledgers.Add(MatchLedger.Of(match));
            }

            return ledgers;
        }

        /// <summary>One player's line, or null if they never played.</summary>
        public StandingsEntry? Entry(string name)
        {
            StandingsEntry? entry;

            return byName.TryGetValue(name, out entry) ? entry : null;
        }

        public override string ToString() =>
            entries.Count == 0
                ? "nobody has played yet"
                : $"{Matches} match(es), leader {entries[0]}";

        /// <summary>
        ///     Numbers the table; players tied on everything but name share a place (two firsts, then a third).
        /// </summary>
        private static void Place(List<StandingsEntry> ordered)
        {
            int place = 1;

            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0 && !Tied(ordered[i - 1], ordered[i]))
                {
                    place = i + 1;
                }

                ordered[i].Placed(place);
            }
        }

        /// <summary>Whether the table has nothing to tell two players apart by, the name aside.</summary>
        private static bool Tied(StandingsEntry left, StandingsEntry right) =>
            left.Points == right.Points
            && left.Wins == right.Wins
            && left.BestPlacement == right.BestPlacement;

        private static int Leading(StandingsEntry left, StandingsEntry right)
        {
            int byPoints = right.Points.CompareTo(left.Points);
            if (byPoints != 0)
            {
                return byPoints;
            }

            int byWins = right.Wins.CompareTo(left.Wins);
            if (byWins != 0)
            {
                return byWins;
            }

            int byBest = left.BestPlacement.CompareTo(right.BestPlacement);

            return byBest != 0 ? byBest : string.CompareOrdinal(left.Name, right.Name);
        }
    }
}
