using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The warning before a phase repeats and gets louder as the phase approaches.</summary>
[TestClass]
public class PhaseAlarmTests
{
    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(60);

    private static PhaseAlarm NewAlarm(double leadSeconds = 60) =>
        new PhaseAlarm(TimeSpan.FromSeconds(leadSeconds));

    private static List<AlarmLevel> Countdown(PhaseAlarm alarm, int phase, double from, double to = 0)
    {
        List<AlarmLevel> said = new List<AlarmLevel>();

        for (double left = from; left >= to; left -= 1)
        {
            AlarmLevel? level = alarm.Due(phase, TimeSpan.FromSeconds(left));
            if (level.HasValue)
            {
                said.Add(level.Value);
            }
        }

        return said;
    }

    [TestMethod]
    public void Nothing_is_said_outside_the_warning_window()
    {
        PhaseAlarm alarm = NewAlarm(30);

        alarm.Due(1, TimeSpan.FromSeconds(90)).Should().BeNull();
        alarm.Due(1, TimeSpan.FromSeconds(31)).Should().BeNull();
        alarm.Due(1, TimeSpan.FromSeconds(30)).Should().NotBeNull();
    }

    [TestMethod]
    public void A_warning_gets_louder_as_the_wall_gets_closer()
    {
        List<AlarmLevel> said = Countdown(NewAlarm(), 1, 60);

        said.Should().NotBeEmpty();
        said.Should().BeInAscendingOrder();
        said.First().Should().Be(AlarmLevel.Notice);
        said.Last().Should().Be(AlarmLevel.Immediate);
    }

    [TestMethod]
    public void The_same_mark_is_never_said_twice()
    {
        PhaseAlarm alarm = NewAlarm();

        alarm.Due(1, TimeSpan.FromSeconds(60)).Should().NotBeNull();
        alarm.Due(1, TimeSpan.FromSeconds(60)).Should().BeNull();
        alarm.Due(1, TimeSpan.FromSeconds(59)).Should().BeNull();
    }

    [TestMethod]
    public void A_long_frame_says_the_nearest_mark_rather_than_all_of_them()
    {
        PhaseAlarm alarm = NewAlarm();

        alarm.Due(1, TimeSpan.FromSeconds(2)).Should().Be(AlarmLevel.Immediate);
        alarm.Due(1, TimeSpan.FromSeconds(2)).Should().BeNull();

        Countdown(alarm, 1, 1).Should().HaveCount(1);
    }

    [TestMethod]
    public void A_new_phase_arms_the_whole_ladder_again()
    {
        PhaseAlarm alarm = NewAlarm();
        Countdown(alarm, 1, 60).Should().NotBeEmpty();

        Countdown(alarm, 2, 60).Should().NotBeEmpty();
    }

    [TestMethod]
    public void A_short_lead_never_promises_more_time_than_there_is()
    {
        List<AlarmLevel> said = Countdown(NewAlarm(10), 1, 10);

        said.Should().NotBeEmpty();
        said.First().Should().NotBe(AlarmLevel.Notice);
    }

    [TestMethod]
    public void A_wall_that_has_already_moved_is_not_a_warning()
    {
        PhaseAlarm alarm = NewAlarm();

        alarm.Due(1, TimeSpan.Zero).Should().NotBeNull();
        alarm.Due(1, TimeSpan.FromSeconds(-1)).Should().BeNull();
    }

    [TestMethod]
    public void Every_level_is_reached_by_a_full_countdown()
    {
        List<AlarmLevel> said = Countdown(NewAlarm(), 1, 60);

        said.Should().Contain(AlarmLevel.Notice);
        said.Should().Contain(AlarmLevel.Urgent);
        said.Should().Contain(AlarmLevel.Immediate);
    }

    [TestMethod]
    public void The_seconds_a_mark_was_for_come_back_with_it()
    {
        PhaseAlarm alarm = NewAlarm(10);

        alarm.Due(1, TimeSpan.FromSeconds(9.7), out int seconds).Should().NotBeNull();
        seconds.Should().Be(10);
    }

    [TestMethod]
    public void A_full_countdown_says_one_thing_per_mark_and_no_more()
    {
        Countdown(NewAlarm(), 1, 60).Should().HaveCount(8);
    }

    [TestMethod]
    public void A_lead_of_nothing_is_refused()
    {
        Action nothing = () => new PhaseAlarm(TimeSpan.Zero);

        nothing.Should().Throw<ArgumentOutOfRangeException>();
    }
}
