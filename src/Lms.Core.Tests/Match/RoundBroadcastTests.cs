using System;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What the server tells one contestant a round is starting with.</summary>
[TestClass]
public class RoundBroadcastTests
{
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(30);

    private static RoundBroadcast Read(string line)
    {
        RoundBroadcast.TryDecode(line, out RoundBroadcast round).Should().BeTrue();

        return round;
    }

    [TestMethod]
    public void A_kit_and_a_hold_survive_the_round_trip()
    {
        RoundBroadcast sent = new RoundBroadcast(2, 1, Hold, new[] { "Knife", "Flashlight" });

        RoundBroadcast read = Read(sent.Encode());

        read.Group.Should().Be(2);
        read.Generation.Should().Be(1);
        read.Hold.Should().Be(Hold);
        read.Kit.Should().Equal("Knife", "Flashlight");
    }

    [TestMethod]
    public void An_empty_kit_is_the_ordinary_case_and_is_not_a_refusal()
    {
        Read(new RoundBroadcast(1, 1, Hold, null).Encode()).Kit.Should().BeEmpty();
    }

    [TestMethod]
    public void A_hold_of_nothing_is_a_real_instruction()
    {
        RoundBroadcast read = Read(new RoundBroadcast(1, 1, TimeSpan.Zero, null).Encode());

        read.Holds.Should().BeFalse();
    }

    [TestMethod]
    public void The_match_is_in_it_because_a_stale_instruction_has_to_be_refusable()
    {
        new RoundBroadcast(3, 2, Hold, null).Encode()
            .Should().Contain("group=3").And.Contain("gen=2");
    }

    [TestMethod]
    public void It_names_no_player_because_the_addressee_is_the_channel()
    {
        new RoundBroadcast(1, 1, TimeSpan.Zero, new[] { "Knife" }).Encode()
            .Should().Be("lms-round/1\tgroup=1\tgen=1\thold=0\titem=Knife");
    }

    [TestMethod]
    public void It_is_told_apart_from_the_other_messages_by_its_header_alone()
    {
        RoundBroadcast.IsRound(new RoundBroadcast(1, 1, Hold, null).Encode()).Should().BeTrue();
        RoundBroadcast.IsRound(MatchBroadcast.WireHeader + "\tx=1").Should().BeFalse();
        RoundBroadcast.IsRound(ArrearsBroadcast.WireHeader + "\towed=8").Should().BeFalse();
        RoundBroadcast.IsRound(new GhostBroadcast(true).Encode()).Should().BeFalse();
        RoundBroadcast.IsRound(null).Should().BeFalse();
    }

    [TestMethod]
    public void Anything_missing_is_declined_rather_than_guessed_at()
    {
        RoundBroadcast.TryDecode("lms-round/1\tgen=1\thold=0", out RoundBroadcast _)
            .Should().BeFalse("no group");
        RoundBroadcast.TryDecode("lms-round/1\tgroup=1\thold=0", out RoundBroadcast _)
            .Should().BeFalse("no generation");
        RoundBroadcast.TryDecode("lms-round/1\tgroup=1\tgen=1", out RoundBroadcast _)
            .Should().BeFalse("no hold");
        RoundBroadcast.TryDecode("lms-round/1\tgroup=1\tgen=1\thold=-1", out RoundBroadcast _)
            .Should().BeFalse("a hold does not run backwards");
        RoundBroadcast.TryDecode("lms-round/2\tgroup=1\tgen=1\thold=0", out RoundBroadcast _)
            .Should().BeFalse("a header this build does not know");
        RoundBroadcast.TryDecode(null, out RoundBroadcast _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_because_two_builds_at_once_is_ordinary()
    {
        Read("lms-round/1\tgroup=1\tgen=1\thold=0\tsomethingNew=7\titem=Knife")
            .Kit.Should().Equal("Knife");
    }

    [TestMethod]
    public void Two_of_the_same_instruction_are_equal_so_a_resend_is_not_a_second_clear()
    {
        new RoundBroadcast(1, 1, Hold, new[] { "Knife" })
            .Should().Be(new RoundBroadcast(1, 1, Hold, new[] { "Knife" }));

        new RoundBroadcast(1, 1, Hold, new[] { "Knife" })
            .Should().NotBe(new RoundBroadcast(1, 2, Hold, new[] { "Knife" }));

        new RoundBroadcast(1, 1, Hold, new[] { "Knife" })
            .Should().NotBe(new RoundBroadcast(1, 1, Hold, null));
    }
    [TestMethod]
    public void The_locked_recipes_survive_the_round_trip()
    {
        RoundBroadcast sent = new RoundBroadcast(2, 1, Hold, null, new[] { "Seamoth", "Cyclops" });

        Read(sent.Encode()).Locked.Should().Equal("Seamoth", "Cyclops");
    }

    [TestMethod]
    public void Locking_nothing_is_the_ordinary_case_and_still_grants()
    {
        Read(new RoundBroadcast(1, 1, Hold, null, null).Encode()).Locked.Should().BeEmpty();
    }

    [TestMethod]
    public void An_instruction_from_a_build_with_no_locked_field_still_reads()
    {
        string old = new RoundBroadcast(4, 2, Hold, new[] { "Flare" }).Encode();

        RoundBroadcast read = Read(old);

        read.Kit.Should().Equal("Flare");
        read.Locked.Should().BeEmpty();
    }

    [TestMethod]
    public void The_kit_and_the_locked_list_do_not_run_into_each_other()
    {
        RoundBroadcast sent = new RoundBroadcast(1, 1, Hold,
            new[] { "Flare", "NutrientBlock" }, new[] { "Exosuit" });

        RoundBroadcast read = Read(sent.Encode());

        read.Kit.Should().Equal("Flare", "NutrientBlock");
        read.Locked.Should().Equal("Exosuit");
    }

    [TestMethod]
    public void The_floor_a_round_grants_survives_the_round_trip()
    {
        RoundBroadcast sent = new RoundBroadcast(2, 1, Hold, null, null, new[] { "Knife", "Seaglide" });

        Read(sent.Encode()).Recipes.Should().Equal("Knife", "Seaglide");
    }

    [TestMethod]
    public void Granting_nothing_is_the_vanilla_baseline_rather_than_everything()
    {
        Read(new RoundBroadcast(1, 1, Hold, null, null, null).Encode()).Recipes.Should().BeEmpty();
        Read(new RoundBroadcast(1, 1, Hold, null, null, new string[0]).Encode()).Recipes.Should().BeEmpty();
    }

    [TestMethod]
    public void The_kit_the_locked_list_and_the_floor_do_not_run_into_each_other()
    {
        RoundBroadcast sent = new RoundBroadcast(1, 1, Hold,
            new[] { "Flare", "NutrientBlock" }, new[] { "Exosuit" }, new[] { "all" });

        RoundBroadcast read = Read(sent.Encode());

        read.Kit.Should().Equal("Flare", "NutrientBlock");
        read.Locked.Should().Equal("Exosuit");
        read.Recipes.Should().Equal("all");
    }

    [TestMethod]
    public void An_instruction_from_a_build_with_no_recipe_field_still_reads()
    {
        string old = "lms-round/1\tgroup=4\tgen=2\thold=30000\titem=Flare";

        RoundBroadcast read = Read(old);

        read.Kit.Should().Equal("Flare");
        read.Recipes.Should().BeEmpty();
    }

    [TestMethod]
    public void Two_instructions_differing_only_in_the_floor_are_not_the_same_instruction()
    {
        RoundBroadcast vanilla = new RoundBroadcast(1, 1, Hold, null, null, null);
        RoundBroadcast everything = new RoundBroadcast(1, 1, Hold, null, null, new[] { "all" });

        vanilla.Equals(everything).Should().BeFalse();
        vanilla.GetHashCode().Should().NotBe(everything.GetHashCode());
    }

    [TestMethod]
    public void Two_instructions_differing_only_in_what_is_locked_are_not_the_same_instruction()
    {
        RoundBroadcast granting = new RoundBroadcast(1, 1, Hold, null, null);
        RoundBroadcast withholding = new RoundBroadcast(1, 1, Hold, null, new[] { "Cyclops" });

        granting.Equals(withholding).Should().BeFalse();
    }

    [TestMethod]
    public void The_round_start_vitals_survive_the_round_trip()
    {
        RoundBroadcast sent = new RoundBroadcast(2, 1, Hold, null, null, null,
            new RoundVitals(health: 85, food: 60, water: 40));

        RoundVitals read = Read(sent.Encode()).Vitals!.Value;

        read.Health.Should().Be(85);
        read.Food.Should().Be(60);
        read.Water.Should().Be(40);
    }

    [TestMethod]
    public void A_round_without_vitals_encodes_none_and_decodes_null()
    {
        string line = new RoundBroadcast(1, 1, TimeSpan.Zero, new[] { "Knife" }).Encode();

        line.Should().Be("lms-round/1\tgroup=1\tgen=1\thold=0\titem=Knife");
        Read(line).Vitals.Should().BeNull();
    }

    [TestMethod]
    public void Each_vital_travels_under_its_own_key()
    {
        new RoundBroadcast(1, 1, Hold, null, null, null, new RoundVitals(10, 20, 30)).Encode()
            .Should().Contain("health=10").And.Contain("food=20").And.Contain("water=30");
    }

    [TestMethod]
    public void Two_instructions_differing_only_in_the_vitals_are_not_the_same_instruction()
    {
        RoundBroadcast full = new RoundBroadcast(1, 1, Hold, null, null, null, new RoundVitals(100, 100, 100));
        RoundBroadcast hurt = new RoundBroadcast(1, 1, Hold, null, null, null, new RoundVitals(50, 100, 100));

        full.Equals(hurt).Should().BeFalse();
    }

    [TestMethod]
    public void The_wire_field_is_written_in_the_invariant_culture()
    {
        new RoundVitals(10, 20, 30).Field(nameof(RoundVitals.Health))
            .Should().Be("10");
    }
}
