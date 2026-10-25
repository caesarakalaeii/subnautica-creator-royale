using System;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The wire encoding of a client's event batch.</summary>
[TestClass]
public class PlayerEventBatchTests
{
    private static readonly WorldPoint Somewhere = new WorldPoint(120.5, -42.25, 300.125);

    private static PlayerEventBatch Decoded(PlayerEventBatch batch)
    {
        PlayerEventBatch.TryDecode(batch.Encode(), out PlayerEventBatch read).Should().BeTrue();

        return read;
    }

    [TestMethod]
    public void A_bite_survives_the_wire_with_everything_an_argument_needs()
    {
        PlayerEventBatch sent = new PlayerEventBatch(3, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(125.5), Somewhere,
                "kelpForest", amount: 3, health: 88.5, phase: 2)
        });

        PlayerEventBatch read = Decoded(sent);

        read.Generation.Should().Be(3);
        read.Events.Should().HaveCount(1);

        PlayerEvent bite = read.Events[0];
        bite.Kind.Should().Be(AuditRecordKind.ZoneBite);
        bite.Elapsed.Should().Be(TimeSpan.FromSeconds(125.5));
        bite.Position.Should().Be(Somewhere);
        bite.Biome.Should().Be("kelpForest");
        bite.Amount.Should().Be(3);
        bite.Health.Should().Be(88.5);
        bite.Phase.Should().Be(2);
    }

    [TestMethod]
    public void Several_events_keep_the_order_they_happened_in()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(1, new[]
        {
            new PlayerEvent(AuditRecordKind.RingFired, TimeSpan.FromSeconds(10)),
            new PlayerEvent(AuditRecordKind.PlayerCaged, TimeSpan.FromSeconds(11)),
            new PlayerEvent(AuditRecordKind.PlayerReleased, TimeSpan.FromSeconds(31))
        }));

        read.Events.Should().HaveCount(3);
        read.Events[0].Kind.Should().Be(AuditRecordKind.RingFired);
        read.Events[1].Kind.Should().Be(AuditRecordKind.PlayerCaged);
        read.Events[2].Kind.Should().Be(AuditRecordKind.PlayerReleased);
    }

    [TestMethod]
    public void An_event_with_nothing_optional_in_it_still_reads_back()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(0, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneWarned, TimeSpan.Zero)
        }));

        PlayerEvent warning = read.Events[0];
        warning.Position.Should().BeNull();
        warning.Biome.Should().BeNull();
        warning.Amount.Should().BeNull();
        warning.Health.Should().BeNull();
        warning.Phase.Should().BeNull();
        warning.Detail.Should().BeNull();
    }

    [TestMethod]
    public void A_client_says_its_world_is_ready_and_the_wire_carries_it()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(1, new[]
        {
            new PlayerEvent(AuditRecordKind.PlayerReady, TimeSpan.FromSeconds(40))
        }));

        read.Events.Should().HaveCount(1);
        read.Events[0].Kind.Should().Be(AuditRecordKind.PlayerReady);
        read.Events[0].Elapsed.Should().Be(TimeSpan.FromSeconds(40));
    }

    [TestMethod]
    public void A_snapshot_is_not_a_batch_and_a_batch_is_not_a_snapshot()
    {
        string snapshot = new PlayerReport(true, "kelpForest", null, null, null).Encode();

        PlayerEventBatch.TryDecode(snapshot, out _).Should().BeFalse();

        string batch = new PlayerEventBatch(1, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(1), amount: 1)
        }).Encode();

        PlayerReport.TryDecode(batch, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Nothing_and_nonsense_are_refused_rather_than_read_as_an_empty_batch()
    {
        PlayerEventBatch.TryDecode(null, out _).Should().BeFalse();
        PlayerEventBatch.TryDecode(string.Empty, out _).Should().BeFalse();
        PlayerEventBatch.TryDecode("lms-events/2\tgen=1", out _).Should().BeFalse();
        PlayerEventBatch.TryDecode("who knows", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_field_this_build_has_never_heard_of_is_ignored_rather_than_fatal()
    {
        PlayerEventBatch.TryDecode(
            "lms-events/1\tgen=4\tsomethingelse=x\nk=ZoneBite\tt=1000\ta=3\twhatever=9",
            out PlayerEventBatch read).Should().BeTrue();

        read.Generation.Should().Be(4);
        read.Events.Should().HaveCount(1);
        read.Events[0].Amount.Should().Be(3);
    }

    [TestMethod]
    public void A_kind_a_client_may_not_announce_is_dropped_from_the_wire()
    {
        PlayerEventBatch.TryDecode(
            "lms-events/1\tgen=1\nk=MatchEnded\tt=1000\nk=KillAttributed\tt=1000\nk=ZoneBite\tt=1000\ta=1",
            out PlayerEventBatch read).Should().BeTrue();

        read.Events.Should().HaveCount(1);
        read.Events[0].Kind.Should().Be(AuditRecordKind.ZoneBite);
    }

    [TestMethod]
    public void An_event_with_no_kind_at_all_is_dropped()
    {
        PlayerEventBatch.TryDecode("lms-events/1\tgen=1\nt=1000\ta=3\nk=ZoneBite\tt=2000",
            out PlayerEventBatch read).Should().BeTrue();

        read.Events.Should().HaveCount(1);
        read.Events[0].Elapsed.Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void A_kind_the_client_may_not_announce_is_refused_at_the_constructor_too()
    {
        Action written = () => new PlayerEvent(AuditRecordKind.PlayerEliminated, TimeSpan.Zero);

        written.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void A_name_carrying_a_separator_cannot_eat_the_rest_of_the_batch()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(1, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(1), amount: 1,
                biome: "kelp\nforest\tdeep", detail: "a\rb"),
            new PlayerEvent(AuditRecordKind.RingFired, TimeSpan.FromSeconds(2))
        }));

        read.Events.Should().HaveCount(2);
        read.Events[0].Biome.Should().Be("kelp forest deep");
        read.Events[0].Detail.Should().Be("a b");
        read.Events[1].Kind.Should().Be(AuditRecordKind.RingFired);
    }

    [TestMethod]
    public void Everything_measured_is_kept_to_the_precision_a_record_keeps()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(1, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromTicks(12345678),
                new WorldPoint(1.00049, 2.0009, 3.9999), amount: 0.12349, health: 99.98765)
        }));

        PlayerEvent bite = read.Events[0];
        bite.Elapsed.Should().Be(TimeSpan.FromMilliseconds(1234));
        bite.Position!.Value.X.Should().BeApproximately(1, 0.0005);
        bite.Amount.Should().BeApproximately(0.123, 0.0005);
        bite.Health.Should().BeApproximately(99.987, 0.0005);
    }

    [TestMethod]
    public void A_batch_that_had_to_drop_events_says_how_many_out_loud()
    {
        PlayerEventBatch read = Decoded(new PlayerEventBatch(2, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(1), amount: 1)
        }, lost: 7));

        read.Lost.Should().Be(7);
    }

    [TestMethod]
    public void An_empty_batch_is_never_encoded_and_never_sent()
    {
        Action empty = () => new PlayerEventBatch(1, new PlayerEvent[0]);

        empty.Should().Throw<ArgumentException>();
    }
}
