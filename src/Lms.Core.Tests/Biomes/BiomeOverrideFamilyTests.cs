using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>The override families, checked against the committed family rules and the full set of override strings extracted from the world data.</summary>
[TestClass]
public class BiomeOverrideFamilyTests
{
    private static IEnumerable<string[]> DataRows(string fileName) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, fileName))
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split('\t'));

    [TestMethod]
    public void The_compiled_in_families_are_exactly_the_committed_ones()
    {
        var committed = DataRows("biome-override-families.tsv")
            .Select(columns => new KeyValuePair<string, string>(columns[0], columns[1]));

        KnownBiomes.OverrideFamilies.Should().BeEquivalentTo(committed);
    }

    [TestMethod]
    public void Every_override_string_in_the_game_resolves_to_the_biome_it_is_mapped_to()
    {
        List<string> wrong = new List<string>();

        foreach (string[] columns in DataRows("biome-overrides.tsv"))
        {
            string raw = columns[0];
            string expected = columns[1];
            BiomeResolution resolution = BiomeResolver.Resolve(raw);

            if (!resolution.IsResolved || resolution.Biome!.Identifier != expected)
            {
                wrong.Add($"{raw} -> {(resolution.IsResolved ? resolution.Biome!.Identifier : resolution.Failure.ToString())}, expected {expected}");
            }
        }

        wrong.Should().BeEmpty();
    }

    [TestMethod]
    public void All_twenty_seven_override_strings_are_accounted_for()
    {
        DataRows("biome-overrides.tsv").Should().HaveCount(27);
    }

    [TestMethod]
    public void Every_biome_string_a_batch_can_pull_from_a_prefab_resolves()
    {
        List<string> unresolved = new List<string>();

        foreach (string[] columns in DataRows("biome-override-prefabs.tsv"))
        {
            if (columns[1] == "-")
            {
                continue;
            }

            BiomeResolution resolution = BiomeResolver.Resolve(columns[1]);
            if (!resolution.IsResolved)
            {
                unresolved.Add($"{columns[1]} ({resolution.Failure}), from {columns[0]}");
            }
        }

        unresolved.Should().BeEmpty();
    }

    [TestMethod]
    public void All_thirty_four_prefab_sourced_batches_are_accounted_for()
    {
        DataRows("biome-override-prefabs.tsv").Should().HaveCount(34);
    }

    [TestMethod]
    public void A_family_beats_a_table_name_it_happens_to_start_with()
    {
        BiomeResolver.Resolve("BloodKelp_Trench").Biome!.Identifier.Should().Be("bloodKelpTrench");
        BiomeResolver.Resolve("BloodKelp").Biome!.Identifier.Should().Be("bloodKelp");
    }

    [TestMethod]
    [DataRow("ILZCorridor", "inactiveLavaZone")]
    [DataRow("ALZChamber", "activeLavaZone")]
    [DataRow("LavaLakes", "activeLavaZone")]
    [DataRow("LostRiver_Junction", "lostRiver")]
    [DataRow("JellyshroomCaves", "jellyshroomCaves")]
    [DataRow("Prison_UpperRoom", "primaryContainmentFacility")]
    [DataRow("deepGrandReef", "deepGrandReef")]
    [DataRow("DeepGrandReef", "deepGrandReef")]
    public void Abbreviations_and_regions_the_table_cannot_name_resolve_through_families(string raw, string expected)
    {
        BiomeResolver.Resolve(raw).Biome!.Identifier.Should().Be(expected);
    }

    [TestMethod]
    public void A_sub_area_that_has_never_been_seen_still_resolves_to_its_region()
    {
        BiomeResolver.Resolve("LostRiver_SomewhereNew").Biome!.Identifier.Should().Be("lostRiver");
        BiomeResolver.Resolve("ILZSomethingElse").Biome!.Identifier.Should().Be("inactiveLavaZone");
    }

    [TestMethod]
    public void A_string_belonging_to_no_family_and_no_table_name_is_still_unresolved()
    {
        BiomeResolver.Resolve("atlantis_Deep").IsResolved.Should().BeFalse();
    }
}
