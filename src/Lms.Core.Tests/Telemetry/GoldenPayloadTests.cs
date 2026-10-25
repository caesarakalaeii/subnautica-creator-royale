using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Match;
using Lms.Core.Results;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The frozen telemetry payload shared with the Go backend; a failure here is a wire change.</summary>
[TestClass]
public class GoldenPayloadTests
{
    /// <summary>The one snapshot both languages are tested against. Kept in one place, here.</summary>
    internal static TelemetrySnapshot Canonical()
    {
        DateTimeOffset at = new DateTimeOffset(2026, 10, 24, 12, 34, 56, 789, TimeSpan.Zero);

        MatchTelemetry match = new MatchTelemetry(2, 1, MatchClockState.Running,
            TimeSpan.FromSeconds(150), phase: 1, phases: 4,
            untilNextPhase: TimeSpan.FromSeconds(30), alive: 3);

        ZoneTelemetry zone = new ZoneTelemetry(
            SphereTelemetry.Of(new ZoneSphere(new WorldPoint(200, -15, 100), 1400)),
            SphereTelemetry.Of(new ZoneSphere(new WorldPoint(500, -90, 300), 900)),
            damagePerTick: 3);

        List<PlayerTelemetry> players = new List<PlayerTelemetry>
        {
            new PlayerTelemetry("Vela", true, new WorldPoint(123.4567, -42.1, 88.9), "kelpForest",
                92, 40.5, 70, 65, "Seamoth", "StasisRifle", true, 0,
                new[] { new InventoryLine("Titanium", 4), new InventoryLine("Quartz \"big\"", 2) },
                new[]
                {
                    new EquipmentLine("Body", "ReinforcedDiveSuit"),
                    new EquipmentLine("Tank", "HighCapacityTank"),
                },
                rifle: true, rifleCharge: 0.5, frozen: false),
            new PlayerTelemetry("Ro\\ok", false, new WorldPoint(-5, -300, 12), null,
                0, 0, 12, 8, null, null, false, 240.25),
        };

        List<EventTelemetry> events = new List<EventTelemetry>
        {
            new EventTelemetry(41, 2, 1, at, TimeSpan.FromSeconds(60), AuditRecordKind.PhaseStarted,
                null, null, 1, null, null, null, null),
            new EventTelemetry(42, 2, 1, at, TimeSpan.FromSeconds(120), AuditRecordKind.ZoneBite,
                "Vela", null, 1, 3, null, new WorldPoint(1, -2, 3), "kelpForest"),
            new EventTelemetry(43, 2, 1, at, TimeSpan.FromSeconds(140), AuditRecordKind.PlayerEliminated,
                "Ro\\ok", "Vela", 1, null, "zone", new WorldPoint(-5, -300, 12), null),
        };

        StandingsTelemetry? standings = new StandingsTelemetry(2, 2, new[]
        {
            new StandingsLine(1, "Vela", 9, played: 2, wins: 1, bestPlacement: 1, kills: 2, outlasted: 5),
            new StandingsLine(2, "Ro\\ok", 4, played: 2, wins: 0, bestPlacement: 3, kills: 1, outlasted: 2),
        }, new[]
        {
            new GameResult(1, 1, new[]
            {
                new GameResult.GameLine("Vela", points: 5, placement: 1, kills: 1, outlasted: 3),
                new GameResult.GameLine("Ro\\ok", points: 2, placement: 3, kills: 1, outlasted: 0),
            }),
            new GameResult(2, 1, new[]
            {
                new GameResult.GameLine("Vela", points: 4, placement: 2, kills: 1, outlasted: 2),
                new GameResult.GameLine("Ro\\ok", points: 2, placement: 3, kills: 0, outlasted: 2),
            }),
        });

        List<PodTelemetry> pods = new List<PodTelemetry>
        {
            new PodTelemetry(0, 0, 600, 1),
            new PodTelemetry(571, 0, 185, 1),
            new PodTelemetry(353, 0, -485, 1),
            new PodTelemetry(-353, 0, -485, 1),
            new PodTelemetry(-100.5, 0, 12.25, 0),
        };

        return new TelemetrySnapshot(at, match, zone, players, events, standings, standingsRevision: 2,
            pods: pods);
    }

    [TestMethod]
    public void The_committed_example_is_exactly_what_the_encoder_writes()
    {
        string committed = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "telemetry-example.json")).Trim();

        TelemetryJson.Write(Canonical()).Should().Be(committed,
            "docs/telemetry/example.json is the contract, and the Go side is tested against it too");
    }
}
