using System;
using System.Collections.Generic;

namespace Lms.Core.Match
{
    /// <summary>
    ///     Who the bracket says is in the group now playing: the mod's copy of the roster from the
    ///     backend's ingest reply, replaced whole on every reply and never merged.
    /// </summary>
    /// <remarks>
    ///     An empty roster filters nobody, same as having no bracket. Names match case-insensitively.
    ///     Not thread-safe: only touched on the server thread through <see cref="MatchHost"/>.
    /// </remarks>
    public sealed class BracketRoster
    {
        private readonly HashSet<string> members;

        /// <param name="group">Which group of the bracket this is the roster of.</param>
        /// <param name="members">Who is in that group. Null means nobody.</param>
        public BracketRoster(int group, IEnumerable<string>? members = null)
        {
            Group = group;
            this.members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (members == null)
            {
                return;
            }

            foreach (string name in members)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    this.members.Add(name.Trim());
                }
            }
        }

        /// <summary>Which group of the bracket this roster is.</summary>
        public int Group { get; private set; }

        /// <summary>How many people the bracket puts in the playing group.</summary>
        public int Count => members.Count;

        /// <summary>Every name, sorted ordinally so the order is the same on every machine.</summary>
        public IReadOnlyList<string> Members
        {
            get
            {
                List<string> all = new List<string>(members);
                all.Sort(StringComparer.Ordinal);

                return all;
            }
        }

        /// <summary>Replaces the whole roster, group and members together.</summary>
        public void Replace(int group, IEnumerable<string>? members = null)
        {
            Group = group;
            List<string> names = new List<string>();

            if (members != null)
            {
                foreach (string name in members)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name.Trim());
                    }
                }
            }

            this.members.Clear();
            foreach (string name in names)
            {
                this.members.Add(name);
            }
        }

        /// <summary>Whether this person is one the bracket puts in the running group. A blank name never is.</summary>
        public bool IsContesting(string? name) =>
            !string.IsNullOrWhiteSpace(name) && members.Contains(name!.Trim());

        public override string ToString() =>
            members.Count == 0 ? "no bracket" : $"group {Group}: {string.Join(", ", Members)}";
    }
}
