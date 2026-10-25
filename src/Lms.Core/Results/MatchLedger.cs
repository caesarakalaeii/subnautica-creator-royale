using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Audit;

namespace Lms.Core.Results
{
    /// <summary>Who came where in one match, read from the audit log.</summary>
    /// <remarks>
    ///     <list type="number">
    ///     <item>whoever the ending named is first, if they were still standing;</item>
    ///     <item>then everybody else still standing, tied on the next placement;</item>
    ///     <item>then everybody who went out, latest first.</item>
    ///     </list>
    ///     A disconnect counts as an exit at that moment (see <see cref="LedgerEntry.Left"/>).
    ///     Ties are listed by name, ordinal, so the order is deterministic.
    /// </remarks>
    public sealed class MatchLedger
    {
        private readonly Dictionary<string, LedgerEntry> byName;
        private readonly ReadOnlyCollection<LedgerEntry> entries;

        private MatchLedger(MatchAudit match, List<LedgerEntry> placed)
        {
            Group = match.Group;
            Generation = match.Generation;
            Audit = match;
            entries = new ReadOnlyCollection<LedgerEntry>(placed);
            byName = new Dictionary<string, LedgerEntry>(StringComparer.Ordinal);

            foreach (LedgerEntry entry in placed)
            {
                byName[entry.Name] = entry;
            }
        }

        /// <summary>Which group this match was.</summary>
        public int Group { get; }

        /// <summary>Which attempt at it. Only the last attempt counts towards the standings.</summary>
        public int Generation { get; }

        /// <summary>The match this was read from.</summary>
        public MatchAudit Audit { get; }

        /// <summary>Best placement first.</summary>
        public IReadOnlyList<LedgerEntry> Entries => entries;

        /// <summary>
        ///     Whoever holds first place alone, or null when it is shared and the match named nobody.
        /// </summary>
        public string? Winner
        {
            get
            {
                if (entries.Count == 0 || entries[0].Placement != 1)
                {
                    return null;
                }

                return entries.Count > 1 && entries[1].Placement == 1 ? null : entries[0].Name;
            }
        }

        public static MatchLedger Of(MatchAudit match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            Dictionary<string, int> kills = Kills(match);

            List<LedgerEntry> placed = new List<LedgerEntry>();
            int placement = 1;
            int total = Count(Blocks(match));

            foreach (List<PlayerAudit> block in Blocks(match))
            {
                // Everybody in a block shares a placement, so they outlast the same people.
                int outlasted = total - (placement - 1) - block.Count;

                foreach (PlayerAudit player in block)
                {
                    int scored;
                    kills.TryGetValue(player.Name, out scored);
                    placed.Add(new LedgerEntry(player, placement, outlasted, scored));
                }

                placement += block.Count;
            }

            return new MatchLedger(match, placed);
        }

        /// <summary>One player's line, or null if this match never mentioned them.</summary>
        public LedgerEntry? Entry(string name)
        {
            LedgerEntry? entry;

            return byName.TryGetValue(name, out entry) ? entry : null;
        }

        public override string ToString() =>
            $"group {Group}.{Generation}: {(Winner == null ? "no single winner" : Winner)}, " +
            $"{entries.Count} placed";

        /// <summary>Kills per player, derived from the attributions so corrections move the points.</summary>
        private static Dictionary<string, int> Kills(MatchAudit match)
        {
            Dictionary<string, int> kills = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (PlayerAudit player in match.Players)
            {
                string? killer = player.KilledBy;

                // No credit for killing yourself (e.g. a mistaken attribution to the victim).
                if (killer == null || killer == player.Name)
                {
                    continue;
                }

                int scored;
                kills.TryGetValue(killer, out scored);
                kills[killer] = scored + 1;
            }

            return kills;
        }

        private static int Count(IEnumerable<List<PlayerAudit>> blocks)
        {
            int total = 0;
            foreach (List<PlayerAudit> block in blocks)
            {
                total += block.Count;
            }

            return total;
        }

        /// <summary>The players in placement order, grouped by shared placement.</summary>
        private static IEnumerable<List<PlayerAudit>> Blocks(MatchAudit match)
        {
            List<PlayerAudit> standing = new List<PlayerAudit>(match.Survivors);
            standing.Sort(ByName);

            PlayerAudit? named = null;
            if (match.Winner != null)
            {
                foreach (PlayerAudit player in standing)
                {
                    if (player.Name == match.Winner)
                    {
                        named = player;
                        break;
                    }
                }
            }

            if (named != null)
            {
                standing.Remove(named);
                yield return new List<PlayerAudit> { named };
            }

            if (standing.Count > 0)
            {
                yield return standing;
            }

            List<PlayerAudit> exits = new List<PlayerAudit>();
            foreach (PlayerAudit player in match.Players)
            {
                if (!player.IsAlive || player.HasLeft)
                {
                    exits.Add(player);
                }
            }

            exits.Sort(LatestOutFirst);

            int start = 0;
            while (start < exits.Count)
            {
                int end = start + 1;

                // Exits at the same moment with the same elimination order (e.g. simultaneous
                // disconnects) tie; eliminations never do.
                while (end < exits.Count && SameExit(exits[start], exits[end]))
                {
                    end++;
                }

                yield return exits.GetRange(start, end - start);
                start = end;
            }
        }

        private static bool SameExit(PlayerAudit left, PlayerAudit right) =>
            ExitedAt(left) == ExitedAt(right) && left.EliminationOrder == right.EliminationOrder;

        private static TimeSpan ExitedAt(PlayerAudit player) =>
            player.EliminatedAt ?? player.LeftAt ?? TimeSpan.Zero;

        private static int ByName(PlayerAudit left, PlayerAudit right) =>
            string.CompareOrdinal(left.Name, right.Name);

        private static int LatestOutFirst(PlayerAudit left, PlayerAudit right)
        {
            int byTime = ExitedAt(right).CompareTo(ExitedAt(left));
            if (byTime != 0)
            {
                return byTime;
            }

            int byOrder = right.EliminationOrder.CompareTo(left.EliminationOrder);

            return byOrder != 0 ? byOrder : ByName(left, right);
        }
    }
}
