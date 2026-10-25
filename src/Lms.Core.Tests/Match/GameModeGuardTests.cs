using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Who a round has to put back into survival before it starts.</summary>
[TestClass]
public class GameModeGuardTests
{
    private static PlayerMode Mode(ushort id, string name, string mode) =>
        new PlayerMode(id, name, mode);

    private static IReadOnlyList<string> Contestants(params string[] names) => names;

    [TestMethod]
    public void A_group_already_in_survival_is_left_alone()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "SURVIVAL"), Mode(2, "Bea", "SURVIVAL") });

        sweep.Wrong.Should().BeEmpty();
        sweep.IsClean.Should().BeTrue();
    }

    [TestMethod]
    public void A_contestant_in_creative_is_named_with_the_mode_they_held()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "SURVIVAL"), Mode(2, "Bea", "CREATIVE") });

        sweep.IsClean.Should().BeFalse();
        sweep.Wrong.Should().ContainSingle();
        sweep.Wrong[0].Name.Should().Be("Bea");
        sweep.Wrong[0].Id.Should().Be(2);

        sweep.Wrong[0].Mode.Should().Be("CREATIVE");
    }

    [TestMethod]
    public void Freedom_and_hardcore_are_not_survival_either()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "FREEDOM"), Mode(2, "Bea", "HARDCORE") });

        sweep.Wrong.Should().HaveCount(2);
    }

    [TestMethod]
    public void The_mode_is_matched_without_case_or_padding_mattering()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada"),
            new[] { Mode(1, "Ada", " survival ") });

        sweep.Wrong.Should().BeEmpty("the server's own spelling of a mode is not a tournament rule");
    }

    [TestMethod]
    public void Somebody_who_is_not_a_contestant_is_never_touched()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada"),
            new[] { Mode(1, "Ada", "SURVIVAL"), Mode(2, "Caster", "CREATIVE") });

        sweep.Wrong.Should().BeEmpty();
    }

    [TestMethod]
    public void A_contestant_name_matches_however_it_is_cased()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("ADA"),
            new[] { Mode(1, "ada", "CREATIVE") });

        sweep.Wrong.Should().ContainSingle();
    }

    [TestMethod]
    public void A_contestant_the_server_cannot_see_is_reported_rather_than_ignored()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "SURVIVAL") });

        sweep.Wrong.Should().BeEmpty();
        sweep.Unseen.Should().ContainSingle();
        sweep.Unseen[0].Should().Be("Bea");
        sweep.IsClean.Should().BeFalse("a contestant nobody could read is not a clean sweep");
    }

    [TestMethod]
    public void A_contestant_the_seam_could_not_read_stops_a_round_rather_than_being_logged_past()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "SURVIVAL") });

        sweep.Refusal.Should().NotBeNull();
        sweep.Refusal.Should().Contain("Bea");
    }

    [TestMethod]
    public void And_names_every_one_of_them_because_the_operator_has_to_go_and_look()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea", "Cal"),
            new[] { Mode(1, "Ada", "SURVIVAL") });

        sweep.Refusal.Should().Contain("Bea").And.Contain("Cal");
    }

    [TestMethod]
    public void A_mode_that_is_merely_wrong_does_not_stop_a_round_because_it_is_corrected()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada"),
            new[] { Mode(1, "Ada", "CREATIVE") });

        sweep.Wrong.Should().ContainSingle();
        sweep.Refusal.Should().BeNull();
    }

    [TestMethod]
    public void A_clean_sweep_refuses_nothing()
    {
        GameModeGuard.Plan(Contestants("Ada"), new[] { Mode(1, "Ada", "SURVIVAL") })
            .Refusal.Should().BeNull();
    }

    [TestMethod]
    public void A_server_that_cannot_read_a_mode_at_all_refuses_every_round_on_it()
    {
        GameModeGuard.Plan(Contestants("Ada", "Bea"), null!).Refusal.Should().NotBeNull();
    }

    [TestMethod]
    public void An_empty_or_null_reading_is_not_a_crash()
    {
        GameModeGuard.Plan(Contestants("Ada"), null!).Unseen.Should().ContainSingle();
        GameModeGuard.Plan(null!, new[] { Mode(1, "Ada", "CREATIVE") }).Wrong.Should().BeEmpty();
    }

    [TestMethod]
    public void The_wrong_are_named_in_one_order()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Cy", "Ada", "Bea"),
            new[] { Mode(3, "Cy", "CREATIVE"), Mode(1, "Ada", "CREATIVE"), Mode(2, "Bea", "CREATIVE") });

        sweep.Wrong.Should().HaveCount(3);
        sweep.Wrong[0].Name.Should().Be("Ada");
        sweep.Wrong[1].Name.Should().Be("Bea");
        sweep.Wrong[2].Name.Should().Be("Cy");
    }

    [TestMethod]
    public void The_sentence_names_who_and_what_they_were_in()
    {
        GameModeSweep sweep = GameModeGuard.Plan(
            Contestants("Ada", "Bea"),
            new[] { Mode(1, "Ada", "SURVIVAL"), Mode(2, "Bea", "CREATIVE") });

        sweep.Message.Should().Contain("Bea");
        sweep.Message.Should().Contain("CREATIVE");
    }
}
