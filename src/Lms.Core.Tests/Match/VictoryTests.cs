using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What it means to win a match.</summary>
[TestClass]
public class VictoryTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private static readonly string[] Nobody = new string[0];
    private static readonly string[] One = { "Vela" };
    private static readonly string[] Two = { "Vela", "Rook" };

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static void Run(MatchHost host, string[] stillIn, int fromSecond, int toSecond)
    {
        for (int second = fromSecond; second <= toSecond; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second), stillIn);
        }
    }

    private static bool Recorded(MatchHost host, AuditRecordKind kind) =>
        host.Audit.Records.Any(record => record.Kind == kind);

    [TestMethod]
    public void A_match_with_two_players_still_in_it_is_not_over()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 90);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse();
        host.Telemetry(Noon + TimeSpan.FromSeconds(90), 2).State.Should().Be(MatchClockState.Running);
    }

    [TestMethod]
    public void The_wall_stops_the_moment_one_player_is_left()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 71);

        double whenWon = host.Zone(Noon + TimeSpan.FromSeconds(71)).Sphere.Radius;

        Run(host, One, 72, 200);

        host.Zone(Noon + TimeSpan.FromSeconds(200)).Sphere.Radius.Should().Be(whenWon,
            "the squeeze stopped where it was when the match was decided");
    }

    [TestMethod]
    public void Nothing_bites_a_winner()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 80);

        host.Telemetry(Noon + TimeSpan.FromSeconds(80), 1).State.Should().NotBe(MatchClockState.Running,
            "a decided match is not live, so the zone watch takes nobody's health");
    }

    [TestMethod]
    public void The_winner_gets_the_window_before_the_match_is_written_off()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 71);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse("the window has not run yet");

        Run(host, One, 72, 71 + (int)Window.TotalSeconds);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeTrue();
    }

    [TestMethod]
    public void The_last_one_standing_is_named()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 100);

        AuditRecord ended = host.Audit.Records.Single(record => record.Kind == AuditRecordKind.MatchEnded);

        ended.Player.Should().Be("Vela");
        Recorded(host, AuditRecordKind.ZoneClosed).Should().BeFalse("the zone never closed; somebody won inside it");
    }

    [TestMethod]
    public void A_match_everybody_loses_ends_too_and_names_nobody()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, Nobody, 71, 100);

        AuditRecord ended = host.Audit.Records.Single(record => record.Kind == AuditRecordKind.MatchEnded);

        ended.Player.Should().BeNull();
    }

    [TestMethod]
    public void The_wall_closing_on_two_of_them_does_not_end_the_match_before_the_fight_does()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 260);

        host.Zone(Noon + TimeSpan.FromSeconds(260)).Sphere.Radius.Should().Be(0, "the wall really is shut");
        Recorded(host, AuditRecordKind.ZoneClosed).Should().BeTrue("the collapse still happened");
        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse("two of them are still fighting in it");

        Run(host, One, 261, 275);

        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.MatchEnded)
            .Player.Should().Be("Vela");
    }

    [TestMethod]
    public void A_wall_that_closes_on_a_crowd_writes_them_off_after_the_window_it_gave_them()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 269);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse("the window the fight gets has not run");

        Run(host, Two, 270, 275);

        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.MatchEnded)
            .Player.Should().BeNull("the wall took the pair of them");
    }

    [TestMethod]
    public void Being_the_only_person_who_ever_joined_is_not_winning()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, One, 1, 200);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse();
        host.Telemetry(Noon + TimeSpan.FromSeconds(200), 1).State.Should().Be(MatchClockState.Running);
    }

    [TestMethod]
    public void An_empty_server_does_not_win_its_own_match()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Nobody, 1, 100);

        Recorded(host, AuditRecordKind.MatchEnded).Should().BeFalse();
    }

    [TestMethod]
    public void A_decided_match_refuses_to_be_resumed()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 80);

        string answer = host.Command("resume", Noon + TimeSpan.FromSeconds(81));

        answer.Should().Contain("decided");
        host.Zone(Noon + TimeSpan.FromSeconds(140)).Sphere.Radius
            .Should().Be(host.Zone(Noon + TimeSpan.FromSeconds(81)).Sphere.Radius);
    }

    [TestMethod]
    public void A_decided_match_refuses_to_be_skipped()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 80);

        host.Command("skip", Noon + TimeSpan.FromSeconds(81)).Should().Contain("decided");
    }

    [TestMethod]
    public void A_reset_puts_the_group_back_on_the_board()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 100);

        host.Command("reset", Noon + TimeSpan.FromSeconds(101));
        host.Command("start", Noon + TimeSpan.FromSeconds(102));

        host.Telemetry(Noon + TimeSpan.FromSeconds(103), 2).State.Should().Be(MatchClockState.Running);
        host.Telemetry(Noon + TimeSpan.FromSeconds(103), 2).Generation.Should().Be(2);
    }

    [TestMethod]
    public void A_match_is_only_won_once()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 70);
        Run(host, One, 71, 300);

        host.Audit.Records.Count(record => record.Kind == AuditRecordKind.MatchEnded).Should().Be(1);
    }

    [TestMethod]
    public void Winning_before_a_phase_fires_still_stops_the_clock()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Run(host, Two, 1, 5);
        Run(host, One, 6, 200);

        Recorded(host, AuditRecordKind.PhaseStarted).Should().BeFalse("nothing fired after the match was decided");
    }
}
