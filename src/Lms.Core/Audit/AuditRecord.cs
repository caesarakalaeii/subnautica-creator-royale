using System;
using Lms.Core.Zones;

namespace Lms.Core.Audit
{
    /// <summary>One line of the audit log: what happened, to whom, where and when.</summary>
    /// <remarks>
    ///     Which payload fields are set depends on the kind. Times are truncated to the millisecond
    ///     and measurements to the thousandth, so a write/read round trip is exact.
    /// </remarks>
    public readonly struct AuditRecord : IEquatable<AuditRecord>
    {
        /// <param name="sequence">Its place in the log, counting from one.</param>
        /// <param name="at">Wall clock of the client that decided this.</param>
        /// <param name="match">Which match, and how far into it.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="player">Who it happened to. Required for the kinds that are about a player.</param>
        /// <param name="actor">The other player involved, such as who fired the ring.</param>
        /// <param name="phase">Which phase, counting from one.</param>
        /// <param name="position">Where it happened.</param>
        /// <param name="biome">The name of the place.</param>
        /// <param name="amount">
        ///     Damage, or the trigger charge for <see cref="AuditRecordKind.RingFired"/>.
        /// </param>
        /// <param name="health">What was left afterwards.</param>
        /// <param name="detail">A short token the kind gives meaning to, like a cause of death.</param>
        public AuditRecord(long sequence, DateTimeOffset at, MatchStamp match, AuditRecordKind kind,
            string? player = null, string? actor = null, int? phase = null, WorldPoint? position = null,
            string? biome = null, double? amount = null, double? health = null, string? detail = null)
        {
            if (sequence < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), sequence,
                    "Records are numbered from one.");
            }

            if (IsAboutAPlayer(kind) && string.IsNullOrWhiteSpace(player))
            {
                throw new ArgumentException($"{kind} is about a player, so it needs a player name.",
                    nameof(player));
            }

            Sequence = sequence;
            TimestampUtc = ToWholeMilliseconds(at);
            Match = new MatchStamp(match.Group, match.Generation, ToWholeMilliseconds(match.Elapsed));
            Kind = kind;
            Player = player;
            Actor = actor;
            Phase = phase;
            Position = position.HasValue ? ToMillimetres(position.Value) : (WorldPoint?)null;
            Biome = biome;
            Amount = amount.HasValue ? ToThousandths(amount.Value) : (double?)null;
            Health = health.HasValue ? ToThousandths(health.Value) : (double?)null;
            Detail = detail;
        }

        /// <summary>Its place in the log, counting from one; monotonic within one log file.</summary>
        public long Sequence { get; }

        /// <summary>When, by the deciding client's clock, in UTC and to the millisecond.</summary>
        public DateTimeOffset TimestampUtc { get; }

        public MatchStamp Match { get; }

        public AuditRecordKind Kind { get; }

        public string? Player { get; }

        public string? Actor { get; }

        public int? Phase { get; }

        public WorldPoint? Position { get; }

        public string? Biome { get; }

        public double? Amount { get; }

        public double? Health { get; }

        public string? Detail { get; }

        public int Group => Match.Group;

        public int Generation => Match.Generation;

        /// <summary>Match time this happened at; what a replay orders by.</summary>
        public TimeSpan Elapsed => Match.Elapsed;

        /// <summary>Whether a kind is about one player and therefore has to name one.</summary>
        public static bool IsAboutAPlayer(AuditRecordKind kind)
        {
            switch (kind)
            {
                case AuditRecordKind.PlayerJoined:
                case AuditRecordKind.PlayerLeft:
                case AuditRecordKind.PlayerPosition:
                case AuditRecordKind.ZoneWarned:
                case AuditRecordKind.ZoneBite:
                case AuditRecordKind.ZoneArrears:
                case AuditRecordKind.PlayerHurt:
                case AuditRecordKind.PlayerEliminated:
                case AuditRecordKind.KillAttributed:
                case AuditRecordKind.RingFired:
                case AuditRecordKind.PlayerCaged:
                case AuditRecordKind.PlayerReleased:
                case AuditRecordKind.GameModeCorrected:
                case AuditRecordKind.BeaconRefused:
                case AuditRecordKind.PlayerReady:
                    return true;

                default:
                    return false;
            }
        }

        public bool Equals(AuditRecord other) =>
            Sequence == other.Sequence
            && TimestampUtc == other.TimestampUtc
            && Match.Equals(other.Match)
            && Kind == other.Kind
            && Player == other.Player
            && Actor == other.Actor
            && Phase == other.Phase
            && Nullable.Equals(Position, other.Position)
            && Biome == other.Biome
            && Nullable.Equals(Amount, other.Amount)
            && Nullable.Equals(Health, other.Health)
            && Detail == other.Detail;

        public override bool Equals(object? obj) => obj is AuditRecord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Sequence.GetHashCode();
                hash = hash * 397 ^ TimestampUtc.GetHashCode();
                hash = hash * 397 ^ Match.GetHashCode();
                hash = hash * 397 ^ (int)Kind;
                return hash * 397 ^ (Player?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() =>
            Player == null
                ? $"#{Sequence} {Kind} at {Elapsed}"
                : $"#{Sequence} {Kind} {Player} at {Elapsed}";

        private static DateTimeOffset ToWholeMilliseconds(DateTimeOffset at)
        {
            DateTimeOffset utc = at.ToUniversalTime();

            return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMillisecond));
        }

        private static TimeSpan ToWholeMilliseconds(TimeSpan span) =>
            TimeSpan.FromTicks(span.Ticks - span.Ticks % TimeSpan.TicksPerMillisecond);

        private static WorldPoint ToMillimetres(WorldPoint point) =>
            new WorldPoint(ToThousandths(point.X), ToThousandths(point.Y), ToThousandths(point.Z));

        private static double ToThousandths(double value) => Math.Truncate(value * 1000) / 1000;
    }
}
