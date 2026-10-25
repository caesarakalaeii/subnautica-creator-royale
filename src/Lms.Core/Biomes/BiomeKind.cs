namespace Lms.Core.Biomes
{
    /// <summary>
    /// What a known biome name means to the tournament. Three of the game's nineteen names are not
    /// cullable places; see docs/data/README.md.
    /// </summary>
    public enum BiomeKind
    {
        /// <summary>Part of the Subnautica 1 map, and so eligible for the cull sequence.</summary>
        Playable,

        /// <summary><c>void</c>: off the edge of the map. Resolves, but is never drawn.</summary>
        OutsideMap,

        /// <summary><c>unassigned</c>: the biome map's own null. Never a cullable biome.</summary>
        Unmapped,

        /// <summary><c>arctic</c>: Below Zero's, present in the table but not on this map.</summary>
        NotInThisGame,
    }
}
