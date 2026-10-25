using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Biomes
{
    /// <summary>Tallies one pass over the atmosphere prefabs and summarises it.</summary>
    /// <remarks>
    ///     The unresolved values matter most: a place the cull sequence has no name for is never culled.
    /// </remarks>
    public sealed class AtmospherePrefabSurvey
    {
        private readonly List<AtmospherePrefabBiome> records = new List<AtmospherePrefabBiome>();

        // Ordinal: two spellings of an authored string are two strings.
        private readonly List<string> unresolved = new List<string>();
        private readonly HashSet<string> unresolvedSeen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Every prefab read, in the order it was read.</summary>
        public IReadOnlyList<AtmospherePrefabBiome> Records => new ReadOnlyCollection<AtmospherePrefabBiome>(records);

        /// <summary>The distinct biome strings nothing could name.</summary>
        public IReadOnlyList<string> UnresolvedValues => new ReadOnlyCollection<string>(unresolved);

        /// <summary>How many prefabs wipe a batch's own string without supplying one.</summary>
        public int NoBiomeCount { get; private set; }

        public int ResolvedCount { get; private set; }

        public int NoVolumeCount { get; private set; }

        public int MissingCount { get; private set; }

        public void Record(AtmospherePrefabBiome record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            records.Add(record);

            switch (record.Outcome)
            {
                case AtmospherePrefabOutcome.Resolved:
                    ResolvedCount++;
                    break;

                case AtmospherePrefabOutcome.Unresolved:
                    if (unresolvedSeen.Add(record.OverrideBiome!))
                    {
                        unresolved.Add(record.OverrideBiome!);
                    }

                    break;

                case AtmospherePrefabOutcome.NoBiome:
                    NoBiomeCount++;
                    break;

                case AtmospherePrefabOutcome.NoVolume:
                    NoVolumeCount++;
                    break;

                default:
                    MissingCount++;
                    break;
            }
        }

        /// <summary>One line for the log and the on-screen message.</summary>
        public string Summary
        {
            get
            {
                string names = unresolved.Count == 0 ? string.Empty : $" ({string.Join(", ", unresolved)})";
                return $"{records.Count} atmosphere prefabs: {ResolvedCount} resolved, " +
                       $"{unresolved.Count} unresolved{names}, {NoBiomeCount} with no biome of their own, " +
                       $"{NoVolumeCount} without a volume, {MissingCount} that would not load";
            }
        }
    }
}
