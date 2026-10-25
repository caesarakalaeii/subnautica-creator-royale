using System;

namespace Lms.Core.Biomes
{
    /// <summary>
    /// Turns what <c>LargeWorld.GetBiome()</c> returns into a tournament biome, or an explicit
    /// failure; never a guess.
    /// </summary>
    /// <remarks>
    /// <c>GetBiome</c> returns a per-batch <c>overrideBiome</c> string, a name from
    /// <c>biomes.csv</c>, or null (docs/data/README.md). Overrides carry suffixes, hence the
    /// prefix matching.
    /// </remarks>
    public static class BiomeResolver
    {
        /// <summary>Resolves a raw game biome name. Never throws; the failure is in the result.</summary>
        public static BiomeResolution Resolve(string? rawBiome)
        {
            if (rawBiome is null)
            {
                return BiomeResolution.Unresolved(null, BiomeResolutionFailure.NoBiomeReported);
            }

            if (string.IsNullOrWhiteSpace(rawBiome))
            {
                return BiomeResolution.Unresolved(rawBiome, BiomeResolutionFailure.BlankName);
            }

            string name = rawBiome.Trim();

            if (KnownBiomes.TryFind(name, out KnownBiomes.Entry exact))
            {
                return BiomeResolution.Resolved(exact.Biome, exact.Kind, rawBiome);
            }

            // Override families first: some start with a table name, and BloodKelp_Trench must
            // reach bloodKelpTrench rather than bloodKelp.
            foreach (KnownBiomes.Family family in KnownBiomes.FamiliesLongestPrefixFirst)
            {
                if (name.StartsWith(family.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return BiomeResolution.Resolved(family.Target.Biome, family.Target.Kind, rawBiome);
                }
            }

            // Then an override that decorates a known name, such as grassyPlateaus_Grass. Longest
            // name first, since bloodKelp is a prefix of bloodKelpTwo.
            foreach (KnownBiomes.Entry candidate in KnownBiomes.LongestNameFirst)
            {
                if (name.StartsWith(candidate.Biome.Identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return BiomeResolution.Resolved(candidate.Biome, candidate.Kind, rawBiome);
                }
            }

            return BiomeResolution.Unresolved(rawBiome, BiomeResolutionFailure.UnknownName);
        }
    }
}
