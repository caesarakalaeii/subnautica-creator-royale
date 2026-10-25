using System;
using System.Globalization;
using System.Text;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>
    ///     What the server tells every client about the match: where it sits on the shared clock,
    ///     which phase it is in, and the geometry of the wall.
    /// </summary>
    /// <remarks>
    ///     The server owns the zone; clients only draw and apply it. Only the current phase's geometry
    ///     travels, and the <see cref="MatchAnchor"/> lets clients advance it between broadcasts.
    /// </remarks>
    public readonly struct MatchBroadcast : IEquatable<MatchBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-zone/1";

        private const string AliveField = "alive";
        private const string DestinationField = "dest";

        internal MatchBroadcast(MatchAnchor anchor, int totalPhases,
            TimeSpan phaseStartedAt, TimeSpan? nextPhaseAt, TimeSpan? warningAt, ZoneShrink shrink,
            int? alive = null, string? destination = null)
        {
            Anchor = anchor;
            TotalPhases = totalPhases;
            PhaseStartedAt = phaseStartedAt;
            NextPhaseAt = nextPhaseAt;
            WarningAt = warningAt;
            Shrink = shrink;
            Alive = alive;
            Destination = destination;
        }

        /// <summary>Where the match sits on the world clock: group, generation, state and epoch.</summary>
        public MatchAnchor Anchor { get; }

        /// <summary>
        ///     How many contestants are still in, from <c>PlayerRoster.Alive</c> so the HUD matches the
        ///     overlay. Null when the server did not say.
        /// </summary>
        public int? Alive { get; }

        /// <summary>
        ///     Where this group's wall is closing to, by name, or null when <c>flags.showDestination</c>
        ///     is off. Withheld names are never sent, so a modified client cannot reveal them.
        /// </summary>
        public string? Destination { get; }

        /// <summary>How many phases have fired, the collapse counted; read off the geometry.</summary>
        public int CompletedPhases => Shrink.PhaseNumber;

        /// <summary>How many phases the config wrote. A match runs one more: the final collapse.</summary>
        public int TotalPhases { get; }

        /// <summary>Match time the current phase began at, which is what the wall shrinks from.</summary>
        public TimeSpan PhaseStartedAt { get; }

        /// <summary>Match time the next phase is due at, or null once the schedule is spent.</summary>
        public TimeSpan? NextPhaseAt { get; }

        /// <summary>
        ///     Match time the warning window opens at, or null once the schedule is spent. Sent as a
        ///     moment rather than as a lead, so the client needs no cadence of its own.
        /// </summary>
        public TimeSpan? WarningAt { get; }

        /// <summary>The wall: where it came from, where it is going, and what it costs.</summary>
        public ZoneShrink Shrink { get; }

        public int Group => Anchor.Group;

        public int Generation => Anchor.Generation;

        public MatchClockState State => Anchor.State;

        /// <summary>Whether the zone is allowed to hurt anybody: not in the lobby, not in a pause.</summary>
        public bool IsLive => Anchor.State == MatchClockState.Running;

        /// <summary>True once the authored squeeze is spent and the zone is closing to nothing.</summary>
        public bool IsCollapsing => CompletedPhases > TotalPhases;

        /// <summary>Match time so far. Paused time does not count.</summary>
        public TimeSpan ElapsedAt(TimeSpan worldNow) => Anchor.ElapsedAt(worldNow);

        /// <summary>How long the current phase has been closing for.</summary>
        public TimeSpan SinceLastPhaseAt(TimeSpan worldNow)
        {
            TimeSpan since = ElapsedAt(worldNow) - PhaseStartedAt;

            return since > TimeSpan.Zero ? since : TimeSpan.Zero;
        }

        /// <summary>Where the wall is, at a moment on the clock every client shares.</summary>
        public ZoneState ZoneAt(TimeSpan worldNow) => Shrink.At(SinceLastPhaseAt(worldNow));

        /// <summary>Time until the next phase, for the HUD countdown. Zero once the schedule is spent.</summary>
        public TimeSpan UntilNextPhaseAt(TimeSpan worldNow)
        {
            if (!NextPhaseAt.HasValue)
            {
                return TimeSpan.Zero;
            }

            TimeSpan remaining = NextPhaseAt.Value - ElapsedAt(worldNow);

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        ///     True between the warning and the phase it warns about, including through a pause, and
        ///     false once the phase is due even before its broadcast arrives.
        /// </summary>
        public bool IsWarningActiveAt(TimeSpan worldNow)
        {
            if (!WarningAt.HasValue || !NextPhaseAt.HasValue)
            {
                return false;
            }

            TimeSpan elapsed = ElapsedAt(worldNow);

            return elapsed >= WarningAt.Value && elapsed < NextPhaseAt.Value;
        }

        /// <summary>Where the match is, in one line, the same sentence on the server and on a client.</summary>
        public string StatusAt(TimeSpan worldNow)
        {
            ZoneState zone = ZoneAt(worldNow);
            bool closed = zone.Sphere.Radius <= 0;

            string state = closed ? "closed" : IsCollapsing ? "closing to nothing" : Phase(State);
            string progress = IsCollapsing ? "final collapse" : $"phase {CompletedPhases}/{TotalPhases}";
            string next = closed
                ? "reset to play the group again"
                : IsCollapsing
                    ? "nothing left to close"
                    : $"next in {MatchTime.Countdown(UntilNextPhaseAt(worldNow))}";
            string warning = IsWarningActiveAt(worldNow) ? ", phase warning up" : string.Empty;

            return $"group {Group}: {state} at {MatchTime.Countdown(ElapsedAt(worldNow))}, " +
                   $"{progress}, {next}{warning}, zone {zone.Sphere}";
        }

        /// <summary>
        ///     The broadcast as one line of text: a version header, then tab-separated <c>key=value</c>
        ///     pairs. Text because Core cannot reference Nitrox packet types.
        /// </summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(240);

            line.Append(WireHeader);
            Pair(line, "anchor", Anchor.ToKey());
            Pair(line, "phase", Number(Shrink.PhaseNumber));
            Pair(line, "phases", Number(TotalPhases));
            Pair(line, "started", Milliseconds(PhaseStartedAt));

            if (NextPhaseAt.HasValue)
            {
                Pair(line, "next", Milliseconds(NextPhaseAt.Value));
            }

            if (WarningAt.HasValue)
            {
                Pair(line, "warn", Milliseconds(WarningAt.Value));
            }

            Pair(line, "from", Sphere(Shrink.From));
            Pair(line, "to", Sphere(Shrink.Target));
            Pair(line, "shrink", Milliseconds(Shrink.ShrinkFor));
            Pair(line, "damage", Number(Shrink.DamagePerTick));

            if (Shrink.NextTarget.HasValue)
            {
                Pair(line, "then", Sphere(Shrink.NextTarget.Value));
            }

            // Optional fields, written only when set; older clients skip unknown keys.
            if (Alive.HasValue)
            {
                Pair(line, AliveField, Number(Alive.Value));
            }

            if (!string.IsNullOrEmpty(Destination))
            {
                Pair(line, DestinationField, Clean(Destination!));
            }

            return line.ToString();
        }

        /// <summary>
        ///     A copy of this broadcast carrying the alive count and destination, which only
        ///     <c>MatchHost</c> knows.
        /// </summary>
        public MatchBroadcast Saying(int? alive, string? destination) =>
            new MatchBroadcast(Anchor, TotalPhases, PhaseStartedAt, NextPhaseAt, WarningAt, Shrink,
                alive, destination);

        /// <summary>Whether a line is one of ours, without reading it.</summary>
        public static bool IsBroadcast(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads a broadcast back, declining it when anything required is missing so a client shows
        ///     no match rather than inventing one.
        /// </summary>
        public static bool TryDecode(string? line, out MatchBroadcast broadcast)
        {
            broadcast = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            MatchAnchor? anchor = null;
            int? phaseNumber = null;
            int? totalPhases = null;
            TimeSpan? phaseStartedAt = null;
            TimeSpan? nextPhaseAt = null;
            TimeSpan? warningAt = null;
            ZoneSphere? from = null;
            ZoneSphere? target = null;
            TimeSpan? shrinkFor = null;
            double? damagePerTick = null;
            ZoneSphere? nextTarget = null;
            int? alive = null;
            string? destination = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                string key = token.Substring(0, split);
                string value = token.Substring(split + 1);

                switch (key)
                {
                    case "anchor":
                        if (!MatchAnchor.TryParse(value, out MatchAnchor parsedAnchor))
                        {
                            return false;
                        }

                        anchor = parsedAnchor;
                        break;

                    case "phase":
                        phaseNumber = ReadInt(value);
                        break;

                    case "phases":
                        totalPhases = ReadInt(value);
                        break;

                    case "started":
                        phaseStartedAt = ReadTime(value);
                        break;

                    case "next":
                        nextPhaseAt = ReadTime(value);
                        break;

                    case "warn":
                        warningAt = ReadTime(value);
                        break;

                    case "from":
                        from = ReadSphere(value);
                        break;

                    case "to":
                        target = ReadSphere(value);
                        break;

                    case "shrink":
                        shrinkFor = ReadTime(value);
                        break;

                    case "damage":
                        damagePerTick = ReadNumber(value);
                        break;

                    case "then":
                        nextTarget = ReadSphere(value);
                        break;

                    case AliveField:
                        alive = ReadInt(value);
                        break;

                    case DestinationField:
                        destination = value.Length == 0 ? null : value;
                        break;

                    default:
                        // A key from a newer server; ignored so an older client still follows the match.
                        break;
                }
            }

            if (!anchor.HasValue || !phaseNumber.HasValue || !totalPhases.HasValue
                || !phaseStartedAt.HasValue || !from.HasValue || !target.HasValue
                || !shrinkFor.HasValue || !damagePerTick.HasValue
                || phaseNumber.Value < 0 || totalPhases.Value < 1
                || shrinkFor.Value < TimeSpan.Zero || !(damagePerTick.Value > 0))
            {
                return false;
            }

            broadcast = new MatchBroadcast(anchor.Value, totalPhases.Value,
                phaseStartedAt.Value, nextPhaseAt, warningAt,
                new ZoneShrink(from.Value, target.Value, shrinkFor.Value, phaseNumber.Value,
                    damagePerTick.Value, nextTarget),
                alive, destination);

            return true;
        }

        public bool Equals(MatchBroadcast other) =>
            Anchor.Equals(other.Anchor)
            && TotalPhases == other.TotalPhases
            && PhaseStartedAt == other.PhaseStartedAt
            && Nullable.Equals(NextPhaseAt, other.NextPhaseAt)
            && Nullable.Equals(WarningAt, other.WarningAt)
            && Shrink.Equals(other.Shrink)
            && Nullable.Equals(Alive, other.Alive)
            && string.Equals(Destination, other.Destination, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is MatchBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Anchor.GetHashCode();
                hash = hash * 397 ^ TotalPhases;
                hash = hash * 397 ^ PhaseStartedAt.GetHashCode();
                return hash * 397 ^ Shrink.GetHashCode();
            }
        }

        public override string ToString() => $"{Anchor} phase {CompletedPhases}/{TotalPhases}, {Shrink}";

        private static void Pair(StringBuilder line, string key, string value) =>
            line.Append('\t').Append(key).Append('=').Append(value);

        /// <summary>Round-trippable (G17) so every client draws the wall exactly where the damage starts.</summary>
        private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Strips separators from an operator-typed name so it cannot corrupt the line.</summary>
        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        private static string Milliseconds(TimeSpan span) =>
            ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        private static string Sphere(ZoneSphere sphere) =>
            Number(sphere.Centre.X) + "," + Number(sphere.Centre.Y) + "," +
            Number(sphere.Centre.Z) + "," + Number(sphere.Radius);

        private static int? ReadInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : (int?)null;

        /// <summary>
        ///     Declines NaN and infinity here, since <see cref="ZoneSphere"/> would throw on them inside
        ///     the decoder.
        /// </summary>
        private static double? ReadNumber(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && !double.IsNaN(parsed)
            && !double.IsInfinity(parsed)
                ? parsed
                : (double?)null;

        private static TimeSpan? ReadTime(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds)
                ? TimeSpan.FromMilliseconds(milliseconds)
                : (TimeSpan?)null;

        private static ZoneSphere? ReadSphere(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length != 4)
            {
                return null;
            }

            double? x = ReadNumber(parts[0]);
            double? y = ReadNumber(parts[1]);
            double? z = ReadNumber(parts[2]);
            double? radius = ReadNumber(parts[3]);

            if (!x.HasValue || !y.HasValue || !z.HasValue || !radius.HasValue || radius.Value < 0)
            {
                return null;
            }

            return new ZoneSphere(new WorldPoint(x.Value, y.Value, z.Value), radius.Value);
        }

        private static string Phase(MatchClockState state)
        {
            switch (state)
            {
                case MatchClockState.Running:
                    return "running";

                case MatchClockState.Paused:
                    return "paused";

                default:
                    return "lobby";
            }
        }

    }
}
