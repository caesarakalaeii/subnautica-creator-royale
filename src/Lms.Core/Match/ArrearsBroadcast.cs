using System;
using System.Globalization;
using System.Text;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>What the server tells one returning client it owes the wall.</summary>
    /// <remarks>
    ///     The server computes the bill; the client applies the damage locally. Sent point-to-point
    ///     and names no player; group and generation fence off bills from a reset match.
    /// </remarks>
    public readonly struct ArrearsBroadcast : IEquatable<ArrearsBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-arrears/1";

        /// <param name="group">Which of the matches this is about.</param>
        /// <param name="generation">Which attempt at it, which is the fence a stale bill fails.</param>
        /// <param name="debt">What the absence cost.</param>
        public ArrearsBroadcast(int group, int generation, ZoneDebt debt)
            : this(group, generation, debt.Damage, debt.Ticks, debt.Absence, debt.IsFatal)
        {
        }

        private ArrearsBroadcast(int group, int generation, double damage, int ticks,
            TimeSpan absence, bool isFatal)
        {
            Group = group;
            Generation = generation;
            Damage = damage;
            Ticks = ticks;
            Absence = absence;
            IsFatal = isFatal;
        }

        public int Group { get; }

        public int Generation { get; }

        /// <summary>Health the wall would have taken, priced at the phase each tick fell due in.</summary>
        public double Damage { get; }

        /// <summary>How many bites it is made of, which is what the client says on screen.</summary>
        public int Ticks { get; }

        /// <summary>How long they were gone, in match time.</summary>
        public TimeSpan Absence { get; }

        /// <summary>
        ///     Whether the wall would have finished them while they were away. Advisory, for wording
        ///     only: the client's own health decides whether they die.
        /// </summary>
        public bool IsFatal { get; }

        /// <summary>The bill as one line of text, the same shape the zone's line uses.</summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(80);

            line.Append(WireHeader);
            Pair(line, "group", Group.ToString(CultureInfo.InvariantCulture));
            Pair(line, "gen", Generation.ToString(CultureInfo.InvariantCulture));
            Pair(line, "owed", Damage.ToString("G17", CultureInfo.InvariantCulture));
            Pair(line, "ticks", Ticks.ToString(CultureInfo.InvariantCulture));
            Pair(line, "away", ((long)Absence.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

            // Written only when true, so an older reader still sees a bill it can apply.
            if (IsFatal)
            {
                Pair(line, "fatal", "1");
            }

            return line.ToString();
        }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsArrears(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads a bill back, declining it when anything required is missing or the bill is for
        ///     nothing, so a client never takes health off on a guess.
        /// </summary>
        public static bool TryDecode(string? line, out ArrearsBroadcast arrears)
        {
            arrears = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            int? group = null;
            int? generation = null;
            double? damage = null;
            int? ticks = null;
            TimeSpan? absence = null;
            bool fatal = false;

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
                    case "group":
                        group = ReadInt(value);
                        break;

                    case "gen":
                        generation = ReadInt(value);
                        break;

                    case "owed":
                        damage = ReadNumber(value);
                        break;

                    case "ticks":
                        ticks = ReadInt(value);
                        break;

                    case "away":
                        absence = ReadTime(value);
                        break;

                    case "fatal":
                        fatal = value == "1";
                        break;

                    default:
                        // A key from a newer server; ignored so an older client still pays what it understands.
                        break;
                }
            }

            if (!group.HasValue || !generation.HasValue || !damage.HasValue || !ticks.HasValue
                || !absence.HasValue || group.Value < 1 || generation.Value < 1
                || !(damage.Value > 0) || ticks.Value < 1 || absence.Value < TimeSpan.Zero)
            {
                return false;
            }

            arrears = new ArrearsBroadcast(group.Value, generation.Value, damage.Value, ticks.Value,
                absence.Value, fatal);

            return true;
        }

        public bool Equals(ArrearsBroadcast other) =>
            Group == other.Group
            && Generation == other.Generation
            && Damage.Equals(other.Damage)
            && Ticks == other.Ticks
            && Absence == other.Absence
            && IsFatal == other.IsFatal;

        public override bool Equals(object? obj) => obj is ArrearsBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Group;
                hash = hash * 397 ^ Generation;
                hash = hash * 397 ^ Damage.GetHashCode();
                hash = hash * 397 ^ Ticks;
                return hash * 397 ^ Absence.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture,
                "group {0}.{1} owes {2:0.#} over {3} ticks, away {4:0}s{5}",
                Group, Generation, Damage, Ticks, Absence.TotalSeconds,
                IsFatal ? ", which would have got them" : string.Empty);

        private static void Pair(StringBuilder line, string key, string value) =>
            line.Append('\t').Append(key).Append('=').Append(value);

        private static int? ReadInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : (int?)null;

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
    }
}
