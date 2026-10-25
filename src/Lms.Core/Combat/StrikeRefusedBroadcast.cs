using System;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     What the server tells the attacker when it did not believe a swing.
    /// </summary>
    /// <remarks>
    ///     The game plays a weapon's swing and hit sound unconditionally, so without this a refused
    ///     swing looks like one that landed. The wire carries a word; the client renders
    ///     <see cref="StrikeRefusals.Sentence"/>.
    /// </remarks>
    public readonly struct StrikeRefusedBroadcast : IEquatable<StrikeRefusedBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-refused/1";

        public StrikeRefusedBroadcast(StrikeRefusal why)
        {
            if (why == StrikeRefusal.None)
            {
                throw new ArgumentOutOfRangeException(nameof(why), why,
                    "Nothing was refused, so there is nothing to send. A landed hit is a HurtBroadcast.");
            }

            Why = why;
        }

        /// <summary>Which of the rule's refusals this was.</summary>
        public StrikeRefusal Why { get; }

        /// <summary>What to put on the attacker's screen.</summary>
        public string Sentence => StrikeRefusals.Sentence(Why);

        public string Encode() => WireHeader + "\twhy=" + StrikeRefusals.Word(Why);

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsRefusal(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads a refusal back, or declines it, including any reason this build does not know.
        /// </summary>
        public static bool TryDecode(string? line, out StrikeRefusedBroadcast refusal)
        {
            refusal = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            for (int i = 1; i < tokens.Length; i++)
            {
                int split = tokens[i].IndexOf('=');
                if (split < 1 || tokens[i].Substring(0, split) != "why")
                {
                    continue;
                }

                if (!StrikeRefusals.TryParse(tokens[i].Substring(split + 1), out StrikeRefusal why)
                    || why == StrikeRefusal.None)
                {
                    return false;
                }

                refusal = new StrikeRefusedBroadcast(why);
                return true;
            }

            return false;
        }

        public bool Equals(StrikeRefusedBroadcast other) => Why == other.Why;

        public override bool Equals(object? obj) => obj is StrikeRefusedBroadcast other && Equals(other);

        public override int GetHashCode() => (int)Why;

        public override string ToString() => Sentence;
    }
}
