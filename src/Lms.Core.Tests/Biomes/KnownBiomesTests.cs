using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>The compiled-in biome table against the committed <c>docs/data/biome-names.txt</c>, in both directions.</summary>
[TestClass]
public class KnownBiomesTests
{
    private static readonly string[] CommittedNames =
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "biome-names.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    [TestMethod]
    public void The_compiled_in_table_is_exactly_the_committed_extract_of_biomes_csv()
    {
        KnownBiomes.FromBiomeTable.Select(biome => biome.Identifier)
            .Should().BeEquivalentTo(CommittedNames);
    }

    [TestMethod]
    public void The_map_has_nineteen_biome_names_not_the_several_hundred_of_the_BiomeType_enum()
    {
        KnownBiomes.FromBiomeTable.Should().HaveCount(19);
    }

    [TestMethod]
    public void No_name_is_classified_twice()
    {
        KnownBiomes.All.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Only_void_unassigned_and_arctic_are_kept_out_of_the_playable_set()
    {
        KnownBiomes.All.Except(KnownBiomes.Playable).Select(biome => biome.Identifier)
            .Should().BeEquivalentTo("void", "unassigned", "arctic");
    }

    [TestMethod]
    public void Twenty_three_biomes_are_left_for_the_cull_sequence_to_draw_from()
    {
        KnownBiomes.Playable.Should().HaveCount(23);
        KnownBiomes.Playable.Should().Contain(new TournamentBiome("lostRiver"));
        KnownBiomes.Playable.Should().Contain(new TournamentBiome("activeLavaZone"));
        KnownBiomes.Playable.Should().Contain(new TournamentBiome("floatingIsland"));
    }

    [TestMethod]
    public void The_inactive_lava_zone_is_reachable_through_its_override_family()
    {
        BiomeResolver.Resolve("ILZChamber").Biome.Should().Be(new TournamentBiome("inactiveLavaZone"));
    }
}
