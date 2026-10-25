using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The last step of a client's event.</summary>
[TestClass]
public class MatchHostClientEventTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(200, -15, 100), 1400);

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static PlayerReading Reading(ushort id = 1, string name = "Vela") =>
        new PlayerReading(id, name, new WorldPoint(200, -15, 100), true, 92, 40, 70, 65);

    private static IReadOnlyList<AuditRecord> Recorded(MatchHost host, PlayerEventBatch batch)
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Update(new[] { Reading() }, Wall);
        roster.Reported(1, batch);

        host.Record(roster.Update(new[] { Reading() }, Wall).Changes);

        return host.Audit.Records.Where(record => PlayerEvent.CanBeReported(record.Kind)).ToList();
    }

    private static PlayerEventBatch Batch(int generation, params PlayerEvent[] events) =>
        new PlayerEventBatch(generation, events);

    [TestMethod]
    public void A_bite_a_client_saw_becomes_a_record_with_everything_on_it()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        IReadOnlyList<AuditRecord> recorded = Recorded(host, Batch(host.Generation,
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(90),
                new WorldPoint(1500, -20, 100), "kelpForest", amount: 3, health: 88, phase: 2)));

        AuditRecord bite = recorded.Should().ContainSingle().Subject;

        bite.Kind.Should().Be(AuditRecordKind.ZoneBite);
        bite.Player.Should().Be("Vela");
        bite.Elapsed.Should().Be(TimeSpan.FromSeconds(90));
        bite.Amount.Should().Be(3);
        bite.Health.Should().Be(88);
        bite.Phase.Should().Be(2);
        bite.Biome.Should().Be("kelpForest");
        bite.Position!.Value.X.Should().Be(1500);
        bite.Group.Should().Be(host.Group);
        bite.Generation.Should().Be(host.Generation);
    }

    [TestMethod]
    public void An_event_is_stamped_with_the_match_time_the_client_saw_it_at()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Advance(Noon + TimeSpan.FromSeconds(120));

        IReadOnlyList<AuditRecord> recorded = Recorded(host, Batch(host.Generation,
            new PlayerEvent(AuditRecordKind.RingFired, TimeSpan.FromSeconds(5))));

        recorded.Should().ContainSingle().Which.Elapsed.Should().Be(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void An_event_from_a_match_that_has_been_reset_is_dropped()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        int before = host.Generation;
        host.Command("reset", Noon + TimeSpan.FromSeconds(30));
        host.Generation.Should().NotBe(before);

        Recorded(host, Batch(before,
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(10), amount: 3)))
            .Should().BeEmpty();
    }

    [TestMethod]
    public void An_event_from_a_client_that_has_not_been_told_about_the_match_yet_is_dropped()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Recorded(host, Batch(0, new PlayerEvent(AuditRecordKind.RingFired, TimeSpan.FromSeconds(1))))
            .Should().BeEmpty();
    }

    [TestMethod]
    public void A_derived_change_is_never_fenced_on_a_generation()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        PlayerRoster roster = new PlayerRoster();
        host.Record(roster.Update(new[] { Reading() }, Wall).Changes);

        host.Audit.Records.Select(record => record.Kind).Should().Contain(AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void One_record_per_bite_and_never_a_run_added_up()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        IReadOnlyList<AuditRecord> recorded = Recorded(host, Batch(host.Generation,
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(10), amount: 3, health: 97),
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(12), amount: 3, health: 94)));

        recorded.Should().HaveCount(2);
        recorded.Select(record => record.Amount).Should().AllBeEquivalentTo(3d);
    }

    [TestMethod]
    public void The_replay_folds_a_clients_events_into_the_tally_they_are_evidence_for()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Recorded(host, Batch(host.Generation,
            new PlayerEvent(AuditRecordKind.ZoneWarned, TimeSpan.FromSeconds(8)),
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(10), amount: 3, health: 97),
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(12), amount: 3, health: 94),
            new PlayerEvent(AuditRecordKind.RingFired, TimeSpan.FromSeconds(14)),
            new PlayerEvent(AuditRecordKind.PlayerCaged, TimeSpan.FromSeconds(20)),
            new PlayerEvent(AuditRecordKind.PlayerReleased, TimeSpan.FromSeconds(40))));

        PlayerAudit player = AuditReplay.Of(host.Audit.Records).Matches[0].Player("Vela")!;

        player.Warnings.Should().Be(1);
        player.Bites.Should().Be(2);
        player.DamageTaken.Should().Be(6);
        player.RingsFired.Should().Be(1);
        player.TimesCaged.Should().Be(1);
        player.Health.Should().Be(94);
    }
}
