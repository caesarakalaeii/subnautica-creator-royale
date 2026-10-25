using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>How the roster turns a client's event batch into named events and drops events for absent players.</summary>
[TestClass]
public class RosterEventTests
{
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(0, 0, 0), 100);

    private static PlayerReading Reading(ushort id = 1, string name = "Vela") =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 92, 40, 70, 65);

    private static PlayerEventBatch Batch(int generation, params AuditRecordKind[] kinds) =>
        new PlayerEventBatch(generation,
            kinds.Select((kind, i) => new PlayerEvent(kind, TimeSpan.FromSeconds(i + 1))).ToList());

    private static IReadOnlyList<AuditRecordKind> Kinds(RosterUpdate update) =>
        update.Changes.Select(change => change.Kind).ToList();

    [TestMethod]
    public void An_event_arrives_with_the_name_the_server_knows_the_sender_by()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(7, Batch(2, AuditRecordKind.ZoneBite));

        RosterUpdate update = roster.Update(new[] { Reading(7, "Quill") }, Wall);
        RosterChange bite = update.Changes.Single(change => change.Kind == AuditRecordKind.ZoneBite);

        bite.Player.Should().Be("Quill");
        bite.Generation.Should().Be(2);
        bite.Elapsed.Should().Be(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void Everything_an_event_carries_survives_the_roster()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerEventBatch(3, new[]
        {
            new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(42),
                new WorldPoint(10, -20, 30), "kelpForest", amount: 3, health: 88, phase: 2,
                detail: "outside")
        }));

        RosterChange bite = roster.Update(new[] { Reading() }, Wall).Changes
            .Single(change => change.Kind == AuditRecordKind.ZoneBite);

        bite.Position.Should().Be(new WorldPoint(10, -20, 30));
        bite.Biome.Should().Be("kelpForest");
        bite.Amount.Should().Be(3);
        bite.Health.Should().Be(88);
        bite.Phase.Should().Be(2);
        bite.Detail.Should().Be("outside");
    }

    [TestMethod]
    public void Events_are_taken_once_and_not_again()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, Batch(1, AuditRecordKind.ZoneBite));

        Kinds(roster.Update(new[] { Reading() }, Wall)).Should().Contain(AuditRecordKind.ZoneBite);
        Kinds(roster.Update(new[] { Reading() }, Wall)).Should().BeEmpty();
    }

    [TestMethod]
    public void The_order_a_client_saw_them_in_survives()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Update(new[] { Reading() }, Wall);
        roster.Reported(1, Batch(1, AuditRecordKind.RingFired, AuditRecordKind.PlayerCaged,
            AuditRecordKind.PlayerReleased));

        Kinds(roster.Update(new[] { Reading() }, Wall)).Should().Equal(AuditRecordKind.RingFired,
            AuditRecordKind.PlayerCaged, AuditRecordKind.PlayerReleased);
    }

    [TestMethod]
    public void Two_batches_between_two_ticks_both_land_in_order()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Update(new[] { Reading() }, Wall);
        roster.Reported(1, Batch(1, AuditRecordKind.RingFired));
        roster.Reported(1, Batch(1, AuditRecordKind.PlayerCaged));

        Kinds(roster.Update(new[] { Reading() }, Wall))
            .Should().Equal(AuditRecordKind.RingFired, AuditRecordKind.PlayerCaged);
    }

    [TestMethod]
    public void An_event_from_somebody_who_is_no_longer_in_the_world_is_dropped()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Update(new[] { Reading(1, "Vela") }, Wall);

        roster.Reported(1, Batch(1, AuditRecordKind.ZoneBite));

        Kinds(roster.Update(new PlayerReading[0], Wall)).Should().Equal(AuditRecordKind.PlayerLeft);
        Kinds(roster.Update(new[] { Reading(1, "Somebody else") }, Wall))
            .Should().Equal(AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void An_event_from_a_player_the_server_has_never_seen_waits_rather_than_being_invented()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(4, Batch(1, AuditRecordKind.ZoneBite));

        Kinds(roster.Update(new PlayerReading[0], Wall)).Should().BeEmpty();
        Kinds(roster.Update(new[] { Reading(4, "Wren") }, Wall))
            .Should().Equal(AuditRecordKind.PlayerJoined, AuditRecordKind.ZoneBite);
    }

    [TestMethod]
    public void A_players_own_events_come_after_their_arrival_and_before_their_elimination()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, Batch(1, AuditRecordKind.ZoneBite));
        roster.Reported(1, new PlayerReport(true, null, null, null, null));

        Kinds(roster.Update(new[] { Reading() }, Wall)).Should().Equal(AuditRecordKind.PlayerJoined,
            AuditRecordKind.ZoneBite, AuditRecordKind.PlayerEliminated);
    }

    [TestMethod]
    public void One_players_events_do_not_come_between_another_players_arrival_and_their_own()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, Batch(1, AuditRecordKind.RingFired));
        roster.Reported(2, Batch(1, AuditRecordKind.PlayerCaged));

        RosterUpdate update = roster.Update(
            new[] { Reading(1, "Vela"), Reading(2, "Quill") }, Wall);

        update.Changes.Select(change => $"{change.Kind} {change.Player}").Should().Equal(
            "PlayerJoined Vela", "RingFired Vela", "PlayerJoined Quill", "PlayerCaged Quill");
    }

    [TestMethod]
    public void A_client_queueing_faster_than_the_server_drains_drops_the_oldest()
    {
        PlayerRoster roster = new PlayerRoster(eventsPerPlayer: 3);
        roster.Update(new[] { Reading() }, Wall);

        for (int i = 0; i < 5; i++)
        {
            roster.Reported(1, new PlayerEventBatch(1, new[]
            {
                new PlayerEvent(AuditRecordKind.ZoneBite, TimeSpan.FromSeconds(i + 1))
            }));
        }

        IReadOnlyList<RosterChange> events = roster.Update(new[] { Reading() }, Wall).Changes;

        events.Should().HaveCount(3);
        events[0].Elapsed.Should().Be(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public void Nothing_is_not_a_batch()
    {
        PlayerEventBatch nothing = null!;
        Action reported = () => new PlayerRoster().Reported(1, nothing);

        reported.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void A_derived_change_has_no_generation_to_fence_on()
    {
        PlayerRoster roster = new PlayerRoster();

        roster.Update(new[] { Reading() }, Wall).Changes[0].Generation.Should().BeNull();
    }
}
