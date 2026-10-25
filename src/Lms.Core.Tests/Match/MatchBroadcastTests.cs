using System;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What the server tells the clients.</summary>
[TestClass]
public class MatchBroadcastTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);
    private static readonly WorldPoint Second = new WorldPoint(200.5, -150, 100);

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Spawn, 1400), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(Second, 400), TimeSpan.FromSeconds(90), 8),
        });

    private static MatchDirector NewDirector() =>
        new MatchDirector(new MatchClockSettings(Cycle, WarningLead), Schedule());

    private static MatchDirector RunningDirector()
    {
        MatchDirector director = NewDirector();
        director.Execute(AdminCommand.Start, Noon);
        return director;
    }

    [TestMethod]
    public void A_broadcast_of_a_match_in_the_lobby_is_the_opening_sphere()
    {
        MatchBroadcast broadcast = NewDirector().Broadcast();

        broadcast.State.Should().Be(MatchClockState.NotStarted);
        broadcast.Group.Should().Be(1);
        broadcast.CompletedPhases.Should().Be(0);
        broadcast.TotalPhases.Should().Be(2, "the collapse is not a phase anybody wrote");
        broadcast.ZoneAt(Noon).Sphere.Should().Be(new ZoneSphere(Spawn, 1400));
        broadcast.IsLive.Should().BeFalse();
    }

    [TestMethod]
    public void A_client_reading_a_broadcast_sees_the_wall_where_the_server_does()
    {
        MatchDirector director = RunningDirector();
        TimeSpan later = Noon + Cycle + TimeSpan.FromSeconds(30);
        director.AdvanceTo(later);

        MatchBroadcast broadcast = director.Broadcast();

        broadcast.ZoneAt(later).Should().BeEquivalentTo(director.Zone);
        broadcast.CompletedPhases.Should().Be(1);
    }

    [TestMethod]
    public void A_client_whose_frame_lands_later_than_the_broadcast_keeps_the_wall_moving()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + Cycle);

        MatchBroadcast broadcast = director.Broadcast();
        TimeSpan halfway = Noon + Cycle + TimeSpan.FromSeconds(45);

        broadcast.ZoneAt(halfway).Sphere.Radius.Should().BeApproximately(1150, 1e-9);

        director.AdvanceTo(halfway);
        broadcast.ZoneAt(halfway).Should().BeEquivalentTo(director.Zone);
    }

    [TestMethod]
    public void A_paused_match_holds_the_wall_still_however_long_the_pause_lasts()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + Cycle + TimeSpan.FromSeconds(30));
        director.Execute(AdminCommand.Pause, Noon + Cycle + TimeSpan.FromSeconds(30));

        MatchBroadcast broadcast = director.Broadcast();

        broadcast.State.Should().Be(MatchClockState.Paused);
        broadcast.IsLive.Should().BeFalse();
        broadcast.ZoneAt(Noon + TimeSpan.FromHours(1)).Should()
                 .BeEquivalentTo(broadcast.ZoneAt(Noon + Cycle + TimeSpan.FromSeconds(30)));
    }

    [TestMethod]
    public void The_countdown_and_the_warning_come_off_the_clock_the_server_sent()
    {
        MatchDirector director = RunningDirector();
        MatchBroadcast broadcast = director.Broadcast();

        broadcast.UntilNextPhaseAt(Noon + TimeSpan.FromMinutes(1)).Should().Be(TimeSpan.FromMinutes(4));
        broadcast.IsWarningActiveAt(Noon + TimeSpan.FromMinutes(3)).Should().BeFalse();
        broadcast.IsWarningActiveAt(Noon + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [TestMethod]
    public void A_broadcast_with_nothing_left_to_close_has_no_countdown_in_it()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + TimeSpan.FromHours(1));

        MatchBroadcast broadcast = director.Broadcast();

        broadcast.NextPhaseAt.Should().BeNull();
        broadcast.WarningAt.Should().BeNull();
        broadcast.UntilNextPhaseAt(Noon + TimeSpan.FromHours(2)).Should().Be(TimeSpan.Zero);
        broadcast.IsWarningActiveAt(Noon + TimeSpan.FromHours(2)).Should().BeFalse();
        broadcast.IsCollapsing.Should().BeTrue();
    }

    [TestMethod]
    public void A_broadcast_survives_the_wire()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + Cycle + TimeSpan.FromSeconds(30));

        MatchBroadcast written = director.Broadcast();

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Should().Be(written);
    }

    [TestMethod]
    public void A_lobby_broadcast_survives_the_wire_with_its_absent_fields_absent()
    {
        MatchBroadcast written = NewDirector().Broadcast();

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Should().Be(written);
        read.Shrink.NextTarget.Should().Be(new ZoneSphere(Spawn, 900));
    }

    [TestMethod]
    public void A_spent_broadcast_survives_the_wire_with_no_next_phase_and_no_next_sphere()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + TimeSpan.FromHours(1));

        MatchBroadcast written = director.Broadcast();

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Should().Be(written);
        read.NextPhaseAt.Should().BeNull();
        read.Shrink.NextTarget.Should().BeNull();
    }

    [TestMethod]
    public void Nonsense_on_the_wire_is_declined_rather_than_guessed_at()
    {
        MatchBroadcast.TryDecode(string.Empty, out _).Should().BeFalse();
        MatchBroadcast.TryDecode("Goal_Aurora_Explosion", out _).Should().BeFalse();
        MatchBroadcast.TryDecode("lms-zone/1", out _).Should().BeFalse("an anchor is the one thing it cannot do without");
        MatchBroadcast.TryDecode("lms-zone/1\tanchor=LmsMatch.1.1.1.R.0.0.0", out _)
                      .Should().BeFalse("and so is the geometry");
    }

    [TestMethod]
    public void A_number_that_is_not_a_number_is_declined_rather_than_thrown()
    {
        string written = RunningDirector().Broadcast().Encode();

        foreach (string nonsense in new[] { "NaN", "Infinity", "-Infinity" })
        {
            MatchBroadcast.TryDecode($"{written}\tdamage={nonsense}", out _).Should().BeFalse(nonsense);
            MatchBroadcast.TryDecode($"{written}\tto=0,0,0,{nonsense}", out _).Should().BeFalse(nonsense);
        }

        MatchBroadcast.TryDecode(written, out _).Should().BeTrue("the line itself is fine");
    }

    [TestMethod]
    public void A_broadcast_from_a_newer_build_is_read_as_far_as_it_goes()
    {
        MatchBroadcast written = RunningDirector().Broadcast();

        MatchBroadcast.TryDecode(written.Encode() + "\tsomethingNew=7", out MatchBroadcast read).Should().BeTrue();
        read.Should().Be(written);
    }

    [TestMethod]
    public void A_broadcast_from_a_different_wire_version_is_declined()
    {
        string written = RunningDirector().Broadcast().Encode().Replace("lms-zone/1", "lms-zone/2");

        MatchBroadcast.TryDecode(written, out _).Should().BeFalse();
    }

    [TestMethod]
    public void The_status_line_says_which_group_it_is_and_where_the_wall_is()
    {
        MatchDirector director = RunningDirector();
        director.AdvanceTo(Noon + Cycle);

        MatchBroadcast broadcast = director.Broadcast();

        broadcast.StatusAt(Noon + Cycle).Should().Be(director.Status);
        broadcast.StatusAt(Noon + Cycle).Should().Contain("group 1").And.Contain("1/2").And.Contain("r1400m");
    }

    [TestMethod]
    public void The_count_and_the_destination_survive_the_wire()
    {
        MatchBroadcast written = NewDirector().Broadcast().Saying(4, "Jellyshroom Cave");

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Alive.Should().Be(4);
        read.Destination.Should().Be("Jellyshroom Cave");
        read.Should().Be(written);
    }

    [TestMethod]
    public void A_broadcast_that_says_neither_reads_as_null_rather_than_zero()
    {
        MatchBroadcast written = NewDirector().Broadcast();

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Alive.Should().BeNull();
        read.Destination.Should().BeNull();
        written.Encode().Should().NotContain("alive=").And.NotContain("dest=");
    }

    [TestMethod]
    public void A_count_of_nobody_still_travels()
    {
        MatchBroadcast written = NewDirector().Broadcast().Saying(0, null);

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Alive.Should().Be(0);
    }

    [TestMethod]
    public void A_destination_name_with_a_tab_in_it_cannot_eat_the_wall()
    {
        MatchBroadcast written = NewDirector().Broadcast().Saying(3, "Lost River,\tnear the skull");

        MatchBroadcast.TryDecode(written.Encode(), out MatchBroadcast read).Should().BeTrue();
        read.Destination.Should().Be("Lost River, near the skull");
        read.Shrink.Target.Should().Be(written.Shrink.Target);
    }

    [TestMethod]
    public void A_line_from_a_build_that_never_sent_them_still_reads()
    {
        MatchBroadcast written = NewDirector().Broadcast();
        string old = written.Encode();

        MatchBroadcast.TryDecode(old, out MatchBroadcast read).Should().BeTrue();
        read.Alive.Should().BeNull();
    }

    [TestMethod]
    public void Two_broadcasts_differing_only_in_the_count_are_not_the_same_broadcast()
    {
        MatchBroadcast four = NewDirector().Broadcast().Saying(4, null);
        MatchBroadcast three = NewDirector().Broadcast().Saying(3, null);

        four.Equals(three).Should().BeFalse();
        four.Encode().Should().NotBe(three.Encode());
    }
}
