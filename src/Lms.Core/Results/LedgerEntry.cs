using System;
using Lms.Core.Audit;

namespace Lms.Core.Results
{
    /// <summary>Where one player came in one match, and how they got there.</summary>
    public sealed class LedgerEntry
    {
        internal LedgerEntry(PlayerAudit player, int placement, int outlasted, int kills)
        {
            Audit = player;
            Placement = placement;
            Outlasted = outlasted;
            Kills = kills;
        }

        public string Name => Audit.Name;

        /// <summary>
        ///     One is best. Tied players share a placement and the next is placed after all of them
        ///     (two firsts, then a third).
        /// </summary>
        public int Placement { get; }

        /// <summary>How many of this match's players went out before them; one point each.</summary>
        /// <remarks>Players who share a placement outlast the same people.</remarks>
        public int Outlasted { get; }

        /// <summary>How many of this match's players they killed.</summary>
        /// <remarks>Derived from the log's attributions, so a later correction moves the points.</remarks>
        public int Kills { get; }

        /// <summary>Still standing at the end: never eliminated, and still connected.</summary>
        public bool Survived => Audit.IsAlive && !Audit.HasLeft;

        /// <summary>Left the server without being eliminated; distinct from a death.</summary>
        public bool Left => Audit.IsAlive && Audit.HasLeft;

        /// <summary>When they went out, either way, or null if they were still there at the end.</summary>
        public TimeSpan? ExitedAt => Audit.EliminatedAt ?? Audit.LeftAt;

        /// <summary>The logged cause of their exit, or null.</summary>
        public string? Cause => Audit.Cause;

        /// <summary>Their full match record: damage, cages, positions.</summary>
        public PlayerAudit Audit { get; }

        public override string ToString() =>
            $"{Placement}. {Name}{(Survived ? " (standing)" : Cause == null ? string.Empty : $" ({Cause})")}";
    }
}
