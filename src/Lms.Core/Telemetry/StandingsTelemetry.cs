using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Results;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     The tournament standings table sent to the casting booth, with one result per finished match.
    /// </summary>
    /// <remarks>
    ///     Folded on the server from the audit log, which the booth never sees. <see cref="Entries"/>
    ///     is already in table order (points, wins, best result, name) and must not be re-sorted. Each
    ///     line carries the components of its total. Value equality drives send-on-change.
    /// </remarks>
    public sealed class StandingsTelemetry : IEquatable<StandingsTelemetry>
    {
        private readonly ReadOnlyCollection<StandingsLine> entries;
        private readonly ReadOnlyCollection<GameResult> games;

        public StandingsTelemetry(int matches, int pointsPerKill,
            IReadOnlyList<StandingsLine>? entries = null,
            IReadOnlyList<GameResult>? games = null)
        {
            if (matches < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(matches), matches,
                    "A tournament cannot have played fewer than no matches.");
            }

            if (pointsPerKill < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pointsPerKill), pointsPerKill,
                    "A kill cannot cost its killer points.");
            }

            Matches = matches;
            PointsPerKill = pointsPerKill;
            this.entries = new ReadOnlyCollection<StandingsLine>(
                entries == null ? new List<StandingsLine>() : new List<StandingsLine>(entries));
            this.games = new ReadOnlyCollection<GameResult>(
                games == null ? new List<GameResult>() : new List<GameResult>(games));
        }

        /// <summary>
        ///     How many matches the table was folded from (tournament progress, not any player's count).
        /// </summary>
        public int Matches { get; }

        /// <summary>
        ///     Points per kill. The booth adds its own hand-attributed kills on top and must use the
        ///     tournament's multiplier.
        /// </summary>
        public int PointsPerKill { get; }

        /// <summary>
        ///     One result per finished match, in fold order. Needed because bracket seeding uses
        ///     per-match results, which the summed rows lose. Never null; part of <see cref="Equals"/>.
        /// </summary>
        public IReadOnlyList<GameResult> Games => games;

        /// <summary>Leader first. The order is the table; do not sort it again.</summary>
        public IReadOnlyList<StandingsLine> Entries => entries;

        /// <summary>
        ///     The table for the finished matches, or null (not an empty table) when none have finished.
        /// </summary>
        public static StandingsTelemetry? Of(Standings standings, ScoringRules rules)
        {
            if (standings == null)
            {
                throw new ArgumentNullException(nameof(standings));
            }

            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (standings.Matches == 0)
            {
                return null;
            }

            List<StandingsLine> lines = new List<StandingsLine>(standings.Entries.Count);
            foreach (StandingsEntry entry in standings.Entries)
            {
                lines.Add(new StandingsLine(entry.Place, entry.Name, entry.Points,
                    entry.MatchesPlayed, entry.Wins, entry.BestPlacement, entry.Kills,
                    entry.Outlasted));
            }

            return new StandingsTelemetry(standings.Matches, rules.PointsPerKill, lines,
                standings.Games);
        }

        public bool Equals(StandingsTelemetry? other)
        {
            if (other == null || Matches != other.Matches || PointsPerKill != other.PointsPerKill
                || entries.Count != other.entries.Count || games.Count != other.games.Count)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].Equals(other.entries[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < games.Count; i++)
            {
                if (!games[i].Equals(other.games[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as StandingsTelemetry);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Matches * 397 ^ PointsPerKill;
                hash = hash * 397 ^ entries.Count;

                foreach (StandingsLine line in entries)
                {
                    hash = hash * 397 ^ line.GetHashCode();
                }

                hash = hash * 397 ^ games.Count;

                foreach (GameResult game in games)
                {
                    hash = hash * 397 ^ game.GetHashCode();
                }

                return hash;
            }
        }

        public override string ToString() =>
            entries.Count == 0
                ? $"{Matches} match(es), nobody placed"
                : $"{Matches} match(es), leader {entries[0]}";
    }

    /// <summary>One player's tournament, as a row of the table.</summary>
    public readonly struct StandingsLine : IEquatable<StandingsLine>
    {
        public StandingsLine(int place, string name, int points, int played, int wins,
            int bestPlacement, int kills, int outlasted)
        {
            Place = place;
            Name = name;
            Points = points;
            Played = played;
            Wins = wins;
            BestPlacement = bestPlacement;
            Kills = kills;
            Outlasted = outlasted;
        }

        /// <summary>
        ///     Table place, one being best; tied players share a place. Differs from row position, which
        ///     breaks ties by name.
        /// </summary>
        public int Place { get; }

        public string Name { get; }

        /// <summary>Their total, which the table is ordered by.</summary>
        public int Points { get; }

        /// <summary>How many matches this player has played.</summary>
        public int Played { get; }

        /// <summary>First places, shared ones included. The first tie-break.</summary>
        public int Wins { get; }

        /// <summary>Their best single result, which is the tie-break after wins.</summary>
        public int BestPlacement { get; }

        /// <summary>Kills across the tournament, worth <see cref="StandingsTelemetry.PointsPerKill"/> each.</summary>
        public int Kills { get; }

        /// <summary>Players outlasted across the tournament, at a point each.</summary>
        public int Outlasted { get; }

        public bool Equals(StandingsLine other) =>
            Place == other.Place
            && Name == other.Name
            && Points == other.Points
            && Played == other.Played
            && Wins == other.Wins
            && BestPlacement == other.BestPlacement
            && Kills == other.Kills
            && Outlasted == other.Outlasted;

        public override bool Equals(object? obj) => obj is StandingsLine other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Name?.GetHashCode() ?? 0;
                hash = hash * 397 ^ Place;
                hash = hash * 397 ^ Points;
                hash = hash * 397 ^ Played;
                hash = hash * 397 ^ Wins;
                hash = hash * 397 ^ BestPlacement;
                hash = hash * 397 ^ Kills;
                return hash * 397 ^ Outlasted;
            }
        }

        public override string ToString() => $"{Place}. {Name}: {Points} from {Played} match(es)";
    }
}
