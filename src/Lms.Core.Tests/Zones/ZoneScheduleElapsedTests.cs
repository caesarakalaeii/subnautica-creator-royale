using System;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>The wall at an elapsed match time, which must agree with <see cref="MatchClock"/>.</summary>
[TestClass]
public class ZoneScheduleElapsedTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -100, 0);
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(60);

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Centre, 500), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Centre, 300), TimeSpan.FromSeconds(30), 3),
            new ZonePhase(2, new ZoneSphere(Centre, 100), TimeSpan.FromSeconds(30), 8),
        });

    [TestMethod]
    public void Nothing_has_fired_before_the_first_cycle_is_up()
    {
        ZoneSchedule schedule = Schedule();

        schedule.PhasesFiredBy(TimeSpan.Zero, Cycle, Cycle).Should().Be(0);
        schedule.PhasesFiredBy(TimeSpan.FromSeconds(59.999), Cycle, Cycle).Should().Be(0);
        schedule.AtElapsed(TimeSpan.FromSeconds(30), Cycle, Cycle).Sphere.Radius.Should().Be(500);
    }

    [TestMethod]
    public void A_phase_has_fired_the_moment_its_cycle_is_up()
    {
        ZoneSchedule schedule = Schedule();

        schedule.PhasesFiredBy(Cycle, Cycle, Cycle).Should().Be(1);
        schedule.PhasesFiredBy(TimeSpan.FromSeconds(150), Cycle, Cycle).Should().Be(2);
    }

    [TestMethod]
    public void A_phase_that_has_just_fired_has_not_moved_the_wall_yet()
    {
        ZoneSchedule schedule = Schedule();

        schedule.AtElapsed(Cycle, Cycle, Cycle).Sphere.Radius.Should().Be(500);
        schedule.AtElapsed(Cycle + TimeSpan.FromSeconds(30), Cycle, Cycle).Sphere.Radius.Should().Be(300);
    }

    [TestMethod]
    public void The_schedule_runs_out_rather_than_running_on()
    {
        ZoneSchedule schedule = Schedule();

        schedule.PhasesFiredBy(TimeSpan.FromHours(1), Cycle, Cycle).Should().Be(schedule.PhaseCount);
        schedule.AtElapsed(TimeSpan.FromHours(1), Cycle, Cycle).Sphere.Radius.Should().Be(0);
    }

    [TestMethod]
    public void A_cycle_of_no_time_is_refused_rather_than_dividing_by_nothing()
    {
        Action never = () => Schedule().PhasesFiredBy(TimeSpan.FromSeconds(1), TimeSpan.Zero, Cycle);

        never.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Match_time_does_not_run_backwards()
    {
        Action backwards = () => Schedule().AtElapsed(TimeSpan.FromSeconds(-1), Cycle, Cycle);

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    [DataRow(60)]
    [DataRow(150)]
    public void The_running_clock_and_the_arithmetic_never_disagree(int openingSeconds)
    {
        ZoneSchedule schedule = Schedule();
        TimeSpan opening = TimeSpan.FromSeconds(openingSeconds);
        MatchClockSettings clock = new MatchClockSettings(Cycle, TimeSpan.FromSeconds(15), TimeSpan.Zero, opening);
        MatchDirector director = new MatchDirector(clock, schedule);

        director.Execute(AdminCommand.Start, TimeSpan.Zero);

        for (int second = 1; second <= 360; second++)
        {
            director.AdvanceTo(TimeSpan.FromSeconds(second));

            director.CompletedPhases.Should().Be(
                schedule.PhasesFiredBy(director.Elapsed, Cycle, opening),
                $"the clock and the schedule have to agree at {second}s");
            director.Zone.Sphere.Should().Be(
                schedule.AtElapsed(director.Elapsed, Cycle, opening).Sphere,
                $"the wall has to be in one place at {second}s");
        }
    }

    [TestMethod]
    [DataRow(60)]
    [DataRow(150)]
    public void A_skipped_phase_leaves_the_two_agreeing(int openingSeconds)
    {
        ZoneSchedule schedule = Schedule();
        TimeSpan opening = TimeSpan.FromSeconds(openingSeconds);
        MatchClockSettings clock = new MatchClockSettings(Cycle, TimeSpan.FromSeconds(15), TimeSpan.Zero, opening);
        MatchDirector director = new MatchDirector(clock, schedule);

        director.Execute(AdminCommand.Start, TimeSpan.Zero);
        director.Execute(AdminCommand.Skip, TimeSpan.FromSeconds(10));

        for (int second = 11; second <= 300; second++)
        {
            director.AdvanceTo(TimeSpan.FromSeconds(second));

            director.CompletedPhases.Should().Be(schedule.PhasesFiredBy(director.Elapsed, Cycle, opening));
            director.Zone.Sphere.Should().Be(schedule.AtElapsed(director.Elapsed, Cycle, opening).Sphere);
        }
    }
}
