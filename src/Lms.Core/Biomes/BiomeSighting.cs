using System;

namespace Lms.Core.Biomes
{
    /// <summary>One distinct biome string the game has reported, and what the resolver made of it.</summary>
    public sealed class BiomeSighting
    {
        internal BiomeSighting(string rawValue, TournamentBiome? biome, BiomeKind? kind, BiomeResolutionFailure failure)
        {
            RawValue = rawValue;
            Biome = biome;
            Kind = kind;
            Failure = failure;
            TimesSeen = 1;
        }

        /// <summary>The string as the game reported it, untrimmed.</summary>
        public string RawValue { get; }

        public TournamentBiome? Biome { get; }

        public BiomeKind? Kind { get; }

        public BiomeResolutionFailure Failure { get; }

        /// <summary>How many samples landed on this value; separates a real biome from a seam.</summary>
        public int TimesSeen { get; private set; }

        public bool IsResolved => Biome is not null;

        /// <summary>
        ///     True when this value is not itself a table name but starts with one: an authored
        ///     per-batch override.
        /// </summary>
        public bool ResolvedByPrefix =>
            Biome is not null && !StringComparer.OrdinalIgnoreCase.Equals(RawValue.Trim(), Biome.Identifier);

        internal void SeenAgain() => TimesSeen++;

        /// <summary>One tab-separated record, as the adapter appends it to its log file.</summary>
        public override string ToString()
        {
            string outcome = IsResolved ? (ResolvedByPrefix ? "prefix" : "exact") : Failure.ToString();
            return $"{RawValue}\t{Biome?.Identifier ?? "-"}\t{Kind?.ToString() ?? "-"}\t{outcome}";
        }
    }
}
