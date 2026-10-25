namespace Lms.Core.Biomes
{
    /// <summary>What an atmosphere prefab turned out to say about the biome of the batches using it.</summary>
    public enum AtmospherePrefabOutcome
    {
        /// <summary>The prefab did not load, so nothing can be concluded.</summary>
        PrefabMissing,

        /// <summary>
        ///     No <c>AtmosphereVolume</c> on the prefab, so <c>PullSettingsFromPrefab</c> returns
        ///     early and the batch keeps whatever string its own file carried.
        /// </summary>
        NoVolume,

        /// <summary>
        ///     A volume with no biome of its own. The batch's own string is still wiped, so it falls
        ///     back to the 2D <c>biomeMap.bin</c>, which names the surface biome for deep areas.
        /// </summary>
        NoBiome,

        /// <summary>A biome string the resolver can name.</summary>
        Resolved,

        /// <summary>A biome string nothing can name.</summary>
        Unresolved,
    }
}
