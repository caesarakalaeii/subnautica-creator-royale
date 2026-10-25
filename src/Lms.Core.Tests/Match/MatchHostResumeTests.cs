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

/// <summary>A server that starts again picks the tournament up from the audit logs its earlier runs left behind.</summary>
[TestClass]
public class MatchHostResumeTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static readonly WorldPoint Safe = new WorldPoint(750, -110, 450);

    private readonly PlayerRoster roster = new PlayerRoster();

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static TimeSpan At(double seconds) => Noon + TimeSpan.FromSeconds(seconds);

    private static PlayerReading Reading(ushort id, string name, bool alive = true) =>
        new PlayerReading(id, name, Safe, alive, 92, 40, 70, 65);

    private void Tick(MatchHost host, TimeSpan worldNow, params PlayerReading[] present) =>
        host.Record(roster.Update(present, host.Zone(worldNow).Sphere, host.Generation).Changes);

    private void PlayOut(MatchHost host, double startAt, string winner, string loser)
    {
        host.Command("start", At(startAt));
        Tick(host, At(startAt), Reading(1, winner), Reading(2, loser));
        host.Advance(At(startAt), new[] { winner, loser });
        Tick(host, At(startAt + 5), Reading(1, winner), Reading(2, loser, alive: false));

        for (double second = startAt + 5; second <= startAt + 20; second++)
        {
            host.Advance(At(second), new[] { winner });
        }
    }

    private static IReadOnlyList<AuditRecord> Run(MatchHost host) => host.Audit.Records.ToList();

    [TestMethod]
    public void A_restarted_server_waits_at_the_game_it_was_on()
    {
        MatchHost before = NewHost();
        PlayOut(before, 0, winner: "Vela", loser: "Rook");
        before.Command("next", At(30));
        PlayOut(before, 31, winner: "Quill", loser: "Fenris");

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });

        after.Group.Should().Be(2, "results have to land on the game being played, not on game 1");
        after.Status(At(60)).Should().Contain("group 2");
    }

    [TestMethod]
    public void A_next_typed_before_the_restart_is_kept()
    {
        MatchHost before = NewHost();
        PlayOut(before, 0, winner: "Vela", loser: "Rook");
        before.Command("next", At(30));

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });

        after.Group.Should().Be(2);
    }

    [TestMethod]
    public void The_games_earlier_runs_finished_stay_on_the_table()
    {
        MatchHost first = NewHost();
        PlayOut(first, 0, winner: "Vela", loser: "Rook");
        first.Command("next", At(30));

        MatchHost second = NewHost();
        second.Resume(new[] { Run(first) });
        PlayOut(second, 0, winner: "Vela", loser: "Fenris");
        second.Command("next", At(30));

        MatchHost third = NewHost();
        third.Resume(new[] { Run(first), Run(second) });

        StandingsTelemetry table = third.Standings(At(1))!;

        table.Matches.Should().Be(2, "one game from each earlier run");
        table.Entries[0].Name.Should().Be("Vela");
        table.Entries[0].Wins.Should().Be(2);
        third.Group.Should().Be(3);
    }

    [TestMethod]
    public void A_game_played_again_after_a_restart_replaces_the_one_before_it()
    {
        MatchHost before = NewHost();
        before.Command("start", At(0));
        Tick(before, At(0), Reading(1, "Vela"), Reading(2, "Rook"));

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });
        after.Generation.Should().BeGreaterThan(Run(before).Last().Generation,
            "a replay is a new attempt, never a continuation of the earlier run's");

        PlayOut(after, 0, winner: "Rook", loser: "Vela");

        StandingsTelemetry table = after.Standings(At(30))!;

        table.Matches.Should().Be(1);
        table.Entries[0].Name.Should().Be("Rook");
    }

    [TestMethod]
    public void Rejoining_after_a_restore_keeps_the_game_that_finished_before_it()
    {
        MatchHost before = NewHost();
        PlayOut(before, 0, winner: "Vela", loser: "Rook");

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });
        after.Record(new PlayerRoster().Update(new[] { Reading(1, "Vela"), Reading(2, "Rook") },
            after.Zone(At(5)).Sphere, after.Generation).Changes);
        after.Command("next", At(10));

        StandingsTelemetry? table = after.Standings(At(11));

        table.Should().NotBeNull();
        table!.Matches.Should().Be(1);
        table.Entries[0].Name.Should().Be("Vela");
    }

    [TestMethod]
    public void Starting_a_game_that_finished_before_the_restart_is_refused()
    {
        MatchHost before = NewHost();
        PlayOut(before, 0, winner: "Vela", loser: "Rook");

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });

        after.Command("start", At(5)).Should().Contain("next");
        after.Audit.Records.Should().NotContain(r => r.Kind == AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void A_reset_after_the_restart_plays_the_finished_game_again()
    {
        MatchHost before = NewHost();
        PlayOut(before, 0, winner: "Vela", loser: "Rook");

        MatchHost after = NewHost();
        after.Resume(new[] { Run(before) });
        after.Command("reset", At(5));
        after.Command("start", At(6));

        after.Audit.Records.Should().Contain(r => r.Kind == AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void A_server_with_no_earlier_run_starts_at_game_one()
    {
        MatchHost host = NewHost();
        host.Resume(new[] { (IReadOnlyList<AuditRecord>)new AuditRecord[0] });

        host.Group.Should().Be(1);
        host.Standings(At(1)).Should().BeNull();
    }
}
