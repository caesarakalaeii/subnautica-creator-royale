using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>When the booth is pushed to and what is sent: cadence, backlog and refusal handling.</summary>
[TestClass]
public class TelemetryPumpTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 24, 12, 34, 56, 789, TimeSpan.Zero);

    private static TelemetryPolicy Booth(double intervalSeconds = 1) =>
        new TelemetryPolicy("http://booth:8080/ingest", TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(5));

    private static TelemetryPump NewPump(double intervalSeconds = 1) => new TelemetryPump(Booth(intervalSeconds));

    private static TelemetrySnapshot Take(TelemetryPump pump, DateTimeOffset now, AuditLog? log = null) =>
        pump.Take(now, null, null, new PlayerTelemetry[0], log ?? new AuditLog());

    private static AuditLog Log(int records)
    {
        AuditLog log = new AuditLog();
        for (int i = 0; i < records; i++)
        {
            log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(i)), AuditRecordKind.PhaseStarted, phase: 1);
        }

        return log;
    }

    [TestMethod]
    public void A_position_sample_is_written_down_and_never_pushed()
    {
        AuditLog log = new AuditLog();
        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(1)), AuditRecordKind.MatchStarted);
        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(2)), AuditRecordKind.PlayerPosition,
            player: "Vela");
        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(3)), AuditRecordKind.ZoneBite,
            player: "Vela", amount: 3);

        TelemetrySnapshot snapshot = Take(NewPump(), Noon, log);

        snapshot.Events.Select(sent => sent.Kind)
            .Should().Equal(AuditRecordKind.MatchStarted, AuditRecordKind.ZoneBite);
    }

    [TestMethod]
    public void A_run_of_position_samples_does_not_leave_the_cursor_behind()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = new AuditLog();

        for (int i = 0; i < 10; i++)
        {
            log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(i)), AuditRecordKind.PlayerPosition,
                player: "Vela");
        }

        Take(pump, Noon, log).Events.Should().BeEmpty();
        pump.Accepted();

        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(11)), AuditRecordKind.ZoneBite,
            player: "Vela", amount: 3);

        TelemetrySnapshot next = Take(pump, Noon + Second, log);

        next.Events.Should().ContainSingle();
        next.Events[0].Sequence.Should().Be(11);
    }

    [TestMethod]
    public void A_push_of_nothing_but_samples_still_moves_the_cursor_on()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = new AuditLog();
        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(1)), AuditRecordKind.PlayerPosition,
            player: "Vela");

        Take(pump, Noon, log).Events.Should().BeEmpty();
        pump.Accepted();

        pump.Acknowledged.Should().Be(1);
    }

    [TestMethod]
    public void A_tournament_with_no_booth_named_never_pushes()
    {
        TelemetryPump pump = new TelemetryPump(TelemetryPolicy.Off);

        pump.IsDue(Noon).Should().BeFalse();
        pump.IsDue(Noon + TimeSpan.FromHours(1)).Should().BeFalse();
    }

    [TestMethod]
    public void The_first_push_is_due_as_soon_as_the_server_has_a_match()
    {
        NewPump().IsDue(Noon).Should().BeTrue();
    }

    [TestMethod]
    public void Nothing_is_pushed_again_until_the_cadence_comes_round()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);
        pump.Accepted();

        pump.IsDue(Noon + TimeSpan.FromMilliseconds(900)).Should().BeFalse();
        pump.IsDue(Noon + Second).Should().BeTrue();
    }

    [TestMethod]
    public void A_timer_that_fires_a_hair_early_still_feeds_the_booth_at_the_rate_asked_for()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);
        pump.Accepted();

        pump.IsDue(Noon + TimeSpan.FromMilliseconds(995)).Should().BeTrue();
    }

    [TestMethod]
    public void A_push_still_in_flight_is_not_joined_by_a_second_one()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);

        pump.IsDue(Noon + Second).Should().BeFalse();
        pump.IsDue(Noon + TimeSpan.FromSeconds(3)).Should().BeFalse();
    }

    [TestMethod]
    public void A_push_nobody_ever_answers_is_abandoned_rather_than_stopping_the_feed_for_the_weekend()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);

        pump.IsDue(Noon + TimeSpan.FromSeconds(5)).Should().BeFalse();
        pump.IsDue(Noon + TimeSpan.FromSeconds(6)).Should().BeTrue();
    }

    [TestMethod]
    public void A_booth_that_refuses_is_backed_off_from_rather_than_hammered()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);
        pump.Refused();

        pump.IsDue(Noon + Second).Should().BeFalse();
        pump.IsDue(Noon + TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [TestMethod]
    public void A_booth_that_stays_down_is_asked_less_and_less_often_up_to_half_a_minute()
    {
        TelemetryPump pump = NewPump();

        DateTimeOffset now = Noon;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Take(pump, now);
            pump.Refused();
            now += TimeSpan.FromMinutes(1);
        }

        Take(pump, now);
        pump.Refused();

        pump.IsDue(now + TimeSpan.FromSeconds(28)).Should().BeFalse();
        pump.IsDue(now + TimeSpan.FromSeconds(30)).Should().BeTrue();
    }

    [TestMethod]
    public void A_booth_that_comes_back_is_pushed_at_on_the_cadence_again()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);
        pump.Refused();
        Take(pump, Noon + TimeSpan.FromSeconds(2));
        pump.Accepted();

        pump.IsDue(Noon + TimeSpan.FromSeconds(3)).Should().BeTrue();
    }

    [TestMethod]
    public void A_push_carries_the_match_the_zone_and_everybody_in_it()
    {
        TelemetryPump pump = NewPump();
        MatchTelemetry match = new MatchTelemetry(2, 1, MatchClockState.Running, TimeSpan.FromMinutes(2),
            1, 4, TimeSpan.FromSeconds(30), 3);
        ZoneTelemetry zone = new ZoneTelemetry(new SphereTelemetry(0, -10, 0, 1400), null, 3);
        PlayerTelemetry[] players =
        {
            new PlayerTelemetry("Vela", true, new WorldPoint(1, -2, 3), null, 92, 40, 70, 65, null, null, true, 0)
        };

        TelemetrySnapshot snapshot = pump.Take(Noon, match, zone, players, new AuditLog());

        snapshot.At.Should().Be(Noon);
        snapshot.Match.Should().BeSameAs(match);
        snapshot.Zone.Should().BeSameAs(zone);
        snapshot.Players.Should().HaveCount(1);
    }

    [TestMethod]
    public void A_push_carries_everything_the_log_has_that_the_booth_has_not_taken()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = Log(3);

        TelemetrySnapshot snapshot = Take(pump, Noon, log);

        snapshot.Events.Should().HaveCount(3);
        snapshot.Events[0].Sequence.Should().Be(1);
        snapshot.Events[2].Sequence.Should().Be(3);
    }

    [TestMethod]
    public void Events_the_booth_has_taken_are_not_sent_again()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = Log(3);
        Take(pump, Noon, log);
        pump.Accepted();

        log.Append(At, new MatchStamp(1, 1, TimeSpan.FromSeconds(9)), AuditRecordKind.ZoneClosed);

        TelemetrySnapshot second = Take(pump, Noon + Second, log);

        second.Events.Should().HaveCount(1);
        second.Events[0].Sequence.Should().Be(4);
    }

    [TestMethod]
    public void Events_from_a_push_that_did_not_land_are_sent_again()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = Log(3);
        Take(pump, Noon, log);
        pump.Refused();

        TelemetrySnapshot second = Take(pump, Noon + TimeSpan.FromSeconds(2), log);

        second.Events.Should().HaveCount(3);
        second.Events[0].Sequence.Should().Be(1);
    }

    [TestMethod]
    public void A_long_outage_drains_in_pushes_the_booth_will_accept_rather_than_one_it_refuses()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = Log(1200);

        TelemetrySnapshot first = Take(pump, Noon, log);

        first.Events.Should().HaveCount(500);
        first.Events[0].Sequence.Should().Be(1);
        first.Events[499].Sequence.Should().Be(500);

        pump.Accepted();

        TelemetrySnapshot second = Take(pump, Noon + Second, log);

        second.Events[0].Sequence.Should().Be(501);
    }

    [TestMethod]
    public void Taking_a_push_from_a_pump_that_is_not_due_is_refused_rather_than_sent()
    {
        TelemetryPump pump = NewPump();
        Take(pump, Noon);

        Action again = () => Take(pump, Noon + TimeSpan.FromMilliseconds(100));

        again.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Answering_a_push_that_was_never_taken_is_ignored_rather_than_moving_the_cursor()
    {
        TelemetryPump pump = NewPump();
        AuditLog log = Log(2);

        pump.Accepted();

        Take(pump, Noon, log).Events.Should().HaveCount(2);
    }
}
