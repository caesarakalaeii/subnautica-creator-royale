using System;

namespace Lms.Core.Biomes
{
    /// <summary>One atmosphere prefab and the biome string it gives every batch that points at it.</summary>
    /// <remarks>
    ///     <c>LargeWorldBatchRoot.Start</c> takes <c>overrideBiome</c> from the prefab's
    ///     <c>AtmosphereVolume</c>, replacing the batch file's value. It lives in an addressable
    ///     bundle, so it can only be read from the running game.
    /// </remarks>
    public sealed class AtmospherePrefabBiome
    {
        private AtmospherePrefabBiome(string classId, string prefabPath, string? overrideBiome,
            AtmospherePrefabOutcome outcome, BiomeResolution? resolution)
        {
            ClassId = classId ?? throw new ArgumentNullException(nameof(classId));
            PrefabPath = prefabPath ?? throw new ArgumentNullException(nameof(prefabPath));
            OverrideBiome = overrideBiome;
            Outcome = outcome;
            Resolution = resolution;
        }

        /// <summary>The prefab's class id, as <c>atmospherePrefabClassId</c> on the batch root spells it.</summary>
        public string ClassId { get; }

        /// <summary>The prefab's path according to <c>prefabs.db</c>.</summary>
        public string PrefabPath { get; }

        /// <summary>The string the volume carries, exactly as it came back. Null when there was none.</summary>
        public string? OverrideBiome { get; }

        public AtmospherePrefabOutcome Outcome { get; }

        /// <summary>What the resolver made of the string, or null when there was no string to resolve.</summary>
        public BiomeResolution? Resolution { get; }

        /// <summary>Reads what the volume on a loaded prefab says.</summary>
        public static AtmospherePrefabBiome From(string classId, string prefabPath, string? overrideBiome)
        {
            if (string.IsNullOrWhiteSpace(overrideBiome))
            {
                // Unlike "no volume", the empty value still overwrites the batch's own string.
                return new AtmospherePrefabBiome(classId, prefabPath, overrideBiome, AtmospherePrefabOutcome.NoBiome, null);
            }

            BiomeResolution resolution = BiomeResolver.Resolve(overrideBiome);
            return new AtmospherePrefabBiome(classId, prefabPath, overrideBiome,
                resolution.IsResolved ? AtmospherePrefabOutcome.Resolved : AtmospherePrefabOutcome.Unresolved,
                resolution);
        }

        /// <summary>The prefab loaded but carries no <c>AtmosphereVolume</c>.</summary>
        public static AtmospherePrefabBiome WithoutVolume(string classId, string prefabPath) =>
            new AtmospherePrefabBiome(classId, prefabPath, null, AtmospherePrefabOutcome.NoVolume, null);

        /// <summary>The prefab did not load.</summary>
        public static AtmospherePrefabBiome Missing(string classId, string prefabPath) =>
            new AtmospherePrefabBiome(classId, prefabPath, null, AtmospherePrefabOutcome.PrefabMissing, null);

        /// <summary>One tab-separated record, as the adapter writes it to its file.</summary>
        public override string ToString()
        {
            string outcome = Outcome == AtmospherePrefabOutcome.Unresolved
                ? Resolution!.Failure.ToString()
                : Outcome.ToString();

            return $"{PrefabPath}\t{ClassId}\t{OverrideBiome ?? "-"}\t" +
                   $"{Resolution?.Biome?.Identifier ?? "-"}\t{Resolution?.Kind?.ToString() ?? "-"}\t{outcome}";
        }
    }
}
