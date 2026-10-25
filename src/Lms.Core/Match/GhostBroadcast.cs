using System;
using System.Globalization;

namespace Lms.Core.Match
{
    /// <summary>What the server tells one client about its own role in the match.</summary>
    /// <remarks>
    ///     The server is the authority: a ghost is immune to the wall and drowning, so a client must
    ///     never decide it is one. Sent point-to-point and names no player.
    /// </remarks>
    public readonly struct GhostBroadcast : IEquatable<GhostBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-ghost/1";

        private const string GhostField = "ghost";

        public GhostBroadcast(bool isGhost)
        {
            IsGhost = isGhost;
        }

        /// <summary>Whether the match is unable to see the client this was sent to.</summary>
        public bool IsGhost { get; }

        /// <summary>
        ///     The flag as one line of text. The field is always written so its absence can only mean
        ///     a malformed line.
        /// </summary>
        public string Encode() =>
            WireHeader + "\t" + GhostField + "=" +
            (IsGhost ? 1 : 0).ToString(CultureInfo.InvariantCulture);

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsGhostFlag(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads the flag back, or declines it. A caller that cannot read it must assume
        ///     "contestant", never "ghost".
        /// </summary>
        public static bool TryDecode(string? line, out GhostBroadcast role)
        {
            role = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            bool? isGhost = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                // Unknown fields are ignored (unknown headers are refused) so mixed mod builds interoperate.
                if (!string.Equals(token.Substring(0, split), GhostField, StringComparison.Ordinal))
                {
                    continue;
                }

                string value = token.Substring(split + 1);
                if (value == "1")
                {
                    isGhost = true;
                }
                else if (value == "0")
                {
                    isGhost = false;
                }
                else
                {
                    // An unknown value is a build mismatch; decline so the caller keeps its current state.
                    return false;
                }
            }

            if (isGhost == null)
            {
                return false;
            }

            role = new GhostBroadcast(isGhost.Value);

            return true;
        }

        public bool Equals(GhostBroadcast other) => IsGhost == other.IsGhost;

        public override bool Equals(object? obj) => obj is GhostBroadcast other && Equals(other);

        public override int GetHashCode() => IsGhost ? 1 : 0;

        public override string ToString() =>
            IsGhost ? "a ghost: the match cannot see you" : "in the match";
    }
}
