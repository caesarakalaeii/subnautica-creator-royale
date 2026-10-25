using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Biomes
{
    /// <summary>
    /// The biome names the game's map can report, and what each one means to the tournament.
    /// </summary>
    /// <remarks>
    /// The <c>name</c> column of <c>StreamingAssets/SNUnmanagedData/Build18/biomes.csv</c>, compiled
    /// in so Core is testable without a game install. A test checks it against
    /// <c>docs/data/biome-names.txt</c>. Unrelated to the <c>BiomeType</c> enum used for spawning.
    /// </remarks>
    public static class KnownBiomes
    {
        // Kept in the same order as docs/data/biome-names.txt so the two can be read side by side.
        private static readonly (string Name, BiomeKind Kind)[] Table =
        {
            ("arctic", BiomeKind.NotInThisGame),
            ("bloodKelp", BiomeKind.Playable),
            ("bloodKelpTwo", BiomeKind.Playable),
            ("CragField", BiomeKind.Playable),
            ("crashZone", BiomeKind.Playable),
            ("dunes", BiomeKind.Playable),
            ("grandReef", BiomeKind.Playable),
            ("grassyPlateaus", BiomeKind.Playable),
            ("inactiveLavaZone", BiomeKind.Playable),
            ("kelpForest", BiomeKind.Playable),
            ("kooshZone", BiomeKind.Playable),
            ("mountains", BiomeKind.Playable),
            ("mushroomForest", BiomeKind.Playable),
            ("safeShallows", BiomeKind.Playable),
            ("seaTreaderPath", BiomeKind.Playable),
            ("sparseReef", BiomeKind.Playable),
            ("unassigned", BiomeKind.Unmapped),
            ("underwaterIslands", BiomeKind.Playable),
            ("void", BiomeKind.OutsideMap),
        };

        // Override families. Source of truth: docs/data/biome-override-families.tsv.
        // Matched longest-prefix-first and before table names, or BloodKelp_Trench collapses into bloodKelp.
        private static readonly (string Prefix, string Biome)[] FamilyTable =
        {
            ("BloodKelp_Trench", "bloodKelpTrench"),
            ("deepGrandReef", "deepGrandReef"),
            ("ILZ", "inactiveLavaZone"),
            ("ALZ", "activeLavaZone"),
            ("Lava", "activeLavaZone"),
            ("LostRiver_", "lostRiver"),
            ("JellyshroomCaves", "jellyshroomCaves"),
            ("EmperorFacility", "primaryContainmentFacility"),
            ("Prison_", "primaryContainmentFacility"),
            // Supplied by an atmosphere prefab, not the world data.
            ("FloatingIsland", "floatingIsland"),
        };

        private static readonly Dictionary<string, Entry> ByName;
        private static readonly Entry[] LongestNameFirstEntries;
        private static readonly Family[] FamiliesLongestPrefixFirstEntries;
        private static readonly ReadOnlyCollection<TournamentBiome> BiomeTableBiomes;
        private static readonly ReadOnlyCollection<TournamentBiome> AllBiomes;
        private static readonly ReadOnlyCollection<TournamentBiome> PlayableBiomes;

        static KnownBiomes()
        {
            ByName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            List<TournamentBiome> fromTable = new List<TournamentBiome>(Table.Length);
            List<TournamentBiome> all = new List<TournamentBiome>();
            List<TournamentBiome> playable = new List<TournamentBiome>();

            foreach ((string name, BiomeKind kind) in Table)
            {
                Entry entry = new Entry(new TournamentBiome(name), kind);
                ByName.Add(name, entry);
                fromTable.Add(entry.Biome);
                all.Add(entry.Biome);

                if (kind == BiomeKind.Playable)
                {
                    playable.Add(entry.Biome);
                }
            }

            // A family's target may already be in the table (inactiveLavaZone) or be new (lostRiver).
            List<Family> families = new List<Family>(FamilyTable.Length);
            foreach ((string prefix, string biomeName) in FamilyTable)
            {
                if (!ByName.TryGetValue(biomeName, out Entry target))
                {
                    target = new Entry(new TournamentBiome(biomeName), BiomeKind.Playable);
                    ByName.Add(biomeName, target);
                    all.Add(target.Biome);
                    playable.Add(target.Biome);
                }

                families.Add(new Family(prefix, target));
            }

            // Longest prefix first, so a shorter prefix cannot shadow a longer one.
            families.Sort((left, right) => right.Prefix.Length.CompareTo(left.Prefix.Length));
            FamiliesLongestPrefixFirstEntries = families.ToArray();

            Entry[] byLength = new Entry[ByName.Count];
            ByName.Values.CopyTo(byLength, 0);
            // Longest name first, so prefix matching cannot mistake bloodKelpTwo for bloodKelp.
            Array.Sort(byLength, (left, right) => right.Biome.Identifier.Length.CompareTo(left.Biome.Identifier.Length));
            LongestNameFirstEntries = byLength;

            BiomeTableBiomes = new ReadOnlyCollection<TournamentBiome>(fromTable);
            AllBiomes = new ReadOnlyCollection<TournamentBiome>(all);
            PlayableBiomes = new ReadOnlyCollection<TournamentBiome>(playable);
        }

        /// <summary>
        ///     Only the nineteen names in the game's own biome table, so the committed extract can be
        ///     checked against the installed file.
        /// </summary>
        public static IReadOnlyList<TournamentBiome> FromBiomeTable => BiomeTableBiomes;

        /// <summary>Every biome the tournament knows: the biome table plus the override families.</summary>
        public static IReadOnlyList<TournamentBiome> All => AllBiomes;

        /// <summary>
        /// The biomes that are actually on this map, and so the only ones a cull sequence may draw
        /// from. Excludes <c>void</c>, <c>unassigned</c> and <c>arctic</c>.
        /// </summary>
        public static IReadOnlyList<TournamentBiome> Playable => PlayableBiomes;

        internal static IReadOnlyList<Entry> LongestNameFirst => LongestNameFirstEntries;

        internal static IReadOnlyList<Family> FamiliesLongestPrefixFirst => FamiliesLongestPrefixFirstEntries;

        /// <summary>The authored override families, so a test can compare them against the file.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> OverrideFamilies
        {
            get
            {
                List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>(FamilyTable.Length);
                foreach ((string prefix, string biome) in FamilyTable)
                {
                    rows.Add(new KeyValuePair<string, string>(prefix, biome));
                }

                return rows;
            }
        }

        internal static bool TryFind(string name, out Entry entry) => ByName.TryGetValue(name, out entry);

        internal readonly struct Family
        {
            internal Family(string prefix, Entry target)
            {
                Prefix = prefix;
                Target = target;
            }

            internal string Prefix { get; }

            internal Entry Target { get; }
        }

        internal readonly struct Entry
        {
            internal Entry(TournamentBiome biome, BiomeKind kind)
            {
                Biome = biome;
                Kind = kind;
            }

            internal TournamentBiome Biome { get; }

            internal BiomeKind Kind { get; }
        }
    }
}
