using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Lms.Core.Results
{
    /// <summary>One finished match's per-player points, kept separate from the summed table.</summary>
    /// <remarks>
    ///     Lets series of particular games be totalled (e.g. to seat a final). Points come from
    ///     <see cref="ScoringRules.PointsFor(LedgerEntry)"/>, so they always agree with the table.
    ///     Value equality, so callers can detect unchanged results.
    /// </remarks>
    public readonly struct GameResult : IEquatable<GameResult>
    {
        /// <summary>Creates a result from the match's lines, best placement first.</summary>
        public GameResult(int group, int generation, IReadOnlyList<GameLine> lines)
        {
            Group = group;
            Generation = generation;
            Lines = new ReadOnlyCollection<GameLine>(lines.ToList());
        }

        /// <summary>Which group this match was.</summary>
        public int Group { get; }

        /// <summary>Which attempt at that group counts (the last one).</summary>
        public int Generation { get; }

        /// <summary>Every placed player's line, best placement first.</summary>
        public IReadOnlyList<GameLine> Lines { get; }

        public bool Equals(GameResult other) =>
            Group == other.Group
            && Generation == other.Generation
            && Lines.SequenceEqual(other.Lines);

        public override bool Equals(object? obj) => obj is GameResult other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int code = (Group * 397) ^ Generation;
                foreach (GameLine line in Lines)
                {
                    code = (code * 397) ^ line.GetHashCode();
                }

                return code;
            }
        }

        public static bool operator ==(GameResult left, GameResult right) => left.Equals(right);

        public static bool operator !=(GameResult left, GameResult right) => !left.Equals(right);

        public override string ToString() =>
            $"group {Group}.{Generation} : {string.Join(", ", Lines)}";

        /// <summary>One player's line in one finished match, already scored.</summary>
        public readonly struct GameLine : IEquatable<GameLine>
        {
            public GameLine(string name, int points, int placement, int kills, int outlasted)
            {
                Name = name;
                Points = points;
                Placement = placement;
                Kills = kills;
                Outlasted = outlasted;
            }

            public string Name { get; }

            /// <summary>Total points for this player in this match, all scoring rules combined.</summary>
            public int Points { get; }

            public int Placement { get; }

            public int Kills { get; }

            public int Outlasted { get; }

            public bool Equals(GameLine other) =>
                Name == other.Name
                && Points == other.Points
                && Placement == other.Placement
                && Kills == other.Kills
                && Outlasted == other.Outlasted;

            public override bool Equals(object? obj) => obj is GameLine other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int code = Name.GetHashCode();
                    code = (code * 397) ^ Points;
                    code = (code * 397) ^ Placement;
                    code = (code * 397) ^ Kills;

                    return (code * 397) ^ Outlasted;
                }
            }

            public static bool operator ==(GameLine left, GameLine right) => left.Equals(right);

            public static bool operator !=(GameLine left, GameLine right) => !left.Equals(right);

            public override string ToString() =>
                $"{Name} #{Placement}: {Points} ({Outlasted} outlasted, {Kills} kills)";
        }
    }
}
