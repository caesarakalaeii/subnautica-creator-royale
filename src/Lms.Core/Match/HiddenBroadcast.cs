using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Lms.Core.Match
{
    /// <summary>Who no client may draw. Empty is the ordinary case.</summary>
    /// <remarks>
    ///     Names players because each observer's client hides remote bodies itself. The whole list is
    ///     sent every time, never a delta, so a client that missed one is corrected by the next.
    /// </remarks>
    public readonly struct HiddenBroadcast : IEquatable<HiddenBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-hidden/1";

        private const string NameField = "name";

        private static readonly ReadOnlyCollection<string> Nobody =
            new ReadOnlyCollection<string>(new string[0]);

        private readonly ReadOnlyCollection<string>? names;

        public HiddenBroadcast(IEnumerable<string>? names)
        {
            if (names == null)
            {
                this.names = Nobody;
                return;
            }

            List<string> kept = new List<string>();
            foreach (string name in names)
            {
                // A blank name matches nobody and would read like a truncated field.
                if (!string.IsNullOrWhiteSpace(name))
                {
                    kept.Add(name.Trim());
                }
            }

            this.names = new ReadOnlyCollection<string>(kept);
        }

        /// <summary>Whose body every client is to leave undrawn. Empty is the ordinary case.</summary>
        public IReadOnlyList<string> Names => names ?? Nobody;

        /// <summary>The list as one line of text, one <c>name=</c> field per player.</summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(64);
            line.Append(WireHeader);

            foreach (string name in Names)
            {
                line.Append('\t').Append(NameField).Append('=').Append(name);
            }

            return line.ToString();
        }

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsHidden(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads the list back. Unknown fields are ignored; a line with no names decodes to an
        ///     empty list, meaning nobody is hidden.
        /// </summary>
        public static bool TryDecode(string? line, out HiddenBroadcast hidden)
        {
            hidden = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            List<string> named = new List<string>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                if (!string.Equals(token.Substring(0, split), NameField, StringComparison.Ordinal))
                {
                    continue;
                }

                string name = token.Substring(split + 1);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    named.Add(name);
                }
            }

            hidden = new HiddenBroadcast(named);

            return true;
        }

        public bool Equals(HiddenBroadcast other)
        {
            IReadOnlyList<string> mine = Names;
            IReadOnlyList<string> theirs = other.Names;

            if (mine.Count != theirs.Count)
            {
                return false;
            }

            for (int i = 0; i < mine.Count; i++)
            {
                if (!string.Equals(mine[i], theirs[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is HiddenBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Names.Count;

                foreach (string name in Names)
                {
                    hash = hash * 397 ^ name.GetHashCode();
                }

                return hash;
            }
        }

        public override string ToString() =>
            Names.Count == 0 ? "nobody is hidden" : $"hidden: {string.Join(", ", Names)}";
    }
}
