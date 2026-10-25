using System;
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

/// <summary>A disconnect does not decide a match until the grace has run.</summary>
[TestClass]
public class DisconnectGraceTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(20);

    private static readonly WorldPoint Inside = new WorldPoint(750, -110, 450);

    private static readonly WorldPoint Outside = new WorldPoint(200, -15, 1700);

    private readonly PlayerRoster roster = new PlayerRoster();

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static PlayerReading Reading(ushort id, string name, WorldPoint where, bool alive = true) =>
        new PlayerReading(id, name, where, alive, 92, 40, 70, 65);

    private static TimeSpan At(double seconds) => Noon + TimeSpan.FromSeconds(seconds);

    private void Tick(MatchHost host, int second, params PlayerReading[] present)
    {
        host.Record(roster.Update(present, host.Zone(At(second)).Sphere, host.Generation).Changes);
        host.Advance(At(second), present.Where(row => row.Alive).Select(row => row.Name).ToList());
    }

    private void Run(MatchHost host, int fromSecond, int toSecond, params PlayerReading[] present)
    {
        for (int second = fromSecond; second <= toSecond; second++)
        {
            Tick(host, second, present);
        }
    }

    private static bool Recorded(MatchHost host, AuditRecordKind kind) =>
        host.Audit.Records.Any(record => record.Kind == kind);

    private static PlayerReading Vela(WorldPoint where, bool alive = true) => Reading(1, "Vela", where, alive);

    private static PlayerReading Rook(WorldPoint where, bool alive = true) => Reading(2, "Rook", where, alive);

    [TestMethod]
    public void A_dropped_cable_does_not_end_the_round_while_the_grace_is_running()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 48, Vela(Inside));

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse("Rook is still inside their grace");
    }

    [TestMethod]
    public void The_clock_keeps_running_while_the_match_waits_for_an_absentee()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 48, Vela(Inside));

        host.Telemetry(At(48), 1).Elapsed.Should().Be(TimeSpan.FromSeconds(48));
    }

    [TestMethod]
    public void A_player_who_gets_back_inside_the_grace_is_still_in_the_match()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 40, Vela(Inside));
        Run(host, 41, 90, Vela(Inside), Rook(Inside));

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse();
    }

    [TestMethod]
    public void A_corpse_and_a_disconnect_is_not_a_finished_match_yet()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 48, Vela(Inside, alive: false));

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse();
    }

    [TestMethod]
    public void The_match_is_decided_the_moment_the_grace_runs_out()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 60, Vela(Inside));

        host.Telemetry(At(60), 1).Elapsed.Should().Be(TimeSpan.FromSeconds(31) + Grace);
    }

    [TestMethod]
    public void The_player_who_stayed_is_named_once_the_grace_has_run()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 70, Vela(Inside));

        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.MatchEnded)
            .Player.Should().Be("Vela");
    }

    [TestMethod]
    public void A_ghost_is_not_somebody_the_match_waits_for()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 30, Vela(Inside), Rook(Inside));
        host.Command("ghost Rook", At(31));
        Run(host, 32, 48, Vela(Inside));

        host.Telemetry(At(48), 1).State.Should().Be(MatchClockState.Paused,
            "the match was decided the moment the ghost left the roster");
    }

    [TestMethod]
    public void An_absence_the_clock_ran_through_is_billed()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        PlayerReading kes = Reading(3, "Kes", Outside);

        Run(host, 1, 30, Vela(Inside), Rook(Inside), kes);

        Tick(host, 30, Vela(Inside), Rook(Inside));
        Run(host, 31, 69, Vela(Inside), Rook(Inside));

        ZoneDebt debt = host.Arrears("Kes", At(70))!.Value;

        debt.Absence.Should().Be(TimeSpan.FromSeconds(40));
        debt.Forgiven.Should().Be(Grace);
        debt.Ticks.Should().Be(10);
        debt.Damage.Should().Be((4 * 1) + (6 * 3));
    }

    [TestMethod]
    public void A_disconnect_at_the_collapse_still_leaves_the_survivor_named()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, 1, 260, Vela(Inside), Rook(Inside));

        Recorded(host, AuditRecordKind.ZoneClosed).Should().BeTrue("the wall really did shut");

        Run(host, 261, 295, Vela(Inside));

        host.Audit.Records.Should().ContainSingle(record => record.Kind == AuditRecordKind.MatchEnded)
            .Which.Player.Should().Be("Vela");
    }
}
