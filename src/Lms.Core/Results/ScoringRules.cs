using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Results
{
    /// <summary>What a match is worth: a point for everybody you outlast, two for everybody you kill.</summary>
    /// <remarks>
    ///     Per-player-outlasted scaling works for any group size and rewards hunting as well as
    ///     hiding. An optional placement table (<see cref="ScoringRules(IReadOnlyList{int})"/>) pays
    ///     nothing by default.
    /// </remarks>
    public sealed class ScoringRules
    {
        private static readonly ReadOnlyCollection<int> NoTable = new ReadOnlyCollection<int>(new int[0]);

        private readonly ReadOnlyCollection<int> points;

        /// <param name="pointsPerPlayerOutlasted">What outlasting one player pays.</param>
        /// <param name="pointsPerKill">What a kill pays.</param>
        /// <param name="pointsByPlacement">
        ///     An optional table paid on top, best placement first, or null for none.
        /// </param>
        public ScoringRules(int pointsPerPlayerOutlasted, int pointsPerKill,
            IReadOnlyList<int>? pointsByPlacement = null)
        {
            PointsPerPlayerOutlasted = pointsPerPlayerOutlasted;
            PointsPerKill = pointsPerKill;

            if (pointsByPlacement == null)
            {
                points = NoTable;
                return;
            }

            if (pointsByPlacement.Count == 0)
            {
                throw new ArgumentException("A points table needs a row for at least first place.",
                    nameof(pointsByPlacement));
            }

            points = new ReadOnlyCollection<int>(new List<int>(pointsByPlacement));
        }

        /// <summary>A table and nothing else, for a bracket placing.</summary>
        public ScoringRules(IReadOnlyList<int> pointsByPlacement)
            : this(0, 0, Required(pointsByPlacement))
        {
        }

        /// <summary>The tournament's: a point per player outlasted, two per kill, no placement table.</summary>
        public static ScoringRules Default { get; } = new ScoringRules(1, 2);

        /// <summary>What outlasting one player pays.</summary>
        public int PointsPerPlayerOutlasted { get; }

        /// <summary>What a kill pays.</summary>
        public int PointsPerKill { get; }

        /// <summary>The bracket table, best placement first. Empty for a match.</summary>
        public IReadOnlyList<int> PointsByPlacement => points;

        /// <summary>What one placement pays, counting placements from one. Nothing without a table.</summary>
        public int PointsFor(int placement) =>
            placement >= 1 && placement <= points.Count ? points[placement - 1] : 0;

        /// <summary>What one player's match is worth, all currencies together.</summary>
        public int PointsFor(LedgerEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return (entry.Outlasted * PointsPerPlayerOutlasted)
                   + (entry.Kills * PointsPerKill)
                   + PointsFor(entry.Placement);
        }

        private static IReadOnlyList<int> Required(IReadOnlyList<int> pointsByPlacement)
        {
            if (pointsByPlacement == null)
            {
                throw new ArgumentNullException(nameof(pointsByPlacement));
            }

            return pointsByPlacement;
        }

        public override string ToString() =>
            $"{PointsPerPlayerOutlasted} per player outlasted, {PointsPerKill} per kill" +
            (points.Count == 0 ? string.Empty : $", placement {string.Join("/", points)}");
    }
}
