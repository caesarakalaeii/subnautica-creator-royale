namespace Lms.Core.Biomes
{
    /// <summary>Why a biome name did not resolve. Never a silent condition — see <see cref="BiomeResolution"/>.</summary>
    public enum BiomeResolutionFailure
    {
        /// <summary>Nothing failed; the name resolved.</summary>
        None = 0,

        /// <summary>
        /// <c>GetBiome</c> returned null: the world is not mounted, or the block has no biome
        /// properties. Null is not a biome and must not be resolved to one.
        /// </summary>
        NoBiomeReported,

        /// <summary>The name was empty or whitespace, which is a bug rather than a place.</summary>
        BlankName,

        /// <summary>
        /// A real string that matches no known name, not even by prefix; most likely an unseen
        /// per-batch override. Log the raw value.
        /// </summary>
        UnknownName,
    }
}
