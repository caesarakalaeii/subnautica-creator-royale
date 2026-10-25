using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The player table merged from server state and client reports: liveness and inventory on departure.</summary>
[TestClass]
public class PlayerRosterTests
{
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(0, 0, 0), 100);

    private static PlayerReading Reading(ushort id = 1, string name = "Vela", double x = 0, bool alive = true) =>
        new PlayerReading(id, name, new WorldPoint(x, 0, 0), alive, 92, 40, 70, 65);

    private static IReadOnlyList<PlayerTelemetry> Rows(PlayerRoster roster, params PlayerReading[] readings) =>
        roster.Update(readings, Wall).Rows;

    private static IReadOnlyList<AuditRecordKind> Changes(PlayerRoster roster, params PlayerReading[] readings) =>
        roster.Update(readings, Wall).Changes.Select(change => change.Kind).ToList();

    /// <summary>The same, for a caller that is reporting on a particular attempt at a match.</summary>
    private static IReadOnlyList<AuditRecordKind> Changes(PlayerRoster roster, int generation,
        params PlayerReading[] readings) =>
        roster.Update(readings, Wall, generation).Changes.Select(change => change.Kind).ToList();

    [TestMethod]
    public void A_replaced_match_gets_its_own_arrivals()
    {
        PlayerRoster roster = new PlayerRoster();

        Changes(roster, 1, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);
        Changes(roster, 1, Reading()).Should().BeEmpty("nothing has changed");

        Changes(roster, 2, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void A_caller_with_no_match_never_re_announces_anybody()
    {
        PlayerRoster roster = new PlayerRoster();

        Changes(roster, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);
        Changes(roster, Reading()).Should().BeEmpty();
    }

    [TestMethod]
    public void Somebody_who_left_on_the_same_tick_as_a_reset_still_leaves_a_departure_behind_them()
    {
        PlayerRoster roster = new PlayerRoster();
        Changes(roster, 1, Reading(1, "Vela"), Reading(2, "Rook"));

        Changes(roster, 2, Reading(1, "Vela")).Should().Equal(
            AuditRecordKind.PlayerLeft, AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void A_player_who_has_said_nothing_yet_still_has_a_row()
    {
        IReadOnlyList<PlayerTelemetry> rows = Rows(new PlayerRoster(), Reading());

        rows.Should().ContainSingle();
        rows[0].Name.Should().Be("Vela");
        rows[0].Health.Should().Be(92);
        rows[0].Biome.Should().BeNull();
        rows[0].Vehicle.Should().BeNull();
        rows[0].HeldItem.Should().BeNull();
        rows[0].Inventory.Should().BeEmpty();
    }

    [TestMethod]
    public void What_a_client_says_about_itself_fills_the_rest_of_its_row()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(false, "kelpForest", "Seamoth", "StasisRifle",
            new[] { new InventoryLine("Titanium", 4) }));

        PlayerTelemetry row = Rows(roster, Reading())[0];

        row.Biome.Should().Be("kelpForest");
        row.Vehicle.Should().Be("Seamoth");
        row.HeldItem.Should().Be("StasisRifle");
        row.Inventory.Should().ContainSingle();
    }

    [TestMethod]
    public void The_zone_verdict_is_the_servers_and_not_the_clients()
    {
        PlayerRoster roster = new PlayerRoster();

        PlayerTelemetry inside = Rows(roster, Reading(x: 50))[0];
        PlayerTelemetry outside = Rows(roster, Reading(x: 140))[0];

        inside.InsideZone.Should().BeTrue();
        inside.DistanceOutside.Should().Be(0);
        outside.InsideZone.Should().BeFalse();
        outside.DistanceOutside.Should().Be(40);
    }

    [TestMethod]
    public void A_player_the_match_is_finished_with_is_not_alive()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(true, null, null, null, null));

        Rows(roster, Reading())[0].Alive.Should().BeFalse();
    }

    [TestMethod]
    public void A_player_the_server_has_written_off_is_not_alive_either()
    {
        Rows(new PlayerRoster(), Reading(alive: false))[0].Alive.Should().BeFalse();
    }

    [TestMethod]
    public void How_many_are_left_is_counted_off_the_same_rows_the_booth_is_sent()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(2, new PlayerReport(true, null, null, null, null));

        IReadOnlyList<PlayerTelemetry> rows = roster.Update(
            new[] { Reading(1, "Vela"), Reading(2, "Rook"), Reading(3, "Wren") }, Wall).Rows;

        PlayerRoster.Alive(rows).Should().Be(2);
    }

    [TestMethod]
    public void A_player_who_leaves_takes_what_they_said_with_them()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(true, "kelpForest", "Seamoth", "StasisRifle",
            new[] { new InventoryLine("Titanium", 4) }));

        Rows(roster, Reading());
        Rows(roster);

        PlayerTelemetry rejoined = Rows(roster, Reading())[0];

        rejoined.Alive.Should().BeTrue();
        rejoined.Biome.Should().BeNull();
        rejoined.Inventory.Should().BeEmpty();
    }

    [TestMethod]
    public void A_report_is_folded_in_on_the_very_next_push()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(false, "kelpForest", null, null, null));

        Rows(roster, Reading())[0].Biome.Should().Be("kelpForest");
    }

    [TestMethod]
    public void Somebody_arriving_is_recorded_once()
    {
        PlayerRoster roster = new PlayerRoster();

        Changes(roster, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);
        Changes(roster, Reading()).Should().BeEmpty("they are still here; that is not news");
    }

    [TestMethod]
    public void An_arrival_says_nothing_about_where_they_arrived()
    {
        PlayerRoster roster = new PlayerRoster();

        RosterChange change = roster.Update(new[] { Reading(x: 140) }, Wall).Changes
            .Should().ContainSingle().Subject;

        change.Kind.Should().Be(AuditRecordKind.PlayerJoined);
        change.Player.Should().Be("Vela");
        change.Position.Should().BeNull();
        change.Biome.Should().BeNull();
        change.Health.Should().BeNull();
    }

    [TestMethod]
    public void Somebody_out_of_one_match_can_be_put_out_of_the_next()
    {
        PlayerRoster roster = new PlayerRoster();

        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Changes(roster, Reading()).Should().Contain(AuditRecordKind.PlayerEliminated);

        roster.Reported(1, PlayerReport.Empty);
        Changes(roster, Reading()).Should().BeEmpty("coming back is not an event in the log");

        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Changes(roster, Reading()).Should().Equal(AuditRecordKind.PlayerEliminated);
    }

    [TestMethod]
    public void Somebody_leaving_is_recorded_once_with_the_last_place_they_were()
    {
        PlayerRoster roster = new PlayerRoster();
        Rows(roster, Reading(x: 140));

        RosterChange change = roster.Update(new PlayerReading[0], Wall).Changes.Should().ContainSingle().Subject;

        change.Kind.Should().Be(AuditRecordKind.PlayerLeft);
        change.Player.Should().Be("Vela");
        change.Position.Should().Be(new WorldPoint(140, 0, 0));
        change.Health.Should().Be(92);

        Changes(roster).Should().BeEmpty();
    }

    [TestMethod]
    public void Somebody_the_zone_finishes_with_is_recorded_once()
    {
        PlayerRoster roster = new PlayerRoster();
        Rows(roster, Reading());

        roster.Reported(1, new PlayerReport(true, "kelpForest", null, null, null));

        Changes(roster, Reading()).Should().Equal(AuditRecordKind.PlayerEliminated);
        Changes(roster, Reading()).Should().BeEmpty("a player is only eliminated once");
    }

    [TestMethod]
    public void An_elimination_says_where_it_happened_and_what_state_they_were_in()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(true, "kelpForest", null, null, null));

        RosterChange change = roster.Update(new[] { Reading(x: 140) }, Wall).Changes
            .Single(c => c.Kind == AuditRecordKind.PlayerEliminated);

        change.Player.Should().Be("Vela");
        change.Position.Should().Be(new WorldPoint(140, 0, 0));
        change.Biome.Should().Be("kelpForest");
        change.Health.Should().Be(92);
    }

    [TestMethod]
    public void Arriving_already_out_is_both_things_in_the_order_they_happened()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(true, null, null, null, null));

        Changes(roster, Reading()).Should().Equal(
            AuditRecordKind.PlayerJoined, AuditRecordKind.PlayerEliminated);
    }

    [TestMethod]
    public void Coming_back_after_a_drop_is_a_fresh_arrival()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Rows(roster, Reading());
        Rows(roster);

        Changes(roster, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void A_quiet_tick_changes_nothing()
    {
        PlayerRoster roster = new PlayerRoster();
        Rows(roster, Reading(1, "Vela"), Reading(2, "Rook"));

        Changes(roster, Reading(1, "Vela"), Reading(2, "Rook")).Should().BeEmpty();
    }

    [TestMethod]
    public void Departures_are_recorded_before_arrivals()
    {
        PlayerRoster roster = new PlayerRoster();
        Rows(roster, Reading(1, "Vela"));

        IReadOnlyList<RosterChange> changes = roster.Update(new[] { Reading(2, "Rook") }, Wall).Changes;

        changes.Select(change => change.Kind).Should()
            .Equal(AuditRecordKind.PlayerLeft, AuditRecordKind.PlayerJoined);
        changes[0].Player.Should().Be("Vela");
        changes[1].Player.Should().Be("Rook");
    }
}
