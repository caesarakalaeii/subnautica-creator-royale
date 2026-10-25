using System;
using System.Globalization;
using System.Text;

namespace Lms.Core.Match
{
    /// <summary>What the server tells one returning client about its own elimination.</summary>
    /// <remarks>
    ///     Stops a reconnect from undoing an elimination: a fresh client would otherwise report itself
    ///     alive. Sent point-to-point and names no player; group and generation fence it to one match
    ///     attempt. There is no "back in" form: a reset moves the generation instead.
    /// </remarks>
    public readonly struct EliminatedBroadcast : IEquatable<EliminatedBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-out/1";

        /// <param name="group">Which of the matches this verdict belongs to.</param>
        /// <param name="generation">Which attempt at it, which is the fence a stale verdict fails.</param>
        public EliminatedBroadcast(int group, int generation)
        {
            Group = group;
            Generation = generation;
        }

        public int Group { get; }

        public int Generation { get; }

        /// <summary>The verdict as one line of text, the same shape the bill uses.</summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(32);

            line.Append(WireHeader);
            Pair(line, "group", Group.ToString(CultureInfo.InvariantCulture));
            Pair(line, "gen", Generation.ToString(CultureInfo.InvariantCulture));

            return line.ToString();
        }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsEliminated(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads a verdict back, or declines it. A caller that cannot read it must assume
        ///     "still in the match".
        /// </summary>
        public static bool TryDecode(string? line, out EliminatedBroadcast verdict)
        {
            verdict = default;

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

                    default:
                        // A key from a newer server; ignored so an older client still reads the fence.
                        break;
                }
            }

            if (!group.HasValue || !generation.HasValue || group.Value < 1 || generation.Value < 1)
            {
                return false;
            }

            verdict = new EliminatedBroadcast(group.Value, generation.Value);

            return true;
        }

        public bool Equals(EliminatedBroadcast other) =>
            Group == other.Group && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is EliminatedBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Group * 397 ^ Generation;
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture,
                "the match is finished with you: group {0}.{1}", Group, Generation);

        private static void Pair(StringBuilder line, string key, string value) =>
            line.Append('\t').Append(key).Append('=').Append(value);

        private static int? ReadInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : (int?)null;
    }
}
