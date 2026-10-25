using System;
using System.IO;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The tournament table folded off the host's own log.</summary>
[TestClass]
public class MatchHostStandingsTests
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

    [TestMethod]
    public void A_tournament_nobody_has_finished_a_match_in_has_no_table()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Tick(host, Noon, Reading(1, "Vela"), Reading(2, "Rook"));

        host.Standings(At(30)).Should().BeNull();
    }

    [TestMethod]
    public void A_finished_match_makes_a_table_the_booth_can_draw()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");

        StandingsTelemetry table = host.Standings(At(25))!;

        table.Matches.Should().Be(1);
        table.Entries.Should().HaveCount(2);
        table.Entries[0].Name.Should().Be("Vela", "leader first, and the order is the table");
        table.Entries[0].Wins.Should().Be(1);
        table.Entries[0].Outlasted.Should().Be(1);
        table.Entries[0].Points.Should().Be(1, "one player outlasted, at a point each");
        table.Entries[1].Name.Should().Be("Rook");
        table.Entries[1].Points.Should().Be(0, "going out first pays nothing");
    }

    [TestMethod]
    public void Every_row_says_what_its_total_is_made_of()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");

        foreach (StandingsLine line in host.Standings(At(25))!.Entries)
        {
            line.Points.Should().Be(line.Outlasted + 2 * line.Kills,
                $"{line.Name}'s columns have to add up to their total");
        }
    }

    [TestMethod]
    public void The_group_being_played_is_not_in_it()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");

        host.Command("next", At(30));
        host.Command("start", At(31));
        Tick(host, At(31), Reading(3, "Quill"), Reading(4, "Fenris"));
        Tick(host, At(36), Reading(3, "Quill"), Reading(4, "Fenris", alive: false));

        StandingsTelemetry table = host.Standings(At(40))!;

        table.Matches.Should().Be(1, "group 2 has not ended");
        table.Entries.Should().HaveCount(2, "so nobody in group 2 is on the table yet");
    }

    [TestMethod]
    public void A_second_finished_match_accumulates()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");

        host.Command("next", At(30));
        PlayOut(host, 31, winner: "Vela", loser: "Fenris");

        StandingsTelemetry table = host.Standings(At(60))!;

        table.Matches.Should().Be(2);
        table.Entries[0].Name.Should().Be("Vela");
        table.Entries[0].Played.Should().Be(2);
        table.Entries[0].Wins.Should().Be(2);
        table.Entries[0].Points.Should().Be(2, "a player outlasted in each");
    }

    [TestMethod]
    public void Asking_twice_with_nothing_written_in_between_is_the_same_table()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");

        host.Standings(At(25)).Should().BeSameAs(host.Standings(At(26)));
    }

    [TestMethod]
    public void A_reset_of_the_only_finished_match_leaves_no_table_at_all()
    {
        MatchHost host = NewHost();
        PlayOut(host, 0, winner: "Vela", loser: "Rook");
        host.Standings(At(25)).Should().NotBeNull();

        host.Command("reset", At(30));

        host.Standings(At(31)).Should().BeNull();
    }
}
