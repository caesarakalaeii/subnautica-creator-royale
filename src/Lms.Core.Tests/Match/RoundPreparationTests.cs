using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The decision half of <c>lms round</c>.</summary>
[TestClass]
public class RoundPreparationTests
{
    private const int GroupSize = 5;

    private static PlayerReading Connected(ushort id, string name) =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 100, 45, 100, 100);

    private static IReadOnlyList<PlayerReading> Group(params string[] names)
    {
        List<PlayerReading> readings = new List<PlayerReading>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            readings.Add(Connected((ushort)(i + 1), names[i]));
        }

        return readings;
    }

    private static RoundPlan Plan(MatchClockState state, IReadOnlyList<PlayerReading> connected,
        GhostRoster? ghosts = null, bool forced = false, IReadOnlyList<string>? ready = null) =>
        RoundPreparation.Plan(state, connected, ghosts ?? new GhostRoster(), GroupSize, forced, ready);

    [TestMethod]
    public void A_full_group_in_the_lobby_is_ready()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"));

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
        plan.AgainstTheCount.Should().BeFalse();
    }

    [TestMethod]
    public void The_contestants_are_who_is_connected_minus_the_ghosts()
    {
        GhostRoster ghosts = new GhostRoster(new[] { "Caster" });

        RoundPlan plan = Plan(MatchClockState.NotStarted,
            Group("Ada", "Caster", "Bea", "Cy", "Dov", "Eir"), ghosts);

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void The_list_is_in_one_order_because_it_goes_into_the_evidence()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Eir", "Ada", "Dov", "Cy", "Bea"));

        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    [DataRow(MatchClockState.Running)]
    [DataRow(MatchClockState.Paused)]
    public void A_round_start_mid_match_is_a_mistake_rather_than_an_instruction(MatchClockState state)
    {
        RoundPlan plan = Plan(state, Group("Ada", "Bea", "Cy", "Dov", "Eir"));

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("Reset it first");
    }

    [TestMethod]
    public void And_force_does_not_override_that_one()
    {
        Plan(MatchClockState.Running, Group("Ada", "Bea", "Cy", "Dov", "Eir"), forced: true)
            .IsRefused.Should().BeTrue();
    }

    [TestMethod]
    public void The_wrong_number_of_players_is_refused_and_the_refusal_names_them()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov"));

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("Ada").And.Contain("Dov");
        plan.Message.Should().Contain("5").And.Contain("4");
        plan.Message.Should().Contain("lms round force");
    }

    [TestMethod]
    public void Too_many_players_is_refused_the_same_way()
    {
        Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir", "Fen"))
            .IsRefused.Should().BeTrue();
    }

    [TestMethod]
    public void Force_prepares_the_round_anyway_and_says_it_is_against_the_count()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea"), forced: true);

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea");
        plan.AgainstTheCount.Should().BeTrue("an override nobody is told about is one they forget");
    }

    [TestMethod]
    public void Force_on_a_group_that_was_the_right_size_anyway_is_not_an_override()
    {
        Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"), forced: true)
            .AgainstTheCount.Should().BeFalse();
    }

    [TestMethod]
    public void A_contestant_that_has_not_reported_ready_refuses_the_round_and_is_named()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"),
            ready: new[] { "Ada", "Bea", "Dov", "Eir" });

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("Cy");
        plan.Message.Should().NotContain("Ada").And.NotContain("Bea").And.NotContain("Eir");
        plan.Message.Should().Contain("lms round force");
    }

    [TestMethod]
    public void Force_prepares_a_round_with_unready_contestants_anyway()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"),
            forced: true, ready: new[] { "Ada" });

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void An_all_ready_group_prepares_ordinarily()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"),
            ready: new[] { "Eir", "Dov", "Cy", "Bea", "Ada" });

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void A_ready_set_nobody_has_joined_gates_nobody_yet()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"),
            ready: new string[0]);

        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void A_ghost_who_is_still_loading_does_not_refuse_the_round()
    {
        GhostRoster ghosts = new GhostRoster(new[] { "Caster" });

        RoundPlan plan = Plan(MatchClockState.NotStarted,
            Group("Ada", "Bea", "Cy", "Dov", "Eir", "Caster"), ghosts, ready: new[] { "Ada", "Bea", "Cy", "Dov", "Eir" });

        plan.IsRefused.Should().BeFalse(plan.Message);
    }

    [TestMethod]
    public void An_absent_ready_list_gates_nobody_the_way_an_older_build_read()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Ada", "Bea", "Cy", "Dov", "Eir"));

        plan.IsRefused.Should().BeFalse(plan.Message);
    }

    [TestMethod]
    public void Nobody_connected_is_refused_even_when_forced()
    {
        RoundPlan plan = Plan(MatchClockState.NotStarted, Group(), forced: true);

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("nobody is connected");
    }

    [TestMethod]
    public void A_world_of_nothing_but_ghosts_is_refused_and_says_which()
    {
        GhostRoster ghosts = new GhostRoster(new[] { "Caster", "Producer" });

        RoundPlan plan = Plan(MatchClockState.NotStarted, Group("Caster", "Producer"), ghosts,
            forced: true);

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("Caster").And.Contain("Producer");
    }

    [TestMethod]
    public void A_refused_plan_names_nobody_at_all()
    {
        Plan(MatchClockState.Running, Group("Ada")).Contestants.Should().BeEmpty();
    }

    [TestMethod]
    public void Nothing_at_all_is_a_refusal_rather_than_a_throw()
    {
        RoundPreparation.Plan(MatchClockState.NotStarted, null, null, GroupSize, false)
            .IsRefused.Should().BeTrue();
    }

    [TestMethod]
    public void A_bracket_takes_who_is_in_the_playing_group_rather_than_who_is_connected()
    {
        BracketRoster bracket = new BracketRoster(2, new[] { "Ada", "Bea", "Cy", "Dov", "Eir" });

        RoundPlan plan = RoundPreparation.Plan(MatchClockState.NotStarted,
            Group("Ada", "Bea", "Cy", "Dov", "Eir", "Latecomer"), new GhostRoster(), GroupSize,
            false, null, bracket);

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void The_ghost_rule_still_applies_on_top_of_the_bracket()
    {
        GhostRoster ghosts = new GhostRoster(new[] { "Caster" });
        BracketRoster bracket = new BracketRoster(2, new[] { "Ada", "Bea", "Cy", "Dov", "Eir", "Caster" });

        RoundPlan plan = RoundPreparation.Plan(MatchClockState.NotStarted,
            Group("Ada", "Bea", "Cy", "Dov", "Eir", "Caster"), ghosts, GroupSize, false, null,
            bracket);

        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void An_empty_bracket_filters_nobody_which_is_what_no_bracket_meant()
    {
        BracketRoster bracket = new BracketRoster(2, Array.Empty<string>());

        RoundPlan plan = RoundPreparation.Plan(MatchClockState.NotStarted,
            Group("Ada", "Bea", "Cy", "Dov", "Eir"), new GhostRoster(), GroupSize, false, null,
            bracket);

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void An_absent_bracket_prepares_the_round_the_way_the_day_read_before_it()
    {
        RoundPlan plan = RoundPreparation.Plan(MatchClockState.NotStarted,
            Group("Ada", "Bea", "Cy", "Dov", "Eir"), new GhostRoster(), GroupSize, false, null, null);

        plan.IsRefused.Should().BeFalse(plan.Message);
        plan.Contestants.Should().Equal("Ada", "Bea", "Cy", "Dov", "Eir");
    }

    [TestMethod]
    public void The_count_refusal_speaks_in_the_bracketed_set_the_round_would_seat()
    {
        BracketRoster bracket = new BracketRoster(2, new[] { "Ada" });

        RoundPlan plan = RoundPreparation.Plan(MatchClockState.NotStarted, Group("Ada", "Bea"),
            new GhostRoster(), GroupSize, false, null, bracket);

        plan.IsRefused.Should().BeTrue();
        plan.Message.Should().Contain("Ada").And.NotContain("Bea");
        plan.Message.Should().Contain("1").And.Contain("5");
    }
}
