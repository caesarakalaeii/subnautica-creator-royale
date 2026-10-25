using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The clock that drives the cull cadence.</summary>
[TestClass]
public class MatchClockTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(1);

    private static MatchClock NewClock() => new MatchClock(new MatchClockSettings(Cycle, WarningLead));

    private static MatchClock StartedClock()
    {
        MatchClock clock = NewClock();
        clock.Start();
        return clock;
    }

    [TestMethod]
    public void A_new_clock_has_not_started_and_no_time_has_passed()
    {
        MatchClock clock = NewClock();

        clock.State.Should().Be(MatchClockState.NotStarted);
        clock.Elapsed.Should().Be(TimeSpan.Zero);
        clock.CompletedPhases.Should().Be(0);
        clock.UntilNextPhase.Should().Be(Cycle);
    }

    [TestMethod]
    public void A_new_clock_has_spent_no_time_in_its_first_phase()
    {
        NewClock().SinceLastPhase.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void Before_the_first_phase_fires_the_phase_is_as_old_as_the_match()
    {
        MatchClock clock = StartedClock();

        clock.Advance(TimeSpan.FromMinutes(2));

        clock.SinceLastPhase.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void A_phase_that_has_just_fired_is_no_time_old()
    {
        MatchClock clock = StartedClock();

        clock.Advance(Cycle);

        clock.SinceLastPhase.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void The_age_of_a_phase_counts_from_the_moment_it_was_scheduled_for()
    {
        MatchClock clock = StartedClock();

        clock.Advance(Cycle + TimeSpan.FromSeconds(7));

        clock.SinceLastPhase.Should().Be(TimeSpan.FromSeconds(7));
    }

    [TestMethod]
    public void The_final_phase_keeps_ageing_after_the_last_one_has_fired()
    {
        MatchClock clock = new MatchClock(new MatchClockSettings(Cycle, WarningLead), 1);
        clock.Start();

        clock.Advance(Cycle + TimeSpan.FromMinutes(3));

        clock.SinceLastPhase.Should().Be(TimeSpan.FromMinutes(3));
    }

    [TestMethod]
    public void Time_does_not_pass_before_the_match_starts()
    {
        MatchClock clock = NewClock();

        clock.Advance(TimeSpan.FromMinutes(30)).Should().BeEmpty();

        clock.Elapsed.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void The_first_cull_falls_due_one_cycle_after_the_start()
    {
        MatchClock clock = StartedClock();

        clock.Advance(Cycle - TimeSpan.FromSeconds(1)).Should().NotContain(e => e.Kind == MatchClockEventKind.PhaseStarted);
        var events = clock.Advance(TimeSpan.FromSeconds(1));

        events.Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseStarted);
        events.Single(e => e.Kind == MatchClockEventKind.PhaseStarted).PhaseNumber.Should().Be(1);
        clock.CompletedPhases.Should().Be(1);
    }

    [TestMethod]
    public void A_warning_is_raised_once_when_its_window_opens()
    {
        MatchClock clock = StartedClock();

        clock.Advance(Cycle - WarningLead).Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseWarning);
        clock.IsWarningActive.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(10)).Should().BeEmpty();
    }

    [TestMethod]
    public void The_warning_clears_once_its_cull_has_fired()
    {
        MatchClock clock = StartedClock();

        clock.Advance(Cycle);

        clock.IsWarningActive.Should().BeFalse();
    }

    [TestMethod]
    public void A_tick_longer_than_a_cycle_does_not_lose_culls()
    {
        MatchClock clock = StartedClock();

        var events = clock.Advance(TimeSpan.FromMinutes(12));

        events.Where(e => e.Kind == MatchClockEventKind.PhaseStarted).Select(e => e.PhaseNumber)
            .Should().Equal(1, 2);
        clock.CompletedPhases.Should().Be(2);
    }

    [TestMethod]
    public void Events_come_back_in_the_order_they_happened()
    {
        MatchClock clock = StartedClock();

        var kinds = clock.Advance(TimeSpan.FromMinutes(10)).Select(e => e.Kind);

        kinds.Should().Equal(
            MatchClockEventKind.PhaseWarning, MatchClockEventKind.PhaseStarted,
            MatchClockEventKind.PhaseWarning, MatchClockEventKind.PhaseStarted);
    }

    [TestMethod]
    public void A_clock_told_where_the_match_is_fires_everything_up_to_there()
    {
        MatchClock clock = StartedClock();

        var kinds = clock.AdvanceTo(TimeSpan.FromMinutes(10)).Select(e => e.Kind);

        kinds.Should().Equal(
            MatchClockEventKind.PhaseWarning, MatchClockEventKind.PhaseStarted,
            MatchClockEventKind.PhaseWarning, MatchClockEventKind.PhaseStarted);
        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(10));
    }

    [TestMethod]
    public void A_clock_told_the_match_got_younger_stays_where_it_is()
    {
        MatchClock clock = StartedClock();
        clock.AdvanceTo(TimeSpan.FromMinutes(6));

        clock.AdvanceTo(TimeSpan.FromMinutes(5)).Should().BeEmpty();

        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(6));
        clock.CompletedPhases.Should().Be(1);
    }

    [TestMethod]
    public void An_event_is_stamped_with_the_time_it_was_scheduled_for_not_the_tick_that_noticed()
    {
        MatchClock clock = StartedClock();

        var cull = clock.Advance(TimeSpan.FromMinutes(7)).First(e => e.Kind == MatchClockEventKind.PhaseStarted);

        cull.AtElapsed.Should().Be(Cycle);
    }

    [TestMethod]
    public void Time_does_not_pass_while_paused()
    {
        MatchClock clock = StartedClock();
        clock.Advance(TimeSpan.FromMinutes(2));

        clock.Pause();
        clock.Advance(TimeSpan.FromMinutes(30)).Should().BeEmpty();

        clock.State.Should().Be(MatchClockState.Paused);
        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Resuming_carries_on_from_where_it_paused()
    {
        MatchClock clock = StartedClock();
        clock.Advance(TimeSpan.FromMinutes(2));
        clock.Pause();
        clock.Advance(TimeSpan.FromMinutes(30));

        clock.Resume();
        clock.Advance(TimeSpan.FromMinutes(3)).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);

        clock.Elapsed.Should().Be(Cycle);
    }

    [TestMethod]
    public void Resetting_puts_the_clock_back_where_it_began()
    {
        MatchClock clock = StartedClock();
        clock.Advance(TimeSpan.FromMinutes(7));

        clock.Reset();

        clock.State.Should().Be(MatchClockState.NotStarted);
        clock.Elapsed.Should().Be(TimeSpan.Zero);
        clock.CompletedPhases.Should().Be(0);
        clock.UntilNextPhase.Should().Be(Cycle);
        clock.IsWarningActive.Should().BeFalse();
    }

    [TestMethod]
    public void The_countdown_to_the_next_cull_shrinks_as_time_passes()
    {
        MatchClock clock = StartedClock();

        clock.Advance(TimeSpan.FromMinutes(2));

        clock.UntilNextPhase.Should().Be(TimeSpan.FromMinutes(3));
    }

    [TestMethod]
    public void Starting_a_running_clock_changes_nothing()
    {
        MatchClock clock = StartedClock();
        clock.Advance(TimeSpan.FromMinutes(2));

        clock.Start();

        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Pausing_a_clock_that_never_started_changes_nothing()
    {
        MatchClock clock = NewClock();

        clock.Pause();

        clock.State.Should().Be(MatchClockState.NotStarted);
    }

    [TestMethod]
    public void Time_never_runs_backwards()
    {
        MatchClock clock = StartedClock();

        Action rewind = () => clock.Advance(TimeSpan.FromSeconds(-1));

        rewind.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delta");
    }


    private static MatchClock SpentClock(int culls)
    {
        MatchClock clock = new MatchClock(new MatchClockSettings(Cycle, WarningLead), culls);
        clock.Start();
        clock.Advance(Cycle * culls);
        return clock;
    }

    [TestMethod]
    public void A_clock_told_how_many_culls_the_sequence_has_stops_at_the_last_one()
    {
        MatchClock clock = SpentClock(2);

        clock.AllPhasesFired.Should().BeTrue();
        clock.Advance(TimeSpan.FromHours(1)).Should().BeEmpty();
        clock.CompletedPhases.Should().Be(2);
    }

    [TestMethod]
    public void The_last_cull_is_not_followed_by_a_warning_for_one_that_never_comes()
    {
        MatchClock clock = SpentClock(2);

        clock.Advance(TimeSpan.FromMinutes(30)).Should().BeEmpty();
        clock.IsWarningActive.Should().BeFalse();
        clock.UntilNextPhase.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void Time_still_passes_once_the_sequence_is_spent()
    {
        MatchClock clock = SpentClock(2);

        clock.Advance(TimeSpan.FromMinutes(3));

        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(13));
    }

    [TestMethod]
    public void Resetting_gives_the_sequence_back()
    {
        MatchClock clock = SpentClock(1);

        clock.Reset();

        clock.AllPhasesFired.Should().BeFalse();
        clock.UntilNextPhase.Should().Be(Cycle);
    }

    [TestMethod]
    public void A_clock_with_no_cull_count_runs_the_cadence_for_as_long_as_the_match_lasts()
    {
        MatchClock clock = StartedClock();

        clock.Advance(TimeSpan.FromHours(1)).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);

        clock.AllPhasesFired.Should().BeFalse();
    }

    [TestMethod]
    public void A_negative_number_of_culls_is_refused()
    {
        Action build = () => new MatchClock(new MatchClockSettings(Cycle, WarningLead), -1);

        build.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("totalPhases");
    }
}
