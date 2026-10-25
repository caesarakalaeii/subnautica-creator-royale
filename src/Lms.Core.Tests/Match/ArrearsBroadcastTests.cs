using System;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The bill on the wire.</summary>
[TestClass]
public class ArrearsBroadcastTests
{
    private static ZoneDebt Debt(double health = 100, double leftAt = 20, double returnedAt = 200)
    {
        WorldPoint centre = new WorldPoint(0, -100, 0);
        ZoneSchedule schedule = new ZoneSchedule(new ZoneSphere(centre, 500), 1,
            new[] { new ZonePhase(1, new ZoneSphere(centre, 200), TimeSpan.FromSeconds(30), 10) });

        return new ZoneArrears(schedule, new ZonePolicy(TimeSpan.FromSeconds(2), 150),
                TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), new DisconnectPolicy(TimeSpan.FromSeconds(30)))
            .Of(new WorldPoint(0, -100, 700), TimeSpan.FromSeconds(leftAt),
                TimeSpan.FromSeconds(returnedAt), health);
    }

    private static ArrearsBroadcast Sent(int group = 3, int generation = 2, double health = 1000) =>
        new ArrearsBroadcast(group, generation, Debt(health));

    [TestMethod]
    public void A_bill_survives_the_round_trip()
    {
        ArrearsBroadcast sent = Sent();

        ArrearsBroadcast.TryDecode(sent.Encode(), out ArrearsBroadcast read).Should().BeTrue();

        read.Should().Be(sent);
        read.Group.Should().Be(3);
        read.Generation.Should().Be(2);
        read.Damage.Should().Be(sent.Damage);
        read.Ticks.Should().Be(sent.Ticks);
        read.Absence.Should().Be(sent.Absence);
    }

    [TestMethod]
    public void It_is_told_apart_from_the_zone_by_its_header()
    {
        string line = Sent().Encode();

        line.Should().StartWith("lms-arrears/1");
        MatchBroadcast.IsBroadcast(line).Should().BeFalse();
        ArrearsBroadcast.IsArrears(line).Should().BeTrue();
    }

    [TestMethod]
    public void A_zone_line_is_not_read_as_a_bill_and_a_bill_is_not_read_as_a_zone()
    {
        ArrearsBroadcast.TryDecode("lms-zone/1\tanchor=1.1.R.0", out _).Should().BeFalse();
        ArrearsBroadcast.IsArrears("lms-zone/1\tanchor=1.1.R.0").Should().BeFalse();
    }

    [TestMethod]
    public void A_line_missing_anything_that_decides_something_is_declined()
    {
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgroup=1\tgen=1\tticks=3\taway=1000", out _)
            .Should().BeFalse("no damage in it");
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgen=1\towed=12\tticks=3\taway=1000", out _)
            .Should().BeFalse("no group in it");
        ArrearsBroadcast.TryDecode(null, out _).Should().BeFalse();
        ArrearsBroadcast.TryDecode(string.Empty, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_bill_of_nothing_is_declined()
    {
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgroup=1\tgen=1\towed=0\tticks=0\taway=1000", out _)
            .Should().BeFalse();
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgroup=1\tgen=1\towed=-5\tticks=2\taway=1000", out _)
            .Should().BeFalse();
    }

    [TestMethod]
    public void A_key_from_a_newer_server_is_ignored_rather_than_fatal()
    {
        ArrearsBroadcast.TryDecode(
                "lms-arrears/1\tgroup=1\tgen=1\towed=12\tticks=3\taway=1000\tsomethingnew=7",
                out ArrearsBroadcast read)
            .Should().BeTrue();

        read.Damage.Should().Be(12);
    }

    [TestMethod]
    public void It_names_no_player_on_purpose()
    {
        Sent().Encode().Should().NotContain("player=");
    }

    [TestMethod]
    public void It_says_whether_the_wall_would_have_finished_them()
    {
        new ArrearsBroadcast(1, 1, Debt(health: 10)).IsFatal.Should().BeTrue();
        new ArrearsBroadcast(1, 1, Debt(health: 10_000)).IsFatal.Should().BeFalse();
    }

    [TestMethod]
    public void It_reads_as_a_sentence()
    {
        Sent(group: 3, generation: 2).ToString().Should().StartWith("group 3.2 owes ");
    }

    [TestMethod]
    public void A_bill_for_a_match_that_is_not_running_is_refused_by_the_reader()
    {
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgroup=0\tgen=1\towed=12\tticks=3\taway=1000", out _)
            .Should().BeFalse();
        ArrearsBroadcast.TryDecode("lms-arrears/1\tgroup=1\tgen=0\towed=12\tticks=3\taway=1000", out _)
            .Should().BeFalse();
    }
}
