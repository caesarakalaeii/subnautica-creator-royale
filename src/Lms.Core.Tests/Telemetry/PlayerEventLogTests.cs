using System;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The client's bounded queue of events that happened since it last reported.</summary>
[TestClass]
public class PlayerEventLogTests
{
    private static PlayerEvent Bite(double seconds, double damage = 3) =>
        new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(seconds), amount: damage);

    [TestMethod]
    public void An_empty_log_has_nothing_to_send()
    {
        PlayerEventLog log = new PlayerEventLog();

        log.Take(out PlayerEventBatch batch).Should().BeFalse();
        batch.Should().BeNull();
    }

    [TestMethod]
    public void What_went_in_comes_out_once_in_the_order_it_happened()
    {
        PlayerEventLog log = new PlayerEventLog();

        log.Add(2, Bite(10));
        log.Add(2, Bite(12));

        log.Take(out PlayerEventBatch batch).Should().BeTrue();
        batch.Generation.Should().Be(2);
        batch.Events.Should().HaveCount(2);
        batch.Events[0].Elapsed.Should().Be(TimeSpan.FromSeconds(10));

        log.Take(out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_new_generation_throws_away_the_last_one_and_says_how_much()
    {
        PlayerEventLog log = new PlayerEventLog();

        log.Add(2, Bite(10));
        log.Add(2, Bite(12));
        log.Add(3, Bite(1));

        log.Take(out PlayerEventBatch batch).Should().BeTrue();
        batch.Generation.Should().Be(3);
        batch.Events.Should().HaveCount(1);
        batch.Lost.Should().Be(2);
    }

    [TestMethod]
    public void A_loss_is_reported_once_and_then_forgotten()
    {
        PlayerEventLog log = new PlayerEventLog();

        log.Add(2, Bite(10));
        log.Add(3, Bite(1));
        log.Take(out PlayerEventBatch first).Should().BeTrue();
        first.Lost.Should().Be(1);

        log.Add(3, Bite(3));
        log.Take(out PlayerEventBatch second).Should().BeTrue();
        second.Lost.Should().Be(0);
    }

    [TestMethod]
    public void A_log_nothing_is_draining_drops_the_oldest_rather_than_growing()
    {
        PlayerEventLog log = new PlayerEventLog(capacity: 3);

        for (int i = 1; i <= 5; i++)
        {
            log.Add(1, Bite(i));
        }

        log.Take(out PlayerEventBatch batch).Should().BeTrue();
        batch.Events.Should().HaveCount(3);
        batch.Events[0].Elapsed.Should().Be(TimeSpan.FromSeconds(3));
        batch.Lost.Should().Be(2);
    }

    [TestMethod]
    public void Nothing_pending_and_a_new_generation_is_not_a_loss()
    {
        PlayerEventLog log = new PlayerEventLog();

        log.Add(1, Bite(1));
        log.Take(out _).Should().BeTrue();

        log.Add(2, Bite(1));
        log.Take(out PlayerEventBatch batch).Should().BeTrue();
        batch.Lost.Should().Be(0);
    }

    [TestMethod]
    public void A_capacity_of_nothing_is_refused()
    {
        Action nothing = () => new PlayerEventLog(capacity: 0);

        nothing.Should().Throw<ArgumentOutOfRangeException>();
    }
}
