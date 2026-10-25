using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>Resolving the raw <c>LargeWorld.GetBiome()</c> value: resolve what is knowable and be explicit about what is not.</summary>
[TestClass]
public class BiomeResolverTests
{
    [TestMethod]
    public void A_name_straight_out_of_the_biome_table_resolves()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("kelpForest");

        resolution.IsResolved.Should().BeTrue();
        resolution.Biome.Should().Be(new TournamentBiome("kelpForest"));
        resolution.Kind.Should().Be(BiomeKind.Playable);
        resolution.Failure.Should().Be(BiomeResolutionFailure.None);
    }

    [TestMethod]
    [DataRow("KELPFOREST", "kelpForest")]
    [DataRow("kelpforest", "kelpForest")]
    [DataRow("cragfield", "CragField")]
    [DataRow("CRAGFIELD", "CragField")]
    [DataRow("SafeShallows", "safeShallows")]
    public void Casing_does_not_matter_and_the_table_spelling_comes_back(string raw, string expected)
    {
        BiomeResolution resolution = BiomeResolver.Resolve(raw);

        resolution.IsResolved.Should().BeTrue();
        resolution.Biome!.Identifier.Should().Be(expected);
    }

    [TestMethod]
    public void A_per_batch_override_resolves_by_its_prefix()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("grassyPlateaus_Grass");

        resolution.Biome.Should().Be(new TournamentBiome("grassyPlateaus"));
    }

    [TestMethod]
    [DataRow("bloodKelpTwo_Floor")]
    [DataRow("bloodkelptwo_CaveFloor")]
    [DataRow("bloodKelpTwo")]
    public void The_longest_matching_prefix_wins_so_bloodKelpTwo_is_never_bloodKelp(string raw)
    {
        BiomeResolution resolution = BiomeResolver.Resolve(raw);

        resolution.Biome!.Identifier.Should().Be("bloodKelpTwo");
    }

    [TestMethod]
    public void A_shorter_name_still_wins_when_it_is_the_only_prefix_that_matches()
    {
        BiomeResolver.Resolve("bloodKelp_Deep").Biome!.Identifier.Should().Be("bloodKelp");
    }

    [TestMethod]
    public void No_biome_reported_at_all_is_not_a_biome()
    {
        BiomeResolution resolution = BiomeResolver.Resolve(null);

        resolution.IsResolved.Should().BeFalse();
        resolution.Biome.Should().BeNull();
        resolution.Failure.Should().Be(BiomeResolutionFailure.NoBiomeReported);
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "spaces")]
    public void A_name_that_is_blank_does_not_resolve(string raw)
    {
        BiomeResolution resolution = BiomeResolver.Resolve(raw);

        resolution.IsResolved.Should().BeFalse();
        resolution.Biome.Should().BeNull();
        resolution.Failure.Should().Be(BiomeResolutionFailure.BlankName);
    }

    [TestMethod]
    public void An_unrecognised_name_is_unresolved_and_hands_the_raw_string_back_for_the_log()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("atlantis_Deep");

        resolution.IsResolved.Should().BeFalse();
        resolution.Biome.Should().BeNull();
        resolution.Failure.Should().Be(BiomeResolutionFailure.UnknownName);
        resolution.RawValue.Should().Be("atlantis_Deep");
    }

    [TestMethod]
    public void A_resolved_name_keeps_the_raw_string_too_so_overrides_can_be_collected()
    {
        BiomeResolver.Resolve("  grassyPlateaus_Grass ").RawValue.Should().Be("  grassyPlateaus_Grass ");
    }

    [TestMethod]
    public void The_biome_maps_own_null_resolves_but_is_never_playable()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("unassigned");

        resolution.IsResolved.Should().BeTrue();
        resolution.Kind.Should().Be(BiomeKind.Unmapped);
        KnownBiomes.Playable.Should().NotContain(resolution.Biome!);
    }

    [TestMethod]
    public void Beyond_the_map_edge_resolves_but_is_never_playable()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("void");

        resolution.IsResolved.Should().BeTrue();
        resolution.Kind.Should().Be(BiomeKind.OutsideMap);
        KnownBiomes.Playable.Should().NotContain(resolution.Biome!);
    }

    [TestMethod]
    public void A_biome_that_belongs_to_the_other_game_resolves_but_is_never_playable()
    {
        BiomeResolution resolution = BiomeResolver.Resolve("arctic");

        resolution.IsResolved.Should().BeTrue();
        resolution.Kind.Should().Be(BiomeKind.NotInThisGame);
        KnownBiomes.Playable.Should().NotContain(resolution.Biome!);
    }
}
