using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Lms.Core.Match
{
    /// <summary>Where every contestant is and how they are doing, sent to the people filming them.</summary>
    /// <remarks>
    ///     Sent only to ghosts: handed to a contestant it would be a wallhack. The whole list travels
    ///     every server tick, never a delta; <see cref="CasterTrack"/> smooths between ticks.
    /// </remarks>
    public readonly struct CasterFeed : IEquatable<CasterFeed>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-caster/1";

        /// <summary>One of these fields per contestant.</summary>
        private const string PlayerField = "p";

        private static readonly ReadOnlyCollection<CasterSighting> Nobody =
            new ReadOnlyCollection<CasterSighting>(new CasterSighting[0]);

        private readonly ReadOnlyCollection<CasterSighting>? seen;

        public CasterFeed(IEnumerable<CasterSighting>? seen)
        {
            if (seen == null)
            {
                this.seen = Nobody;
                return;
            }

            List<CasterSighting> kept = new List<CasterSighting>();
            foreach (CasterSighting one in seen)
            {
                // A nameless row matches nobody and would read like a truncated one.
                if (one.IsSomebody)
                {
                    kept.Add(one);
                }
            }

            this.seen = new ReadOnlyCollection<CasterSighting>(kept);
        }

        /// <summary>
        ///     Every contestant the server can see. Empty is a real message: it clears the last
        ///     group's plates between groups.
        /// </summary>
        public IReadOnlyList<CasterSighting> Seen => seen ?? Nobody;

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsFeed(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        public string Encode()
        {
            StringBuilder line = new StringBuilder(128);
            line.Append(WireHeader);

            foreach (CasterSighting one in Seen)
            {
                line.Append('\t').Append(PlayerField).Append('=').Append(one.Encode());
            }

            return line.ToString();
        }

        /// <summary>
        ///     Reads the list back. Unknown fields and rows that will not parse are skipped; see
        ///     <see cref="CasterSighting.TryDecode"/>.
        /// </summary>
        public static bool TryDecode(string? line, out CasterFeed feed)
        {
            feed = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            List<CasterSighting> read = new List<CasterSighting>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int split = token.IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                if (!string.Equals(token.Substring(0, split), PlayerField, StringComparison.Ordinal))
                {
                    continue;
                }

                if (CasterSighting.TryDecode(token.Substring(split + 1), out CasterSighting one))
                {
                    read.Add(one);
                }
            }

            feed = new CasterFeed(read);

            return true;
        }

        public bool Equals(CasterFeed other)
        {
            IReadOnlyList<CasterSighting> mine = Seen;
            IReadOnlyList<CasterSighting> theirs = other.Seen;

            if (mine.Count != theirs.Count)
            {
                return false;
            }

            for (int i = 0; i < mine.Count; i++)
            {
                if (!mine[i].Equals(theirs[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is CasterFeed other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Seen.Count;

                foreach (CasterSighting one in Seen)
                {
                    hash = hash * 397 ^ one.GetHashCode();
                }

                return hash;
            }
        }

        public override string ToString() =>
            Seen.Count == 0 ? "nobody in the match" : $"{Seen.Count} contestant(s) on camera";
    }
}
