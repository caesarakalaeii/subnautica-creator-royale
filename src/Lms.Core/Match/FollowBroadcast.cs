using System;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Who a caster's camera is staying on, or that it is letting go, addressed to one ghost or
    ///     to all of them.
    /// </summary>
    /// <remarks>
    ///     Only a name travels: <see cref="CasterFeed"/> already supplies positions every tick. An
    ///     empty name is <see cref="LetGo"/>.
    /// </remarks>
    public readonly struct FollowBroadcast : IEquatable<FollowBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-follow/2";

        /// <summary>The unaddressed form older builds send; still read, meaning every camera.</summary>
        private const string LegacyWireHeader = "lms-follow/1";

        private const string GhostField = "ghost";
        private const string NameField = "name";

        private readonly string? ghost;

        private readonly string? name;

        public FollowBroadcast(string? name) : this(null, name)
        {
        }

        /// <summary>Builds a follow. An empty <paramref name="ghost"/> addresses every camera.</summary>
        public FollowBroadcast(string? ghost, string? name)
        {
            this.ghost = string.IsNullOrWhiteSpace(ghost) ? string.Empty : ghost!.Trim();
            this.name = string.IsNullOrWhiteSpace(name) ? string.Empty : name!.Trim();
        }

        /// <summary>The message that means "fly it yourself again".</summary>
        public static FollowBroadcast LetGo { get; } = new FollowBroadcast(null);

        /// <summary>The ghost this line is for. Empty means every camera.</summary>
        public string Ghost => ghost ?? string.Empty;

        /// <summary>Who to stay on. Empty when this is a <see cref="LetGo"/>.</summary>
        public string Name => name ?? string.Empty;
        /// <summary>Whether this asks a camera to stay on somebody at all.</summary>
        public bool Following => Name.Length > 0;

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsFollow(string? line) =>
            line != null && (line.StartsWith(WireHeader, StringComparison.Ordinal)
                || line.StartsWith(LegacyWireHeader, StringComparison.Ordinal));

        /// <summary>The follow as one line of text. The ghost field is written only when addressed.</summary>
        public string Encode() =>
            Ghost.Length == 0
                ? WireHeader + "\t" + NameField + "=" + Clean(Name)
                : WireHeader + "\t" + GhostField + "=" + Clean(Ghost) + "\t" + NameField + "=" + Clean(Name);

        /// <summary>
        ///     Reads a follow back, from either header. Unknown fields are ignored; a line with no
        ///     name decodes to <see cref="LetGo"/>.
        /// </summary>
        public static bool TryDecode(string? line, out FollowBroadcast follow)
        {
            follow = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] == LegacyWireHeader)
            {
                follow = new FollowBroadcast(Field(tokens, NameField));

                return true;
            }

            if (tokens[0] != WireHeader)
            {
                return false;
            }

            follow = new FollowBroadcast(Field(tokens, GhostField), Field(tokens, NameField));

            return true;
        }

        private static string Field(string[] tokens, string wanted)
        {
            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                if (string.Equals(token.Substring(0, split), wanted, StringComparison.Ordinal))
                {
                    return token.Substring(split + 1);
                }
            }

            return string.Empty;
        }

        /// <summary>Names compare case-insensitively, like roster names elsewhere.</summary>
        public bool Equals(FollowBroadcast other) =>
            string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Ghost, other.Ghost, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is FollowBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            // netstandard2.0 has no HashCode.Combine.
            unchecked
            {
                return (StringComparer.OrdinalIgnoreCase.GetHashCode(Name) * 397)
                    ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Ghost);
            }
        }

        public override string ToString() =>
            Ghost.Length == 0
                ? Following ? $"stay on {Name}" : "let go"
                : $"for {Ghost}: {(Following ? $"stay on {Name}" : "let go")}";

        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}
