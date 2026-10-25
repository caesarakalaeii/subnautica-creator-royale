using System;

namespace Lms.Core.Biomes
{
    /// <summary>
    /// Which tournament biome a game biome name maps to, or why none does. The raw value is kept
    /// so unresolved names can be logged.
    /// </summary>
    public sealed class BiomeResolution
    {
        private BiomeResolution(TournamentBiome? biome, BiomeKind? kind, string? rawValue, BiomeResolutionFailure failure)
        {
            Biome = biome;
            Kind = kind;
            RawValue = rawValue;
            Failure = failure;
        }

        /// <summary>The biome, spelled as the game's own table spells it. Null when unresolved.</summary>
        public TournamentBiome? Biome { get; }

        /// <summary>What kind of biome it is, or null when nothing resolved.</summary>
        public BiomeKind? Kind { get; }

        /// <summary>Exactly what was handed in, untrimmed, including null. Logged when resolution fails.</summary>
        public string? RawValue { get; }

        /// <summary>Why resolution failed, or <see cref="BiomeResolutionFailure.None"/>.</summary>
        public BiomeResolutionFailure Failure { get; }

        public bool IsResolved => Biome is not null;

        internal static BiomeResolution Resolved(TournamentBiome biome, BiomeKind kind, string rawValue) =>
            new BiomeResolution(biome, kind, rawValue, BiomeResolutionFailure.None);

        internal static BiomeResolution Unresolved(string? rawValue, BiomeResolutionFailure failure)
        {
            if (failure == BiomeResolutionFailure.None)
            {
                throw new ArgumentException("An unresolved biome needs a reason.", nameof(failure));
            }

            return new BiomeResolution(null, null, rawValue, failure);
        }

        public override string ToString() =>
            IsResolved ? Biome!.Identifier : $"unresolved ({Failure}): {RawValue ?? "<null>"}";
    }
}
