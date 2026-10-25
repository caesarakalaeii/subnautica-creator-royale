using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Match;
using Lms.Core.Results;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The telemetry JSON contract with the Go backend, asserted on the payload text.</summary>
[TestClass]
public class TelemetryJsonTests
{
    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly WorldPoint Somewhere = new WorldPoint(10, -20, 30);

    private static string Written(TelemetrySnapshot snapshot) => TelemetryJson.Write(snapshot);

    [TestMethod]
    public void A_push_with_no_tournament_on_the_server_still_says_when_it_was_taken()
    {
        string json = Written(new TelemetrySnapshot(Noon, null, null));

        json.Should().Be(
            @"{""v"":6,""at"":""2026-10-24T12:00:00.000Z"",""match"":null,""zone"":null," +
            @"""standingsRevision"":0,""standings"":null,""players"":[],""events"":[],""pods"":[]}");
    }

    [TestMethod]
    public void The_version_leads_the_payload()
    {
        Written(new TelemetrySnapshot(Noon, null, null)).Should().StartWith(@"{""v"":6,");
    }

    [TestMethod]
    public void A_tournament_with_no_finished_match_carries_a_null_table_at_revision_zero()
    {
        Written(new TelemetrySnapshot(Noon, null, null))
            .Should().Contain(@"""standingsRevision"":0,""standings"":null");
    }

    [TestMethod]
    public void The_table_carries_what_each_total_is_made_of()
    {
        StandingsTelemetry table = new StandingsTelemetry(2, 2, new[]
        {
            new StandingsLine(1, "Vela", 9, played: 2, wins: 1, bestPlacement: 1, kills: 2, outlasted: 5),
        });

        Written(new TelemetrySnapshot(Noon, null, null, null, null, table, standingsRevision: 4))
            .Should().Contain(
                @"""standingsRevision"":4,""standings"":{""matches"":2,""pointsPerKill"":2,""entries"":[" +
                @"{""place"":1,""name"":""Vela"",""points"":9,""played"":2,""wins"":1,""best"":1," +
                @"""kills"":2,""outlasted"":5}],""games"":[]}");
    }

    [TestMethod]
    public void The_table_says_what_a_kill_pays()
    {
        StandingsTelemetry table = new StandingsTelemetry(2, 2, new[]
        {
            new StandingsLine(1, "Vela", 9, played: 2, wins: 1, bestPlacement: 1, kills: 2, outlasted: 5),
        });

        Written(new TelemetrySnapshot(Noon, null, null, null, null, table, standingsRevision: 4))
            .Should().Contain(@"""standings"":{""matches"":2,""pointsPerKill"":2,");
    }

    [TestMethod]
    public void A_table_nobody_is_in_is_still_a_table()
    {
        Written(new TelemetrySnapshot(Noon, null, null, null, null, new StandingsTelemetry(1, 2), 1))
            .Should().Contain(@"""standings"":{""matches"":1,""pointsPerKill"":2,""entries"":[],""games"":[]}");
    }

    [TestMethod]
    public void Each_finished_match_rides_beside_the_table_it_summed()
    {
        StandingsTelemetry table = new StandingsTelemetry(2, 2, new[]
        {
            new StandingsLine(1, "Vela", 9, played: 2, wins: 1, bestPlacement: 1, kills: 2, outlasted: 5),
        }, new[]
        {
            new GameResult(14, 1, new[]
            {
                new Lms.Core.Results.GameResult.GameLine("Vela", points: 5, placement: 1,
                    kills: 1, outlasted: 3),
            }),
        });

        Written(new TelemetrySnapshot(Noon, null, null, null, null, table, standingsRevision: 4))
            .Should().Contain(
                @"""entries"":[{""place"":1,""name"":""Vela"",""points"":9,""played"":2,""wins"":1," +
                @"""best"":1,""kills"":2,""outlasted"":5}],""games"":[{""group"":14,""generation"":1," +
                @"""entries"":[{""name"":""Vela"",""points"":5,""placement"":1,""kills"":1," +
                @"""outlasted"":3}]}]}");
    }

    [TestMethod]
    public void A_table_with_no_finished_match_yet_carries_an_empty_games_array()
    {
        Written(new TelemetrySnapshot(Noon, null, null, null, null, new StandingsTelemetry(1, 2), 1))
            .Should().Contain(@"""matches"":1,""pointsPerKill"":2,""entries"":[],""games"":[]}");
    }

    [TestMethod]
    public void The_match_carries_the_countdown_the_overlay_renders()
    {
        MatchTelemetry match = new MatchTelemetry(2, 1, MatchClockState.Running,
            TimeSpan.FromSeconds(90), phase: 1, phases: 4,
            untilNextPhase: TimeSpan.FromSeconds(30), alive: 3);

        Written(new TelemetrySnapshot(Noon, match, null)).Should().Contain(
            @"""match"":{""group"":2,""generation"":1,""state"":""Running"",""elapsedMs"":90000," +
            @"""phase"":1,""phases"":4,""untilNextMs"":30000,""alive"":3}");
    }

    [TestMethod]
    public void Nothing_due_is_a_null_countdown_rather_than_a_zero_one()
    {
        MatchTelemetry match = new MatchTelemetry(1, 1, MatchClockState.NotStarted,
            TimeSpan.Zero, phase: 0, phases: 4, untilNextPhase: null, alive: 5);

        Written(new TelemetrySnapshot(Noon, match, null)).Should().Contain(@"""untilNextMs"":null");
    }

    [TestMethod]
    public void The_zone_carries_both_spheres_for_the_map()
    {
        ZoneTelemetry zone = new ZoneTelemetry(
            new SphereTelemetry(200, -15, 100, 1400),
            new SphereTelemetry(500, -90, 300, 900),
            damagePerTick: 3);

        Written(new TelemetrySnapshot(Noon, null, zone)).Should().Contain(
            @"""zone"":{""now"":{""x"":200,""y"":-15,""z"":100,""r"":1400}," +
            @"""next"":{""x"":500,""y"":-90,""z"":300,""r"":900},""damagePerTick"":3}");
    }

    [TestMethod]
    public void The_last_phase_has_no_next_sphere()
    {
        ZoneTelemetry zone = new ZoneTelemetry(new SphereTelemetry(750, -110, 450, 120), null, 20);

        Written(new TelemetrySnapshot(Noon, null, zone)).Should().Contain(@"""next"":null");
    }

    [TestMethod]
    public void A_player_row_carries_everything_the_dashboard_shows()
    {
        PlayerTelemetry player = new PlayerTelemetry("Vela", alive: true, position: Somewhere,
            biome: "kelpForest", health: 92, oxygen: 40, food: 70, water: 65,
            vehicle: "Seamoth", heldItem: "StasisRifle", insideZone: true, distanceOutside: 0,
            inventory: new[] { new InventoryLine("Titanium", 4) },
            equipment: new[] { new EquipmentLine("Body", "ReinforcedDiveSuit") },
            rifle: true, rifleCharge: 0.5, frozen: false);

        Written(new TelemetrySnapshot(Noon, null, null, new[] { player })).Should().Contain(
            @"""players"":[{""name"":""Vela"",""alive"":true,""x"":10,""y"":-20,""z"":30,""depth"":20," +
            @"""biome"":""kelpForest"",""health"":92,""oxygen"":40,""food"":70,""water"":65," +
            @"""vehicle"":""Seamoth"",""held"":""StasisRifle"",""rifle"":true,""rifleCharge"":0.5," +
            @"""frozen"":false,""insideZone"":true,""distanceOutside"":0," +
            @"""inventory"":[{""item"":""Titanium"",""count"":4}]," +
            @"""equipment"":[{""slot"":""Body"",""item"":""ReinforcedDiveSuit""}]}]");
    }

    [TestMethod]
    public void A_player_wearing_nothing_sends_an_empty_set_rather_than_leaving_it_out()
    {
        PlayerTelemetry player = new PlayerTelemetry("Rook", alive: true, position: Somewhere,
            biome: null, health: 100, oxygen: 45, food: 90, water: 90,
            vehicle: null, heldItem: null, insideZone: true, distanceOutside: 0);

        string json = Written(new TelemetrySnapshot(Noon, null, null, new[] { player }));

        json.Should().Contain(@"""equipment"":[]");
        json.Should().Contain(@"""rifle"":false,""rifleCharge"":0,""frozen"":false");
    }

    [TestMethod]
    public void A_rifle_being_charged_and_a_player_who_cannot_move_are_both_on_the_row()
    {
        PlayerTelemetry player = new PlayerTelemetry("Vela", alive: true, position: Somewhere,
            biome: null, health: 50, oxygen: 20, food: 40, water: 40,
            vehicle: null, heldItem: "StasisRifle", insideZone: true, distanceOutside: 0,
            inventory: null, equipment: null, rifle: true, rifleCharge: 0.75, frozen: true);

        Written(new TelemetrySnapshot(Noon, null, null, new[] { player })).Should().Contain(
            @"""rifle"":true,""rifleCharge"":0.75,""frozen"":true");
    }

    [TestMethod]
    public void A_player_on_foot_with_empty_hands_sends_nulls_rather_than_blanks()
    {
        PlayerTelemetry player = new PlayerTelemetry("Rook", alive: false, position: Somewhere,
            biome: null, health: 0, oxygen: 0, food: 12, water: 8,
            vehicle: null, heldItem: null, insideZone: false, distanceOutside: 240);

        string json = Written(new TelemetrySnapshot(Noon, null, null, new[] { player }));

        json.Should().Contain(@"""vehicle"":null,""held"":null");
        json.Should().Contain(@"""biome"":null");
        json.Should().Contain(@"""inventory"":[]");
    }

    [TestMethod]
    public void The_pods_are_on_the_document_so_a_caster_has_something_to_orient_against()
    {
        TelemetrySnapshot snapshot = new TelemetrySnapshot(Noon, null, null, null, null, null, 0,
            new[] { new PodTelemetry(0, 0, 600, 1), new PodTelemetry(-100.5, 0, 12.25, 0) });

        Written(snapshot).Should().Contain(
            @"""pods"":[{""x"":0,""y"":0,""z"":600,""assigned"":1}," +
            @"{""x"":-100.5,""y"":0,""z"":12.25,""assigned"":0}]");
    }

    [TestMethod]
    public void A_world_with_no_pods_in_it_sends_an_empty_list_rather_than_nothing()
    {
        Written(new TelemetrySnapshot(Noon, null, null)).Should().Contain(@"""pods"":[]");
    }

    [TestMethod]
    public void An_event_carries_the_logs_own_word_for_what_happened()
    {
        EventTelemetry bite = new EventTelemetry(42, 1, 1, Noon, TimeSpan.FromSeconds(120),
            AuditRecordKind.ZoneBite, "Vela", null, 2, 3, null, Somewhere, "kelpForest");

        Written(new TelemetrySnapshot(Noon, null, null, null, new[] { bite })).Should().Contain(
            @"""events"":[{""seq"":42,""group"":1,""generation"":1," +
            @"""at"":""2026-10-24T12:00:00.000Z"",""elapsedMs"":120000," +
            @"""kind"":""ZoneBite"",""player"":""Vela"",""actor"":null,""phase"":2,""amount"":3," +
            @"""detail"":null,""x"":10,""y"":-20,""z"":30,""biome"":""kelpForest""}]");
    }

    [TestMethod]
    public void An_event_with_no_place_says_so_rather_than_claiming_the_origin()
    {
        EventTelemetry phase = new EventTelemetry(7, 1, 1, Noon, TimeSpan.FromMinutes(1),
            AuditRecordKind.PhaseStarted, null, null, 1, null, null, null, null);

        string json = Written(new TelemetrySnapshot(Noon, null, null, null, new[] { phase }));

        json.Should().Contain(@"""x"":null,""y"":null,""z"":null");
    }

    [TestMethod]
    public void An_event_can_be_taken_straight_off_an_audit_record()
    {
        AuditLog log = new AuditLog();
        AuditRecord record = log.Append(Noon, new MatchStamp(1, 1, TimeSpan.FromSeconds(30)),
            AuditRecordKind.PlayerCaged, player: "Rook", actor: "Vela", position: Somewhere,
            biome: "grassyPlateaus");

        EventTelemetry telemetry = EventTelemetry.Of(record);

        telemetry.Sequence.Should().Be(record.Sequence);
        telemetry.Group.Should().Be(1);
        telemetry.Generation.Should().Be(1);
        telemetry.Kind.Should().Be(AuditRecordKind.PlayerCaged);
        telemetry.Player.Should().Be("Rook");
        telemetry.Actor.Should().Be("Vela");
        telemetry.Elapsed.Should().Be(TimeSpan.FromSeconds(30));
        telemetry.Position.Should().Be(record.Position);
        telemetry.Biome.Should().Be("grassyPlateaus");
    }

    [TestMethod]
    public void A_sphere_can_be_taken_straight_off_the_zone()
    {
        SphereTelemetry sphere = SphereTelemetry.Of(new ZoneSphere(new WorldPoint(1, 2, 3), 400));

        sphere.X.Should().Be(1);
        sphere.Y.Should().Be(2);
        sphere.Z.Should().Be(3);
        sphere.Radius.Should().Be(400);
    }

    [TestMethod]
    public void A_hostile_player_name_does_not_break_the_payload()
    {
        PlayerTelemetry player = new PlayerTelemetry("Ve\"la\\", alive: true, position: Somewhere,
            biome: null, health: 100, oxygen: 45, food: 90, water: 90,
            vehicle: null, heldItem: null, insideZone: true, distanceOutside: 0);

        Written(new TelemetrySnapshot(Noon, null, null, new[] { player }))
            .Should().Contain(@"""name"":""Ve\""la\\""");
    }

    [TestMethod]
    public void Several_players_and_several_events_are_comma_separated()
    {
        List<PlayerTelemetry> players = new List<PlayerTelemetry>
        {
            new PlayerTelemetry("A", true, Somewhere, null, 1, 1, 1, 1, null, null, true, 0),
            new PlayerTelemetry("B", true, Somewhere, null, 1, 1, 1, 1, null, null, true, 0),
        };

        List<EventTelemetry> events = new List<EventTelemetry>
        {
            new EventTelemetry(1, 1, 1, Noon, TimeSpan.Zero, AuditRecordKind.MatchStarted, null, null, null, null, null, null, null),
            new EventTelemetry(2, 1, 1, Noon, TimeSpan.Zero, AuditRecordKind.MatchPaused, null, null, null, null, null, null, null),
        };

        string json = Written(new TelemetrySnapshot(Noon, null, null, players, events));

        json.Should().Contain(@"""name"":""A""").And.Contain(@"""name"":""B""");
        json.Should().Contain(@"},{""name"":""B""");
        json.Should().Contain(@"},{""seq"":2");
    }
}
