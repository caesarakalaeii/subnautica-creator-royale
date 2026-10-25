using System;
using Lms.Core.Zones;

namespace Lms.Core.Audit
{
    /// <summary>One player's match, reconstructed from the log.</summary>
    public sealed class PlayerAudit
    {
        internal PlayerAudit(string name)
        {
            Name = name;
        }

        /// <summary>Their in-game name, which is how the log identifies them.</summary>
        public string Name { get; }

        /// <summary>False once the log says they were eliminated.</summary>
        public bool IsAlive => EliminationOrder == 0;

        /// <summary>Whether they disconnected. Distinct from elimination and scored separately.</summary>
        public bool HasLeft { get; private set; }

        /// <summary>When they joined, if the log saw them join rather than simply appear.</summary>
        public TimeSpan? JoinedAt { get; private set; }

        /// <summary>When they went out, or null if they did not.</summary>
        public TimeSpan? EliminatedAt { get; private set; }

        /// <summary>When they disconnected, or null.</summary>
        public TimeSpan? LeftAt { get; private set; }

        /// <summary>Match time their last absence was charged, or null if never.</summary>
        /// <remarks>
        ///     Makes charging idempotent: a charge later than <see cref="LeftAt"/> means this absence
        ///     is already paid for.
        /// </remarks>
        public TimeSpan? ArrearsAt { get; private set; }

        /// <summary>Match time of the last record that mentioned them.</summary>
        public TimeSpan LastSeen { get; private set; }

        /// <summary>Where they went out in the order: first out is one, and zero means still in.</summary>
        public int EliminationOrder { get; private set; }

        /// <summary>The cause as logged, e.g. <c>zone</c>, <c>creature</c>, <c>drowned</c>.</summary>
        public string? Cause { get; private set; }

        /// <summary>Who gets the kill, or null when nobody does.</summary>
        /// <remarks>
        ///     Set from the elimination and overridden by any later
        ///     <see cref="AuditRecordKind.KillAttributed"/>.
        /// </remarks>
        public string? KilledBy { get; private set; }

        /// <summary>The last place the log put them.</summary>
        public WorldPoint? LastPosition { get; private set; }

        /// <summary>The biome name of that place.</summary>
        public string? LastBiome { get; private set; }

        /// <summary>Health as of the last record that carried one.</summary>
        public double? Health { get; private set; }

        /// <summary>All damage taken, added up.</summary>
        public double DamageTaken { get; private set; }

        /// <summary>How many times the zone bit them.</summary>
        public int Bites { get; private set; }

        /// <summary>How often they were standing where the wall was going.</summary>
        public int Warnings { get; private set; }

        /// <summary>Rings they fired.</summary>
        public int RingsFired { get; private set; }

        /// <summary>Rings of theirs that caught somebody.</summary>
        public int Catches { get; private set; }

        /// <summary>How often somebody else's ring held them.</summary>
        public int TimesCaged { get; private set; }

        /// <summary>How many times another player damaged them; counted apart from <see cref="Bites"/>.</summary>
        public int HitsTaken { get; private set; }

        /// <summary>Whether a ring is holding them right now, as far as the log knows.</summary>
        /// <remarks>
        ///     Joining and leaving clear it, since a client that dropped while caged never reports
        ///     the release.
        /// </remarks>
        public bool IsCaged { get; private set; }

        public override string ToString() =>
            IsAlive
                ? $"{Name}, in"
                : $"{Name}, out {EliminationOrder}{(Cause == null ? string.Empty : $" ({Cause})")}";

        /// <summary>Updates their state from a record that mentions them.</summary>
        internal void Seen(AuditRecord record)
        {
            LastSeen = record.Elapsed;

            if (record.Position.HasValue)
            {
                LastPosition = record.Position;
            }

            if (record.Biome != null)
            {
                LastBiome = record.Biome;
            }

            if (record.Health.HasValue)
            {
                Health = record.Health;
            }

            switch (record.Kind)
            {
                case AuditRecordKind.PlayerJoined:
                    JoinedAt = record.Elapsed;
                    HasLeft = false;
                    IsCaged = false;
                    break;

                case AuditRecordKind.PlayerLeft:
                    HasLeft = true;
                    LeftAt = record.Elapsed;
                    IsCaged = false;
                    break;

                case AuditRecordKind.ZoneWarned:
                    Warnings++;
                    break;

                case AuditRecordKind.ZoneBite:
                    Bites++;
                    DamageTaken += record.Amount ?? 0;
                    break;

                case AuditRecordKind.ZoneArrears:
                    // Counts as damage but not as a bite: the player was absent.
                    ArrearsAt = record.Elapsed;
                    DamageTaken += record.Amount ?? 0;
                    break;

                case AuditRecordKind.PlayerHurt:
                    // Counts as damage but not as a bite: bites are zone damage only.
                    HitsTaken++;
                    DamageTaken += record.Amount ?? 0;
                    break;

                case AuditRecordKind.RingFired:
                    RingsFired++;
                    break;

                case AuditRecordKind.PlayerCaged:
                    TimesCaged++;
                    IsCaged = true;
                    break;

                case AuditRecordKind.PlayerReleased:
                    IsCaged = false;
                    break;

                default:
                    break;
            }
        }

        internal void Caught()
        {
            Catches++;
        }

        internal void Eliminate(TimeSpan at, string? cause, string? killedBy, int order)
        {
            EliminationOrder = order;
            EliminatedAt = at;
            Cause = cause;
            KilledBy = killedBy;
        }

        /// <summary>An operator's ruling on who gets this kill; the last one wins, and null means nobody.</summary>
        internal void AttributeTo(string? killer)
        {
            KilledBy = killer;
        }
    }
}
