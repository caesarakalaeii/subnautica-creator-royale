using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Biomes
{
    /// <summary>Collects the distinct biome strings a session has seen, in first-seen order.</summary>
    /// <remarks>
    ///     Per-batch override strings cannot be enumerated offline (docs/data/README.md), so the full
    ///     set is learned by exploring the map.
    /// </remarks>
    public sealed class BiomeSightingLog
    {
        // Ordinal, unlike biome identity: `Dunes` and `dunes` are two different authored strings.
        private readonly Dictionary<string, BiomeSighting> byRawValue = new Dictionary<string, BiomeSighting>(StringComparer.Ordinal);
        private readonly List<BiomeSighting> firstSeenOrder = new List<BiomeSighting>();

        /// <summary>Every distinct value seen, in the order it was first seen.</summary>
        public IReadOnlyList<BiomeSighting> Sightings => new ReadOnlyCollection<BiomeSighting>(firstSeenOrder);

        /// <summary>How many distinct values did not resolve.</summary>
        public int UnresolvedCount { get; private set; }

        /// <summary>Records a sample.</summary>
        /// <returns>
        ///     The sighting the first time a raw value is seen, otherwise null. A null raw value
        ///     (e.g. a loading screen) is ignored.
        /// </returns>
        public BiomeSighting? Record(BiomeResolution resolution)
        {
            if (resolution.RawValue is null)
            {
                return null;
            }

            if (byRawValue.TryGetValue(resolution.RawValue, out BiomeSighting existing))
            {
                existing.SeenAgain();
                return null;
            }

            BiomeSighting sighting = new BiomeSighting(resolution.RawValue, resolution.Biome, resolution.Kind, resolution.Failure);
            byRawValue.Add(sighting.RawValue, sighting);
            firstSeenOrder.Add(sighting);

            if (!sighting.IsResolved)
            {
                UnresolvedCount++;
            }

            return sighting;
        }
    }
}
