using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Audit
{
    /// <summary>Folds an audit log back into the matches it describes.</summary>
    /// <remarks>
    ///     The standings are computed from this replay. Records can be applied one at a time so a
    ///     live feed can query the current match after each one.
    /// </remarks>
    public sealed class AuditReplay
    {
        private readonly List<MatchAudit> matches = new List<MatchAudit>();
        private readonly ReadOnlyCollection<MatchAudit> view;

        private MatchAudit? current;

        public AuditReplay()
        {
            view = new ReadOnlyCollection<MatchAudit>(matches);
        }

        /// <summary>Replays a whole log in one go.</summary>
        public static AuditReplay Of(IEnumerable<AuditRecord> records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            AuditReplay replay = new AuditReplay();
            foreach (AuditRecord record in records)
            {
                replay.Apply(record);
            }

            return replay;
        }

        /// <summary>The number of the last record applied; zero before the first.</summary>
        public long Sequence { get; private set; }

        /// <summary>Every match the log describes, in the order it played them.</summary>
        public IReadOnlyList<MatchAudit> Matches => view;

        /// <summary>The match the log is in the middle of, or null before the first record.</summary>
        public MatchAudit? Match => current;

        /// <summary>Applies one record.</summary>
        /// <remarks>Throws on a record that does not move the sequence forward, so an edited log is noticed.</remarks>
        public void Apply(AuditRecord record)
        {
            if (record.Sequence <= Sequence)
            {
                throw new InvalidOperationException(
                    $"record #{record.Sequence} arrives after #{Sequence}, and an append-only log " +
                    "does not go backwards or repeat itself.");
            }

            Sequence = record.Sequence;

            // A new group or generation is a new match; the record's stamp says which.
            if (current == null || current.Group != record.Group || current.Generation != record.Generation)
            {
                current = new MatchAudit(record.Group, record.Generation);
                matches.Add(current);
            }

            current.Apply(record);
        }

        public override string ToString() =>
            current == null
                ? "nothing replayed"
                : $"{matches.Count} match(es), {Sequence} records: {current}";
    }
}
