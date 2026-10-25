using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Lms.Core.Match;

namespace Lms.Core.Audit
{
    /// <summary>One match (a group and a generation), reconstructed from its records.</summary>
    /// <remarks>
    ///     Each generation is a separate attempt with its own per-player state; only the last counts
    ///     towards the standings. The roster is whoever the records mention, since
    ///     <c>PlayerJoined</c> is written once per connection, not once per match.
    /// </remarks>
    public sealed class MatchAudit
    {
        private readonly List<PlayerAudit> players = new List<PlayerAudit>();
        private readonly Dictionary<string, PlayerAudit> byName =
            new Dictionary<string, PlayerAudit>(StringComparer.Ordinal);

        private readonly List<PlayerAudit> eliminated = new List<PlayerAudit>();
        private readonly ReadOnlyCollection<PlayerAudit> playerView;
        private readonly ReadOnlyCollection<PlayerAudit> eliminationView;

        internal MatchAudit(int group, int generation)
        {
            Group = group;
            Generation = generation;
            playerView = new ReadOnlyCollection<PlayerAudit>(players);
            eliminationView = new ReadOnlyCollection<PlayerAudit>(eliminated);
        }

        /// <summary>Which group this match is for.</summary>
        public int Group { get; }

        /// <summary>Which attempt at that group. A reset increments it.</summary>
        public int Generation { get; }

        /// <summary>The clock state; a new attempt is in the lobby until something starts it.</summary>
        public MatchClockState State { get; private set; }

        /// <summary>How far the match got: the elapsed time of its latest record.</summary>
        public TimeSpan Elapsed { get; private set; }

        /// <summary>How many phases fired, the collapse included.</summary>
        /// <remarks>Taken from the record's phase number rather than a count, so a lost line does not shift it.</remarks>
        public int PhasesFired { get; private set; }

        /// <summary>True once the wall finished closing and there was nowhere left to stand.</summary>
        public bool IsClosed { get; private set; }

        public bool HasEnded { get; private set; }

        /// <summary>
        ///     True once this attempt replaces earlier ones in the standings: it started, it ended,
        ///     or an operator's <c>reset</c> opened it.
        /// </summary>
        /// <remarks>
        ///     A lobby nobody started or reset does not count, so a restarted server's lobby does not
        ///     displace a finished game.
        /// </remarks>
        public bool Supersedes { get; private set; }

        /// <summary>
        ///     Whoever the ending named, or null. A zone that closes on the last two ends the match
        ///     without a winner.
        /// </summary>
        public string? Winner { get; private set; }

        /// <summary>Everybody the match mentioned, in the order it first mentioned them.</summary>
        public IReadOnlyList<PlayerAudit> Players => playerView;

        /// <summary>Who went out, first out first.</summary>
        public IReadOnlyList<PlayerAudit> EliminationOrder => eliminationView;

        /// <summary>Who was still standing: not eliminated and still connected.</summary>
        public IReadOnlyList<PlayerAudit> Survivors
        {
            get
            {
                List<PlayerAudit> standing = new List<PlayerAudit>();
                foreach (PlayerAudit player in players)
                {
                    if (player.IsAlive && !player.HasLeft)
                    {
                        standing.Add(player);
                    }
                }

                return standing;
            }
        }

        /// <summary>One player by name, or null if this match never mentioned them.</summary>
        public PlayerAudit? Player(string name)
        {
            PlayerAudit? player;

            return byName.TryGetValue(name, out player) ? player : null;
        }

        public override string ToString() =>
            $"group {Group}.{Generation}: {players.Count} players, {eliminated.Count} out, " +
            $"phase {PhasesFired}, {State}";

        internal void Apply(AuditRecord record)
        {
            if (record.Elapsed > Elapsed)
            {
                Elapsed = record.Elapsed;
            }

            switch (record.Kind)
            {
                case AuditRecordKind.MatchStarted:
                    // Whoever left the lobby and is not back never played, so drop them.
                    foreach (PlayerAudit gone in players.Where(p => p.HasLeft).ToList())
                    {
                        players.Remove(gone);
                        byName.Remove(gone.Name);
                    }

                    State = MatchClockState.Running;
                    Supersedes = true;
                    break;

                case AuditRecordKind.MatchResumed:
                    State = MatchClockState.Running;
                    Supersedes = true;
                    break;

                case AuditRecordKind.MatchReset:
                    Supersedes = true;
                    break;

                case AuditRecordKind.MatchPaused:
                    State = MatchClockState.Paused;
                    break;

                case AuditRecordKind.PhaseStarted:
                    PhasesFired = record.Phase.HasValue && record.Phase.Value > PhasesFired
                        ? record.Phase.Value
                        : PhasesFired + 1;
                    break;

                case AuditRecordKind.ZoneClosed:
                    IsClosed = true;
                    break;

                case AuditRecordKind.MatchEnded:
                    HasEnded = true;
                    Supersedes = true;
                    Winner = record.Player;
                    break;

                case AuditRecordKind.PlayerEliminated:
                    Eliminate(record);
                    break;

                case AuditRecordKind.KillAttributed:
                    // Credits the kill only; it is not an elimination and does not move placements.
                    Seen(record).AttributeTo(record.Actor);

                    // Enrol the killer so the credited kill is not lost.
                    if (record.Actor != null)
                    {
                        Roster(record.Actor);
                    }

                    break;

                case AuditRecordKind.PlayerCaged:
                    Seen(record);
                    if (record.Actor != null)
                    {
                        Roster(record.Actor).Caught();
                    }

                    break;

                default:
                    if (record.Player != null)
                    {
                        Seen(record);
                    }

                    break;
            }
        }

        private void Eliminate(AuditRecord record)
        {
            PlayerAudit player = Seen(record);

            // A second elimination is the same death reported twice (two clients can both decide it).
            if (!player.IsAlive)
            {
                return;
            }

            player.Eliminate(record.Elapsed, record.Detail, record.Actor, eliminated.Count + 1);
            eliminated.Add(player);
        }

        private PlayerAudit Seen(AuditRecord record)
        {
            PlayerAudit player = Roster(record.Player!);
            player.Seen(record);

            return player;
        }

        private PlayerAudit Roster(string name)
        {
            PlayerAudit? player;
            if (byName.TryGetValue(name, out player))
            {
                return player;
            }

            player = new PlayerAudit(name);
            byName.Add(name, player);
            players.Add(player);

            return player;
        }
    }
}
