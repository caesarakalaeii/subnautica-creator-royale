using System.Linq;
using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>The log of distinct raw biome values in first-seen order, with what became of each.</summary>
[TestClass]
public class BiomeSightingLogTests
{
    private static BiomeResolution Sample(string? raw) => BiomeResolver.Resolve(raw);

    [TestMethod]
    public void The_first_time_a_value_is_seen_it_comes_back_as_a_new_sighting()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        BiomeSighting? sighting = log.Record(Sample("kelpForest"));

        sighting.Should().NotBeNull();
        sighting!.RawValue.Should().Be("kelpForest");
        sighting.Biome.Should().Be(new TournamentBiome("kelpForest"));
        sighting.TimesSeen.Should().Be(1);
    }

    [TestMethod]
    public void Seeing_a_value_again_is_not_a_new_sighting_but_is_counted()
    {
        BiomeSightingLog log = new BiomeSightingLog();
        log.Record(Sample("kelpForest"));

        log.Record(Sample("kelpForest")).Should().BeNull();

        log.Sightings.Should().HaveCount(1);
        log.Sightings[0].TimesSeen.Should().Be(2);
    }

    [TestMethod]
    public void A_value_that_differs_only_in_casing_is_a_separate_sighting()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        log.Record(Sample("dunes")).Should().NotBeNull();
        log.Record(Sample("Dunes")).Should().NotBeNull();

        log.Sightings.Should().HaveCount(2);
    }

    [TestMethod]
    public void A_sample_with_no_biome_reported_is_not_a_sighting_at_all()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        log.Record(Sample(null)).Should().BeNull();

        log.Sightings.Should().BeEmpty();
    }

    [TestMethod]
    public void An_override_that_resolved_by_prefix_is_flagged_because_that_is_the_interesting_case()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        BiomeSighting sighting = log.Record(Sample("grassyPlateaus_Grass"))!;

        sighting.ResolvedByPrefix.Should().BeTrue();
        sighting.Biome!.Identifier.Should().Be("grassyPlateaus");
    }

    [TestMethod]
    public void An_exact_table_name_is_not_flagged_as_a_prefix_match()
    {
        new BiomeSightingLog().Record(Sample("KELPFOREST"))!.ResolvedByPrefix.Should().BeFalse();
    }

    [TestMethod]
    public void Unresolved_values_are_counted_so_a_readout_can_show_the_number()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        log.Record(Sample("atlantis_Deep"));
        log.Record(Sample("atlantis_Deep"));
        log.Record(Sample("someOtherPlace"));
        log.Record(Sample("kelpForest"));

        log.UnresolvedCount.Should().Be(2);
        log.Sightings.Should().HaveCount(3);
    }

    [TestMethod]
    public void A_blank_name_is_recorded_because_something_authored_it()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        BiomeSighting sighting = log.Record(Sample(" "))!;

        sighting.Failure.Should().Be(BiomeResolutionFailure.BlankName);
        log.UnresolvedCount.Should().Be(1);
    }

    [TestMethod]
    public void Sightings_come_back_in_the_order_they_were_first_seen()
    {
        BiomeSightingLog log = new BiomeSightingLog();

        log.Record(Sample("safeShallows"));
        log.Record(Sample("kelpForest_Base"));
        log.Record(Sample("safeShallows"));
        log.Record(Sample("dunes"));

        log.Sightings.Select(sighting => sighting.RawValue)
            .Should().Equal("safeShallows", "kelpForest_Base", "dunes");
    }

    [TestMethod]
    [DataRow("grassyPlateaus_Grass", "grassyPlateaus_Grass\tgrassyPlateaus\tPlayable\tprefix", DisplayName = "override matched by prefix")]
    [DataRow("kelpForest", "kelpForest\tkelpForest\tPlayable\texact", DisplayName = "table name")]
    [DataRow("void", "void\tvoid\tOutsideMap\texact", DisplayName = "not playable but known")]
    [DataRow("nowhereInParticular", "nowhereInParticular\t-\t-\tUnknownName", DisplayName = "unresolved")]
    public void A_sighting_records_itself_as_one_tab_separated_line(string raw, string expected)
    {
        new BiomeSightingLog().Record(Sample(raw))!.ToString().Should().Be(expected);
    }
}
