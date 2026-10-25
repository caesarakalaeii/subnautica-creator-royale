using System;
using System.Globalization;

namespace Lms.Core.Audit
{
    /// <summary>Which match a record belongs to and how far into it: group, generation and match time.</summary>
    /// <remarks>
    ///     Records with the same group but different generations are separate attempts; only the
    ///     last counts towards the standings.
    /// </remarks>
    public readonly struct MatchStamp : IEquatable<MatchStamp>
    {
        public MatchStamp(int group, int generation, TimeSpan elapsed)
        {
            if (group < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(group), group, "Groups count from one.");
            }

            if (generation < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(generation), generation,
                    "Generations count from one.");
            }

            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed,
                    "Match time does not run backwards.");
            }

            Group = group;
            Generation = generation;
            Elapsed = elapsed;
        }

        /// <summary>Which group's match this is, counting from one.</summary>
        public int Group { get; }

        /// <summary>Which attempt at that group, counting from one. A reset increments it.</summary>
        public int Generation { get; }

        /// <summary>Match time, excluding paused time.</summary>
        public TimeSpan Elapsed { get; }

        public bool Equals(MatchStamp other) =>
            Group == other.Group && Generation == other.Generation && Elapsed == other.Elapsed;

        public override bool Equals(object? obj) => obj is MatchStamp other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Group;
                hash = hash * 397 ^ Generation;
                return hash * 397 ^ Elapsed.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "group {0}.{1} at {2:hh\\:mm\\:ss}",
                Group, Generation, Elapsed);
    }
}
