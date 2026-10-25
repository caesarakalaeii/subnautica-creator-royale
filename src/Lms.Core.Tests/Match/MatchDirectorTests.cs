using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>One match at a time: the director is what start, pause, skip, reset and next group do; the clock underneath only counts.</summary>
[TestClass]
public class MatchDirectorTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(1);

    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Spawn, 1400), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(200, -150, 100), 400), TimeSpan.FromSeconds(90), 8),
        });

    private static MatchDirector NewDirector() =>
        new MatchDirector(new MatchClockSettings(Cycle, WarningLead), Schedule());

    private static MatchDirector RunningDirector()
    {
        MatchDirector director = NewDirector();
        director.Execute(AdminCommand.Start);
        return director;
    }

    [TestMethod]
    public void A_count_of_absentees_below_zero_is_refused()
    {
        MatchDirector director = RunningDirector();

        Action nonsense = () => director.Standing(new[] { "Vela" }, -1, TimeSpan.FromMinutes(1));

        nonsense.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_match_never_starts_itself()
    {
        MatchDirector director = NewDirector();

        director.AdvanceTo(TimeSpan.FromHours(2));

        director.State.Should().Be(MatchClockState.NotStarted);
        director.Elapsed.Should().Be(TimeSpan.Zero);
        director.CompletedPhases.Should().Be(0);
    }


    [TestMethod]
    public void A_match_waits_in_the_lobby_of_group_one_until_an_admin_starts_it()
    {
        MatchDirector director = NewDirector();

        director.Group.Should().Be(1);
        director.State.Should().Be(MatchClockState.NotStarted);
        director.Advance(TimeSpan.FromMinutes(30)).Should().BeEmpty();
        director.CompletedPhases.Should().Be(0);
    }

    [TestMethod]
    public void A_started_match_is_anchored_to_the_moment_it_started()
    {
        MatchDirector director = NewDirector();
        director.AdvanceTo(TimeSpan.FromMinutes(1));
        director.Execute(AdminCommand.Start, TimeSpan.FromMinutes(1));

        director.State.Should().Be(MatchClockState.Running);
        director.Elapsed.Should().Be(TimeSpan.Zero);

        director.AdvanceTo(TimeSpan.FromMinutes(2));

        director.Elapsed.Should().Be(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void Starting_runs_the_clock()
    {
        MatchDirector director = NewDirector();

        AdminCommandResult result = director.Execute(AdminCommand.Start);

        result.Applied.Should().BeTrue();
        director.Advance(Cycle).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);
    }

    [TestMethod]
    public void Starting_a_running_match_is_refused_and_says_why()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(2));

        AdminCommandResult result = director.Execute(AdminCommand.Start);

        result.Applied.Should().BeFalse();
        result.Message.Should().Contain("running");
        director.Elapsed.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Pausing_and_resuming_hold_the_match_where_it_was()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(2));

        director.Execute(AdminCommand.Pause).Applied.Should().BeTrue();
        director.Advance(TimeSpan.FromMinutes(10)).Should().BeEmpty();
        director.Execute(AdminCommand.Resume).Applied.Should().BeTrue();
        director.Advance(TimeSpan.FromMinutes(3)).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);

        director.Elapsed.Should().Be(Cycle);
    }

    [TestMethod]
    public void Resuming_a_match_that_is_not_paused_is_refused()
    {
        MatchDirector director = RunningDirector();

        AdminCommandResult result = director.Execute(AdminCommand.Resume);

        result.Applied.Should().BeFalse();
        result.Message.Should().Contain("not paused");
    }

    [TestMethod]
    public void Skipping_starts_the_next_phase_now_and_hands_back_what_moved()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(1));

        AdminCommandResult result = director.Execute(AdminCommand.Skip);

        result.Applied.Should().BeTrue();
        result.Events.Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseStarted);
        director.CompletedPhases.Should().Be(1);
    }

    [TestMethod]
    public void Skipping_before_the_match_starts_is_refused()
    {
        MatchDirector director = NewDirector();

        director.Execute(AdminCommand.Skip).Applied.Should().BeFalse();

        director.CompletedPhases.Should().Be(0);
    }

    [TestMethod]
    public void The_match_stops_closing_once_the_zone_has_closed()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromHours(1));

        director.TotalPhases.Should().Be(2, "an operator counts the phases they wrote");
        director.CompletedPhases.Should().Be(3, "the match ran one more than the file did");
        director.IsCollapsing.Should().BeTrue();
        director.AllPhasesFired.Should().BeTrue();
        director.Zone.Sphere.Radius.Should().Be(0);
        director.Execute(AdminCommand.Skip).Applied.Should().BeFalse();
    }

    [TestMethod]
    public void The_zone_waits_at_the_last_authored_sphere_before_it_closes()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromMinutes(12));

        director.CompletedPhases.Should().Be(2);
        director.Zone.Sphere.Radius.Should().Be(400);
        director.Zone.IsShrinking.Should().BeFalse();
    }

    [TestMethod]
    public void A_closed_zone_stays_closed_until_somebody_resets_it()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromHours(1));
        director.Zone.Sphere.Radius.Should().Be(0);

        director.Advance(TimeSpan.FromHours(1));
        director.Zone.Sphere.Radius.Should().Be(0, "nothing reopens a closed zone on its own");

        director.Execute(AdminCommand.Reset).Applied.Should().BeTrue();

        director.Zone.Sphere.Radius.Should().Be(1400);
        director.AllPhasesFired.Should().BeFalse();
    }

    [TestMethod]
    public void An_operator_can_skip_straight_into_the_collapse()
    {
        MatchDirector director = RunningDirector();

        director.Execute(AdminCommand.Skip);
        director.Execute(AdminCommand.Skip);
        AdminCommandResult collapse = director.Execute(AdminCommand.Skip);
        director.Advance(TimeSpan.FromSeconds(45));

        collapse.Applied.Should().BeTrue();
        director.CompletedPhases.Should().Be(3);
        director.Zone.Sphere.Radius.Should().BeApproximately(200, 1e-9, "the collapse closes over the last phase's 90s window");
    }

    [TestMethod]
    public void The_zone_is_the_opening_sphere_until_the_first_phase_fires()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromMinutes(2));

        director.Zone.Sphere.Radius.Should().Be(1400);
        director.Zone.PhaseNumber.Should().Be(0);
    }

    [TestMethod]
    public void The_zone_follows_the_phases_that_have_fired()
    {
        MatchDirector director = RunningDirector();

        director.Advance(Cycle + TimeSpan.FromSeconds(45));

        director.Zone.PhaseNumber.Should().Be(1);
        director.Zone.IsShrinking.Should().BeTrue();
        director.Zone.Sphere.Radius.Should().BeApproximately(1150, 1e-9);
    }

    [TestMethod]
    public void The_zone_holds_the_closed_sphere_once_the_collapse_is_done()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromHours(1));

        director.Zone.Sphere.Radius.Should().Be(0);
        director.Zone.IsShrinking.Should().BeFalse();
    }

    [TestMethod]
    public void The_zone_bites_nobody_in_the_lobby()
    {
        NewDirector().IsLive.Should().BeFalse();
    }

    [TestMethod]
    public void The_zone_bites_nobody_while_the_match_is_paused()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(6));

        director.Execute(AdminCommand.Pause);

        director.IsLive.Should().BeFalse();
        director.Execute(AdminCommand.Resume);
        director.IsLive.Should().BeTrue();
    }

    [TestMethod]
    public void Resetting_replays_the_same_group_from_the_lobby()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(12));

        AdminCommandResult result = director.Execute(AdminCommand.Reset);

        result.Applied.Should().BeTrue();
        director.Group.Should().Be(1);
        director.State.Should().Be(MatchClockState.NotStarted);
        director.CompletedPhases.Should().Be(0);
        director.Zone.Sphere.Radius.Should().Be(1400);
        director.UntilNextPhase.Should().Be(Cycle);
    }

    [TestMethod]
    public void The_next_group_is_a_fresh_match_with_the_same_rules()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(12));

        director.Execute(AdminCommand.NextGroup).Applied.Should().BeTrue();

        director.Group.Should().Be(2);
        director.State.Should().Be(MatchClockState.NotStarted);
        director.CompletedPhases.Should().Be(0);
        director.AllPhasesFired.Should().BeFalse();
    }

    [TestMethod]
    public void Every_reset_and_every_group_change_invalidates_per_match_state()
    {
        MatchDirector director = RunningDirector();
        int first = director.Generation;

        director.Execute(AdminCommand.Reset);
        int second = director.Generation;
        director.Execute(AdminCommand.NextGroup);

        second.Should().NotBe(first);
        director.Generation.Should().NotBe(second);
    }

    [TestMethod]
    public void A_match_that_is_only_advanced_keeps_the_same_generation()
    {
        MatchDirector director = RunningDirector();
        int generation = director.Generation;

        director.Advance(TimeSpan.FromMinutes(12));

        director.Generation.Should().Be(generation);
    }

    [TestMethod]
    public void Status_says_which_group_it_is_and_how_far_the_squeeze_has_got()
    {
        MatchDirector director = RunningDirector();
        director.Advance(TimeSpan.FromMinutes(6));

        AdminCommandResult result = director.Execute(AdminCommand.Status);

        result.Applied.Should().BeFalse("asking where the match is does not move it");
        result.Message.Should().Be(director.Status);
        director.Status.Should().Contain("group 1").And.Contain("1/2").And.Contain("04:00");
    }

    [TestMethod]
    public void Status_says_when_the_zone_has_closed()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromHours(1));

        director.Status.Should().Contain("closed").And.Contain("reset").And.Contain("r0m");
    }

    [TestMethod]
    public void Status_names_the_collapse_rather_than_a_phase_number_nobody_wrote()
    {
        MatchDirector director = RunningDirector();

        director.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(30));

        director.Status.Should().Contain("final collapse").And.NotContain("3/2");
    }

    [TestMethod]
    public void Status_says_where_the_wall_is()
    {
        RunningDirector().Status.Should().Contain("r1400m");
    }

    [TestMethod]
    public void Advancing_hands_back_the_phases_that_fell_due()
    {
        MatchDirector director = RunningDirector();

        var kinds = director.Advance(TimeSpan.FromMinutes(6)).Select(e => e.Kind);

        kinds.Should().Equal(MatchClockEventKind.PhaseWarning, MatchClockEventKind.PhaseStarted);
    }

    [TestMethod]
    public void A_command_acts_on_the_moment_it_was_typed_rather_than_the_last_tick()
    {
        MatchDirector director = NewDirector();
        director.Execute(AdminCommand.Start, TimeSpan.Zero);
        director.AdvanceTo(TimeSpan.FromMinutes(1));

        director.Execute(AdminCommand.Skip, TimeSpan.FromMinutes(4));

        director.CompletedPhases.Should().Be(1);
        director.Elapsed.Should().Be(Cycle, "the phase started now, not three minutes from now");
        director.UntilNextPhase.Should().Be(Cycle);
    }

    [TestMethod]
    public void A_director_needs_a_squeeze_to_run()
    {
        Action fixedSqueeze = () => new MatchDirector(new MatchClockSettings(Cycle, WarningLead), (ZoneSchedule)null!);
        Action perGroup = () => new MatchDirector(new MatchClockSettings(Cycle, WarningLead), (Func<int, ZoneSchedule>)null!);

        fixedSqueeze.Should().Throw<ArgumentNullException>();
        perGroup.Should().Throw<ArgumentNullException>();
    }
}
