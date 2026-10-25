using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Lms.Core.Audit;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     The <see cref="PlayerEvent"/>s one client recorded since its last send, carried on the
    ///     <c>LmsPlayerReport</c> envelope and distinguished from a snapshot by its header.
    /// </summary>
    /// <remarks>
    ///     Separate from the snapshot because batches are exactly-once and ordered while snapshots are
    ///     latest-wins. Wire form: a header line, then one line of tab-separated <c>key=value</c> pairs
    ///     per event. The server drops a batch whose generation is not the current match.
    /// </remarks>
    public sealed class PlayerEventBatch
    {
        /// <summary>The first token of the wire form; bumped when a field's meaning changes.</summary>
        public const string WireHeader = "lms-events/1";

        private static readonly char[] PairSplit = { '=' };
        private static readonly char[] LineSplit = { '\n' };

        private readonly ReadOnlyCollection<PlayerEvent> events;

        /// <param name="generation">
        ///     Which attempt at the match these belong to. Zero if the client has not seen a match; the
        ///     server drops such a batch.
        /// </param>
        /// <param name="events">What happened, oldest first.</param>
        /// <param name="lost">How many events were dropped before this batch was built.</param>
        public PlayerEventBatch(int generation, IReadOnlyList<PlayerEvent> events, int lost = 0)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (events.Count == 0)
            {
                throw new ArgumentException("An empty batch is never worth a packet.", nameof(events));
            }

            if (lost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lost), lost, "A negative loss is not a loss.");
            }

            Generation = generation;
            Lost = lost;
            this.events = new ReadOnlyCollection<PlayerEvent>(new List<PlayerEvent>(events));
        }

        public int Generation { get; }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<PlayerEvent> Events => events;

        /// <summary>How many events were dropped before this batch, or zero.</summary>
        public int Lost { get; }

        /// <summary>Encodes the wire form. Optional fields are omitted rather than written empty.</summary>
        public string Encode()
        {
            StringBuilder wire = new StringBuilder(64 + events.Count * 64);

            wire.Append(WireHeader);
            Pair(wire, "gen", Generation.ToString(CultureInfo.InvariantCulture));

            if (Lost > 0)
            {
                Pair(wire, "lost", Lost.ToString(CultureInfo.InvariantCulture));
            }

            foreach (PlayerEvent happened in events)
            {
                wire.Append('\n');
                Pair(wire, "k", happened.Kind.ToString(), first: true);
                Pair(wire, "t", Milliseconds(happened.Elapsed));

                if (happened.Position.HasValue)
                {
                    WorldPoint at = happened.Position.Value;
                    Pair(wire, "x", Measured(at.X));
                    Pair(wire, "y", Measured(at.Y));
                    Pair(wire, "z", Measured(at.Z));
                }

                Optional(wire, "b", happened.Biome);
                OptionalNumber(wire, "a", happened.Amount);
                OptionalNumber(wire, "h", happened.Health);

                if (happened.Phase.HasValue)
                {
                    Pair(wire, "p", happened.Phase.Value.ToString(CultureInfo.InvariantCulture));
                }

                Optional(wire, "d", happened.Detail);
            }

            return wire.ToString();
        }

        /// <summary>
        ///     Decodes a batch, or returns false.
        /// </summary>
        /// <remarks>
        ///     An unknown header version is refused; unknown fields are ignored for forward compatibility.
        ///     Events with no kind or a kind not allowed by <see cref="PlayerEvent.CanBeReported"/> are
        ///     dropped individually and the rest of the batch is kept.
        /// </remarks>
        public static bool TryDecode(string? wire, out PlayerEventBatch batch)
        {
            batch = null!;

            if (string.IsNullOrWhiteSpace(wire))
            {
                return false;
            }

            string[] lines = wire!.Split(LineSplit);
            string[] head = lines[0].Split('\t');

            if (head[0] != WireHeader)
            {
                return false;
            }

            int generation = 0;
            int lost = 0;

            foreach (KeyValuePair<string, string> pair in Pairs(head))
            {
                switch (pair.Key)
                {
                    case "gen": generation = Whole(pair.Value); break;
                    case "lost": lost = Whole(pair.Value); break;
                }
            }

            List<PlayerEvent> events = new List<PlayerEvent>(lines.Length);

            for (int i = 1; i < lines.Length; i++)
            {
                if (TryEvent(lines[i], out PlayerEvent happened))
                {
                    events.Add(happened);
                }
            }

            if (events.Count == 0)
            {
                // Nothing survived: a bare header, or only refused kinds.
                return false;
            }

            batch = new PlayerEventBatch(generation, events, lost);

            return true;
        }

        public override string ToString() =>
            $"{events.Count} event(s) in generation {Generation}" + (Lost > 0 ? $", {Lost} lost" : string.Empty);

        private static bool TryEvent(string line, out PlayerEvent happened)
        {
            happened = default;

            AuditRecordKind? kind = null;
            long elapsedMs = 0;
            double? x = null;
            double? y = null;
            double? z = null;
            string? biome = null;
            double? amount = null;
            double? health = null;
            int? phase = null;
            string? detail = null;

            foreach (KeyValuePair<string, string> pair in Pairs(line.Split('\t')))
            {
                switch (pair.Key)
                {
                    case "k": kind = Kind(pair.Value); break;
                    case "t": elapsedMs = Whole(pair.Value); break;
                    case "x": x = Number(pair.Value); break;
                    case "y": y = Number(pair.Value); break;
                    case "z": z = Number(pair.Value); break;
                    case "b": biome = pair.Value; break;
                    case "a": amount = Number(pair.Value); break;
                    case "h": health = Number(pair.Value); break;
                    case "p": phase = Whole(pair.Value); break;
                    case "d": detail = pair.Value; break;
                }
            }

            if (kind == null || elapsedMs < 0)
            {
                return false;
            }

            WorldPoint? position = x.HasValue && y.HasValue && z.HasValue
                ? new WorldPoint(x.Value, y.Value, z.Value)
                : (WorldPoint?)null;

            happened = new PlayerEvent(kind.Value, TimeSpan.FromMilliseconds(elapsedMs), position,
                biome, amount, health, phase, detail);

            return true;
        }

        /// <summary>Every <c>key=value</c> pair among the tokens, skipping anything that is not one.</summary>
        private static IEnumerable<KeyValuePair<string, string>> Pairs(string[] tokens)
        {
            foreach (string token in tokens)
            {
                string[] pair = token.Split(PairSplit, 2);

                if (pair.Length == 2)
                {
                    yield return new KeyValuePair<string, string>(pair[0], pair[1]);
                }
            }
        }

        /// <summary>
        ///     Parses a kind by name, as the audit log writes it. Kinds a client may not report return null.
        /// </summary>
        private static AuditRecordKind? Kind(string written)
        {
            foreach (AuditRecordKind candidate in ReportableKinds)
            {
                if (candidate.ToString() == written)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        ///     Listed explicitly because <c>Enum.GetValues</c> allocates on every call on netstandard2.0.
        /// </summary>
        private static readonly AuditRecordKind[] ReportableKinds =
        {
            AuditRecordKind.ZoneWarned,
            AuditRecordKind.ZoneBite,
            AuditRecordKind.RingFired,
            AuditRecordKind.PlayerCaged,
            AuditRecordKind.PlayerReleased,
            AuditRecordKind.BeaconRefused,
            AuditRecordKind.PlayerReady
        };

        private static int Whole(string written) =>
            int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;

        private static double? Number(string written) =>
            double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : (double?)null;

        /// <summary>
        ///     Truncated to the millisecond (and numbers to the thousandth), the precision
        ///     <see cref="AuditRecord"/> keeps, so a round trip is exact.
        /// </summary>
        private static string Milliseconds(TimeSpan elapsed) =>
            (elapsed.Ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture);

        private static string Measured(double value) =>
            (Math.Truncate(value * 1000) / 1000).ToString("0.###", CultureInfo.InvariantCulture);

        private static void OptionalNumber(StringBuilder wire, string key, double? value)
        {
            if (value.HasValue)
            {
                Pair(wire, key, Measured(value.Value));
            }
        }

        private static void Optional(StringBuilder wire, string key, string? value)
        {
            if (value != null)
            {
                Pair(wire, key, Clean(value));
            }
        }

        private static void Pair(StringBuilder wire, string key, string value, bool first = false)
        {
            if (!first)
            {
                wire.Append('\t');
            }

            wire.Append(key).Append('=').Append(value);
        }

        /// <summary>Replaces tabs and newlines, which would otherwise split a pair or invent an event.</summary>
        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}
