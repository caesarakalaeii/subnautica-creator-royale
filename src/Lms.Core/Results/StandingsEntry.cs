using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Results
{
    /// <summary>One player's tournament: what they scored, and what it took.</summary>
    public sealed class StandingsEntry
    {
        private readonly List<int> placements = new List<int>();
        private readonly ReadOnlyCollection<int> placementView;

        internal StandingsEntry(string name)
        {
            Name = name;
            placementView = new ReadOnlyCollection<int>(placements);
        }

        public string Name { get; }

        public int Points { get; private set; }

        /// <summary>Where they are in the table, one being best.</summary>
        /// <remarks>
        ///     Tied players share a place (two firsts, then a third). The name orders the display
        ///     only; it never decides who is ahead.
        /// </remarks>
        public int Place { get; private set; }

        public int MatchesPlayed { get; private set; }

        /// <summary>First places, shared ones included.</summary>
        public int Wins { get; private set; }

        /// <summary>Their best single result, which is the first tie-break after wins.</summary>
        public int BestPlacement { get; private set; }

        /// <summary>Where they came in each match they played, in the order they played them.</summary>
        public IReadOnlyList<int> Placements => placementView;

        /// <summary>How many players they have outlasted across the tournament.</summary>
        /// <remarks>Kept separately so the table can show how <see cref="Points"/> was made up.</remarks>
        public int Outlasted { get; private set; }

        /// <summary>How many players they have killed across the tournament.</summary>
        public int Kills { get; private set; }

        /// <summary>All damage taken across the tournament.</summary>
        public double DamageTaken { get; private set; }

        /// <summary>Rings of theirs that caught somebody.</summary>
        public int Catches { get; private set; }

        public override string ToString() => $"{Name}: {Points} points from {MatchesPlayed} match(es)";

        internal void Placed(int place)
        {
            Place = place;
        }

        internal void Add(LedgerEntry entry, int points)
        {
            Points += points;
            MatchesPlayed++;
            placements.Add(entry.Placement);

            if (entry.Placement == 1)
            {
                Wins++;
            }

            if (BestPlacement == 0 || entry.Placement < BestPlacement)
            {
                BestPlacement = entry.Placement;
            }

            Outlasted += entry.Outlasted;
            Kills += entry.Kills;
            DamageTaken += entry.Audit.DamageTaken;
            Catches += entry.Audit.Catches;
        }
    }
}
