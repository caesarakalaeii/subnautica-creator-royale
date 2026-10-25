using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>A round is a ten-minute opening and then thirty minutes of shrinking, which the clock could not express.</summary>
[TestClass]
public class MatchClockOpeningTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Opening = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lead = TimeSpan.FromMinutes(1);

    private static MatchClockSettings Settings(TimeSpan? opening = null) =>
        new MatchClockSettings(Cycle, Lead, MatchClockSettings.DefaultVictoryWindow, opening ?? Opening);

    private static MatchClock Started(int? phases = 6)
    {
        MatchClock clock = new MatchClock(Settings(), phases);
        clock.Start();
        return clock;
    }

    [TestMethod]
    public void An_opening_that_is_not_named_is_the_cycle()
    {
        new MatchClockSettings(Cycle, Lead).OpeningLength.Should().Be(Cycle);
        new MatchClockSettings(Cycle, Lead, TimeSpan.Zero, null).OpeningLength.Should().Be(Cycle);
    }

    [TestMethod]
    public void The_first_phase_waits_the_opening_and_the_second_waits_a_cycle()
    {
        MatchClock clock = Started();

        clock.UntilNextPhase.Should().Be(Opening, "nothing has fired, so the wall is still waiting out the opening");

        clock.Advance(Opening).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);
        clock.CompletedPhases.Should().Be(1);

        clock.UntilNextPhase.Should().Be(Cycle, "the opening is spent and every window after it is a cycle");

        clock.Advance(Cycle).Should().Contain(e => e.Kind == MatchClockEventKind.PhaseStarted);
        clock.CompletedPhases.Should().Be(2);
    }

    [TestMethod]
    public void A_cycle_into_the_opening_is_not_a_phase()
    {
        MatchClock clock = Started();

        clock.Advance(Cycle).Should().NotContain(e => e.Kind == MatchClockEventKind.PhaseStarted);
        clock.CompletedPhases.Should().Be(0);
    }

    [TestMethod]
    public void The_age_of_the_opening_phase_is_the_whole_match_so_far()
    {
        MatchClock clock = Started();

        clock.Advance(TimeSpan.FromMinutes(7));

        clock.SinceLastPhase.Should().Be(TimeSpan.FromMinutes(7));
    }

    [TestMethod]
    public void The_age_of_a_later_phase_is_measured_from_the_opening_plus_the_cycles_before_it()
    {
        MatchClock clock = Started();

        clock.Advance(Opening);
        clock.Advance(TimeSpan.FromMinutes(2));
        clock.SinceLastPhase.Should().Be(TimeSpan.FromMinutes(2), "two minutes into the first cycle");

        clock.Advance(TimeSpan.FromMinutes(3));
        clock.Advance(TimeSpan.FromMinutes(1));
        clock.SinceLastPhase.Should().Be(TimeSpan.FromMinutes(1), "one minute into the second cycle");
    }

    [TestMethod]
    public void The_first_warning_is_a_lead_before_the_opening_ends()
    {
        MatchClock clock = Started();

        clock.Advance(Opening - Lead - TimeSpan.FromSeconds(1))
            .Should().NotContain(e => e.Kind == MatchClockEventKind.PhaseWarning);

        clock.Advance(TimeSpan.FromSeconds(1))
            .Should().Contain(e => e.Kind == MatchClockEventKind.PhaseWarning);
    }

    [TestMethod]
    public void A_reset_puts_the_opening_back_rather_than_a_cycle()
    {
        MatchClock clock = Started();
        clock.Advance(Opening + Cycle);

        clock.Reset();

        clock.CompletedPhases.Should().Be(0);
        clock.UntilNextPhase.Should().Be(Opening, "a replayed group gets the same opening the first one had");
        clock.SinceLastPhase.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_ten_and_thirty_round_is_six_phases_and_forty_minutes_wall_to_wall()
    {
        MatchClock clock = Started(phases: 6);

        List<TimeSpan> fired = new List<TimeSpan>();
        for (int minute = 0; minute < 45; minute++)
        {
            fired.AddRange(clock.Advance(TimeSpan.FromMinutes(1))
                .Where(e => e.Kind == MatchClockEventKind.PhaseStarted)
                .Select(e => e.AtElapsed));
        }

        fired.Should().Equal(
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(35));
        clock.AllPhasesFired.Should().BeTrue("six phases is the whole schedule");
    }

    [TestMethod]
    public void One_delta_that_covers_the_whole_opening_and_more_loses_no_phase()
    {
        MatchClock clock = Started();

        int phases = clock.Advance(Opening + Cycle + Cycle)
            .Count(e => e.Kind == MatchClockEventKind.PhaseStarted);

        phases.Should().Be(3);
        clock.CompletedPhases.Should().Be(3);
    }

    [TestMethod]
    public void An_opening_shorter_than_the_warning_it_would_have_to_give_is_refused()
    {
        Action tooShort = () => new MatchClockSettings(
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2),
            MatchClockSettings.DefaultVictoryWindow, TimeSpan.FromMinutes(1));

        tooShort.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("warningLead",
                "it is the lead an operator has to change, not the opening they meant");
    }

    [TestMethod]
    public void An_opening_of_no_length_is_refused()
    {
        Action nothing = () => new MatchClockSettings(
            Cycle, Lead, MatchClockSettings.DefaultVictoryWindow, TimeSpan.Zero);

        nothing.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("openingLength");
    }
}
