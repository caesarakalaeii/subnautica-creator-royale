using System;
using Lms.Core.Audit;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>One line of the event feed.</summary>
    /// <remarks>
    ///     A projection of <see cref="AuditRecord"/>, kept separate so feed changes never alter the
    ///     durable audit log format. <c>Kind</c> uses the log's own word.
    /// </remarks>
    public sealed class EventTelemetry
    {
        public EventTelemetry(long sequence, int group, int generation, DateTimeOffset at,
            TimeSpan elapsed, AuditRecordKind kind,
            string? player, string? actor, int? phase, double? amount, string? detail,
            WorldPoint? position, string? biome)
        {
            Sequence = sequence;
            Group = group;
            Generation = generation;
            At = at;
            Elapsed = elapsed;
            Kind = kind;
            Player = player;
            Actor = actor;
            Phase = phase;
            Amount = amount;
            Detail = detail;
            Position = position;
            Biome = biome;
        }

        /// <summary>Its place in the log. The feed's cursor: a client asks for everything after it.</summary>
        public long Sequence { get; }

        /// <summary>Which group's match this belongs to.</summary>
        public int Group { get; }

        /// <summary>
        ///     Which attempt at that match; a reset changes it.
        /// </summary>
        /// <remarks>
        ///     Carried per event rather than inferred from the snapshot, because one push can straddle a reset.
        /// </remarks>
        public int Generation { get; }

        public DateTimeOffset At { get; }

        /// <summary>Match time, which a timeline is drawn against.</summary>
        public TimeSpan Elapsed { get; }

        public AuditRecordKind Kind { get; }

        public string? Player { get; }

        /// <summary>The other player: who fired the ring, who gets the kill.</summary>
        public string? Actor { get; }

        public int? Phase { get; }

        public double? Amount { get; }

        public string? Detail { get; }

        /// <summary>Where it happened, for the map's pins; null when it has no place.</summary>
        public WorldPoint? Position { get; }

        public string? Biome { get; }

        /// <summary>Projects an audit record.</summary>
        public static EventTelemetry Of(AuditRecord record) =>
            new EventTelemetry(record.Sequence, record.Group, record.Generation, record.TimestampUtc,
                record.Elapsed, record.Kind,
                record.Player, record.Actor, record.Phase, record.Amount, record.Detail,
                record.Position, record.Biome);
    }
}
