using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lms.Core.Wire;
using Lms.Core.Zones;

namespace Lms.Core.Audit
{
    /// <summary>Writes and reads one line of the audit log.</summary>
    /// <remarks>
    ///     Six tab-separated columns (sequence, timestamp, elapsed ms, group, generation, kind), then
    ///     <c>key=value</c> pairs. Unknown keys are ignored so newer logs still replay; an unknown kind
    ///     is rejected. Adding a key is compatible, moving a column is not.
    /// </remarks>
    public static class AuditFormat
    {
        /// <summary>The version in <see cref="Header"/>. It moves when a column moves.</summary>
        public const int Version = 1;

        /// <summary>The first line of a log file; reads as a comment so older readers skip it.</summary>
        public const string Header = "#lms-audit 1";

        private const int ColumnCount = 6;
        private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        /// <summary>Three decimals: millimetres and thousandths.</summary>
        private const string NumberFormat = "0.###";

        /// <summary>Reads a log file back as the server runs that wrote it, oldest first.</summary>
        /// <remarks>
        ///     A new run starts at each header and wherever the sequence goes back, since a restarted
        ///     server may append to the same file.
        /// </remarks>
        /// <param name="lines">The file's lines.</param>
        /// <param name="unreadable">How many lines were neither a record nor blank or a comment.</param>
        public static IReadOnlyList<IReadOnlyList<AuditRecord>> ReadRuns(IEnumerable<string> lines,
            out int unreadable)
        {
            List<IReadOnlyList<AuditRecord>> runs = new List<IReadOnlyList<AuditRecord>>();
            List<AuditRecord> run = new List<AuditRecord>();
            unreadable = 0;

            foreach (string line in lines)
            {
                if (line.Trim() == Header)
                {
                    run = Close(runs, run);
                    continue;
                }

                if (!TryRead(line, out AuditRecord record, out string? problem))
                {
                    if (problem != null)
                    {
                        unreadable++;
                    }

                    continue;
                }

                if (run.Count > 0 && record.Sequence <= run[run.Count - 1].Sequence)
                {
                    run = Close(runs, run);
                }

                run.Add(record);
            }

            Close(runs, run);

            return runs;
        }

        /// <summary>Stores a non-empty run and returns the list for the next one.</summary>
        private static List<AuditRecord> Close(List<IReadOnlyList<AuditRecord>> runs, List<AuditRecord> run)
        {
            if (run.Count == 0)
            {
                return run;
            }

            runs.Add(run);

            return new List<AuditRecord>();
        }

        /// <summary>The line for one record, without a trailing newline.</summary>
        public static string Write(AuditRecord record)
        {
            StringBuilder line = new StringBuilder(160);

            line.Append(record.Sequence.ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(record.TimestampUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            line.Append('\t').Append(((long)record.Elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(record.Group.ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(record.Generation.ToString(CultureInfo.InvariantCulture));
            line.Append('\t').Append(record.Kind.ToString());

            Pair(line, "player", record.Player);
            Pair(line, "actor", record.Actor);

            if (record.Phase.HasValue)
            {
                Pair(line, "phase", record.Phase.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (record.Position.HasValue)
            {
                WorldPoint point = record.Position.Value;
                Pair(line, "pos", $"{Number(point.X)},{Number(point.Y)},{Number(point.Z)}");
            }

            Pair(line, "biome", record.Biome);

            if (record.Amount.HasValue)
            {
                Pair(line, "amount", Number(record.Amount.Value));
            }

            if (record.Health.HasValue)
            {
                Pair(line, "health", Number(record.Health.Value));
            }

            Pair(line, "detail", record.Detail);

            return line.ToString();
        }

        /// <summary>Reads one line back.</summary>
        /// <param name="line">A line from a log file.</param>
        /// <param name="record">The record, if there was one.</param>
        /// <param name="problem">Why the line is not a record, or null when it is blank or a comment.</param>
        /// <returns>True when <paramref name="record"/> is a record.</returns>
        public static bool TryRead(string line, out AuditRecord record, out string? problem)
        {
            record = default;
            problem = null;

            if (string.IsNullOrWhiteSpace(line) || line.TrimStart()[0] == '#')
            {
                return false;
            }

            string[] columns = line.Split('\t');
            if (columns.Length < ColumnCount)
            {
                problem = $"a record has {ColumnCount} columns before its payload; this line has {columns.Length}";
                return false;
            }

            if (!long.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
                || sequence < 1)
            {
                problem = $"'{columns[0]}' is not a sequence number";
                return false;
            }

            if (!DateTimeOffset.TryParseExact(columns[1], TimestampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at))
            {
                problem = $"'{columns[1]}' is not a timestamp";
                return false;
            }

            if (!long.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long elapsedMs)
                || elapsedMs < 0)
            {
                problem = $"'{columns[2]}' is not an elapsed time in milliseconds";
                return false;
            }

            if (!int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int group)
                || group < 1)
            {
                problem = $"'{columns[3]}' is not a group";
                return false;
            }

            if (!int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation)
                || generation < 1)
            {
                problem = $"'{columns[4]}' is not a generation";
                return false;
            }

            if (!Enum.TryParse(columns[5], out AuditRecordKind kind)
                || !Enum.IsDefined(typeof(AuditRecordKind), kind))
            {
                problem = $"'{columns[5]}' is not a kind this build knows about";
                return false;
            }

            string? player = null;
            string? actor = null;
            int? phase = null;
            WorldPoint? position = null;
            string? biome = null;
            double? amount = null;
            double? health = null;
            string? detail = null;

            for (int i = ColumnCount; i < columns.Length; i++)
            {
                string token = columns[i];
                if (token.Length == 0)
                {
                    continue;
                }

                int split = token.IndexOf('=');
                if (split < 1)
                {
                    problem = $"'{token}' is not a key=value pair";
                    return false;
                }

                string key = token.Substring(0, split);
                string value = WireText.Unescape(token.Substring(split + 1));

                switch (key)
                {
                    case "player":
                        player = value;
                        break;

                    case "actor":
                        actor = value;
                        break;

                    case "phase":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPhase))
                        {
                            problem = $"'{token}' is not a phase number";
                            return false;
                        }

                        phase = parsedPhase;
                        break;

                    case "pos":
                        if (!TryReadPosition(value, out WorldPoint parsedPosition))
                        {
                            problem = $"'{token}' is not a pos of three numbers";
                            return false;
                        }

                        position = parsedPosition;
                        break;

                    case "biome":
                        biome = value;
                        break;

                    case "amount":
                        if (!TryReadNumber(value, out double parsedAmount))
                        {
                            problem = $"'{token}' is not an amount";
                            return false;
                        }

                        amount = parsedAmount;
                        break;

                    case "health":
                        if (!TryReadNumber(value, out double parsedHealth))
                        {
                            problem = $"'{token}' is not a health";
                            return false;
                        }

                        health = parsedHealth;
                        break;

                    case "detail":
                        detail = value;
                        break;

                    default:
                        // A key from a newer writer: ignored so the record still replays.
                        break;
                }
            }

            if (AuditRecord.IsAboutAPlayer(kind) && string.IsNullOrWhiteSpace(player))
            {
                problem = $"{kind} is about a player and this line names no player";
                return false;
            }

            record = new AuditRecord(sequence, at,
                new MatchStamp(group, generation, TimeSpan.FromMilliseconds(elapsedMs)), kind,
                player, actor, phase, position, biome, amount, health, detail);

            return true;
        }

        private static void Pair(StringBuilder line, string key, string? value)
        {
            if (value == null)
            {
                return;
            }

            line.Append('\t').Append(key).Append('=').Append(WireText.Escape(value));
        }

        private static string Number(double value) => value.ToString(NumberFormat, CultureInfo.InvariantCulture);

        private static bool TryReadNumber(string value, out double number) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        private static bool TryReadPosition(string value, out WorldPoint point)
        {
            point = default;

            string[] parts = value.Split(',');
            if (parts.Length != 3
                || !TryReadNumber(parts[0], out double x)
                || !TryReadNumber(parts[1], out double y)
                || !TryReadNumber(parts[2], out double z))
            {
                return false;
            }

            point = new WorldPoint(x, y, z);
            return true;
        }
    }
}
