using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Zones;

namespace Lms.Core.Audit
{
    /// <summary>The append-only audit log: everything that happened, in order.</summary>
    /// <remarks>
    ///     Assigns sequence numbers only; the caller supplies the timestamp and the host persists
    ///     <see cref="AuditFormat.Write"/>'s line. Records are kept in memory, uncapped.
    /// </remarks>
    public sealed class AuditLog
    {
        private readonly List<AuditRecord> records = new List<AuditRecord>();
        private readonly ReadOnlyCollection<AuditRecord> view;

        public AuditLog()
        {
            view = new ReadOnlyCollection<AuditRecord>(records);
        }

        /// <summary>The last number handed out; zero for an empty log.</summary>
        public long Sequence { get; private set; }

        /// <summary>Everything recorded, oldest first, as a read-only view.</summary>
        public IReadOnlyList<AuditRecord> Records => view;

        /// <summary>Stamps one event, appends it and returns the record.</summary>
        /// <remarks>
        ///     A record that is about a player but names none is refused before it takes a number, so
        ///     a gap in the numbering always means a lost record.
        /// </remarks>
        public AuditRecord Append(DateTimeOffset at, MatchStamp match, AuditRecordKind kind,
            string? player = null, string? actor = null, int? phase = null, WorldPoint? position = null,
            string? biome = null, double? amount = null, double? health = null, string? detail = null)
        {
            AuditRecord record = new AuditRecord(Sequence + 1, at, match, kind,
                player, actor, phase, position, biome, amount, health, detail);

            records.Add(record);
            Sequence = record.Sequence;

            return record;
        }
    }
}
