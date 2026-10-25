using System;
using FluentAssertions;
using Lms.Core.Biomes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Biomes;

/// <summary>Biome names compare case-insensitively, so a biome differing only in casing is the same biome.</summary>
[TestClass]
public class TournamentBiomeTests
{
    [TestMethod]
    public void Identifiers_that_differ_only_in_casing_are_the_same_biome()
    {
        new TournamentBiome("kelpForest").Should().Be(new TournamentBiome("KELPFOREST"));
    }

    [TestMethod]
    public void Identifiers_that_differ_only_in_casing_share_a_hash_code()
    {
        new TournamentBiome("CragField").GetHashCode()
            .Should().Be(new TournamentBiome("cragfield").GetHashCode());
    }

    [TestMethod]
    public void Different_identifiers_are_different_biomes()
    {
        new TournamentBiome("bloodKelp").Should().NotBe(new TournamentBiome("bloodKelpTwo"));
    }

    [TestMethod]
    public void Different_identifiers_hash_differently()
    {
        new TournamentBiome("bloodKelp").GetHashCode()
            .Should().NotBe(new TournamentBiome("bloodKelpTwo").GetHashCode());
    }

    [TestMethod]
    public void A_biome_is_never_equal_to_nothing()
    {
        new TournamentBiome("dunes").Equals(null).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(null, DisplayName = "null")]
    [DataRow("", DisplayName = "empty")]
    [DataRow(" ", DisplayName = "space")]
    [DataRow("\t\n", DisplayName = "whitespace")]
    public void An_identifier_that_names_nothing_is_rejected_rather_than_stored(string? identifier)
    {
        Action construct = () => new TournamentBiome(identifier!);

        construct.Should().Throw<ArgumentException>().WithParameterName("identifier");
    }

    [TestMethod]
    public void The_identifier_keeps_the_casing_it_was_given_for_display()
    {
        new TournamentBiome("grassyPlateaus").Identifier.Should().Be("grassyPlateaus");
        new TournamentBiome("grassyPlateaus").ToString().Should().Be("grassyPlateaus");
    }

    [TestMethod]
    public void Surrounding_whitespace_is_not_part_of_an_identifier()
    {
        new TournamentBiome("  safeShallows\n").Identifier.Should().Be("safeShallows");
    }
}
