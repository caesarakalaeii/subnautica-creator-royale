using System;
using System.Collections.Generic;
using Lms.Core.Telemetry;

namespace Lms.Core.Match
{
    /// <summary>Who is present but not in the match: casters and crew, known to the server as ghosts.</summary>
    /// <remarks>
    ///     Seeded from <c>ghosts:</c> in the config and changed by <c>lms ghost</c>/<c>lms unghost</c>;
    ///     each client learns its role via <see cref="GhostBroadcast"/>. Names match case-insensitively.
    ///     Not thread-safe: only touched on the server thread through <see cref="MatchHost"/>.
    /// </remarks>
    public sealed class GhostRoster
    {
        private static readonly PlayerReading[] Nobody = new PlayerReading[0];

        private readonly HashSet<string> named;

        /// <param name="named">The crew, from <c>ghosts:</c>. Null or empty means nobody.</param>
        public GhostRoster(IEnumerable<string>? named = null)
        {
            this.named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (named == null)
            {
                return;
            }

            foreach (string name in named)
            {
                Add(name);
            }
        }

        /// <summary>How many people the match cannot see.</summary>
        public int Count => named.Count;

        /// <summary>Every name, sorted ordinally so the order is the same on every machine.</summary>
        public IReadOnlyList<string> Named
        {
            get
            {
                List<string> all = new List<string>(named);
                all.Sort(StringComparer.Ordinal);

                return all;
            }
        }

        /// <summary>Whether the match is meant to be unable to see this player. A blank name never is.</summary>
        public bool IsGhost(string? name) =>
            !string.IsNullOrWhiteSpace(name) && named.Contains(name!.Trim());

        /// <summary>Makes somebody a ghost. False if they already were, or if there is no name.</summary>
        public bool Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return named.Add(name!.Trim());
        }

        /// <summary>Puts somebody back in the match. False if they were never out of it.</summary>
        public bool Remove(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return named.Remove(name!.Trim());
        }

        /// <summary>
        ///     Who is in the match: everybody connected, minus the ghosts. The roster, pod layout, alive
        ///     count and standings all filter through this. Returns the same list when there are no ghosts.
        /// </summary>
        public IReadOnlyList<PlayerReading> Contestants(IReadOnlyList<PlayerReading> readings)
        {
            if (readings == null)
            {
                return Nobody;
            }

            if (named.Count == 0)
            {
                return readings;
            }

            List<PlayerReading> contestants = new List<PlayerReading>(readings.Count);
            foreach (PlayerReading reading in readings)
            {
                if (!IsGhost(reading.Name))
                {
                    contestants.Add(reading);
                }
            }

            return contestants;
        }

        /// <summary>Who is connected and out of the match, so each can be sent its ghost flag.</summary>
        public IReadOnlyList<PlayerReading> Ghosts(IReadOnlyList<PlayerReading> readings)
        {
            if (readings == null || named.Count == 0)
            {
                return Nobody;
            }

            List<PlayerReading> ghosts = new List<PlayerReading>();
            foreach (PlayerReading reading in readings)
            {
                if (IsGhost(reading.Name))
                {
                    ghosts.Add(reading);
                }
            }

            return ghosts;
        }

        public override string ToString() =>
            named.Count == 0 ? "no ghosts" : $"{named.Count} ghost(s): {string.Join(", ", Named)}";
    }
}
