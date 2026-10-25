using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>How the roster restores a verdict the server hands it when a player rejoins.</summary>
[TestClass]
public class PlayerRosterRejoinTests
{
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(0, 0, 0), 100);

    private static PlayerReading Reading(ushort id = 1, string name = "Vela") =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 92, 40, 70, 65);

    private static IReadOnlyList<AuditRecordKind> Changes(PlayerRoster roster, int generation,
        params PlayerReading[] readings) =>
        roster.Update(readings, Wall, generation).Changes.Select(change => change.Kind).ToList();

    [TestMethod]
    public void A_player_the_server_says_is_out_is_out_whatever_their_fresh_client_reports()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        roster.Update(new[] { Reading() }, Wall, 1).Rows[0].Alive.Should().BeFalse();
    }

    [TestMethod]
    public void And_the_alive_count_does_not_move_when_they_come_back()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        IReadOnlyList<PlayerTelemetry> rows = roster.Update(
            new[] { Reading(1, "Vela"), Reading(2, "Rook") }, Wall, 1).Rows;

        PlayerRoster.Alive(rows).Should().Be(1);
    }

    [TestMethod]
    public void The_record_is_not_filed_twice_because_the_standings_are_a_fold_over_the_log()
    {
        PlayerRoster roster = new PlayerRoster();

        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Changes(roster, 1, Reading()).Should().Equal(
            AuditRecordKind.PlayerJoined, AuditRecordKind.PlayerEliminated);

        Changes(roster, 1).Should().Equal(AuditRecordKind.PlayerLeft);

        roster.Out("Vela", 1);

        Changes(roster, 1, Reading(9)).Should().Equal(AuditRecordKind.PlayerJoined);
    }

    [TestMethod]
    public void And_it_holds_for_the_second_and_the_eighth_rejoin()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        for (ushort id = 1; id <= 8; id++)
        {
            roster.Update(new[] { Reading(id) }, Wall, 1).Rows[0].Alive.Should().BeFalse();
            Changes(roster, 1).Should().Equal(AuditRecordKind.PlayerLeft);
        }
    }

    [TestMethod]
    public void A_new_attempt_at_the_match_leaves_it_behind_because_that_is_what_a_reset_means()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        roster.Update(new[] { Reading() }, Wall, 1).Rows[0].Alive.Should().BeFalse();

        roster.Update(new[] { Reading() }, Wall, 2).Rows[0].Alive.Should().BeTrue();
    }

    [TestMethod]
    public void A_verdict_lands_even_when_it_arrives_before_the_tick_that_notices_the_match()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 4);

        roster.Update(new[] { Reading() }, Wall, 4).Rows[0].Alive.Should().BeFalse();
    }

    [TestMethod]
    public void And_the_next_elimination_in_the_next_attempt_is_recorded_again()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);
        Changes(roster, 1, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);

        Changes(roster, 2, Reading()).Should().Equal(AuditRecordKind.PlayerJoined);

        roster.Reported(1, new PlayerReport(true, null, null, null, null));

        Changes(roster, 2, Reading()).Should().Equal(AuditRecordKind.PlayerEliminated);
    }

    [TestMethod]
    public void It_is_about_one_name_and_nobody_else()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        IReadOnlyList<PlayerTelemetry> rows = roster.Update(
            new[] { Reading(1, "Vela"), Reading(2, "Rook") }, Wall, 1).Rows;

        rows.Single(row => row.Name == "Vela").Alive.Should().BeFalse();
        rows.Single(row => row.Name == "Rook").Alive.Should().BeTrue();
    }

    [TestMethod]
    public void The_name_is_matched_exactly_the_way_the_log_matches_it()
    {
        PlayerRoster roster = new PlayerRoster();
        roster.Out("Vela", 1);

        roster.Update(new[] { Reading(1, "vela") }, Wall, 1).Rows[0].Alive.Should().BeTrue();
        roster.Update(new[] { Reading(1, "Vela") }, Wall, 1).Rows[0].Alive.Should().BeFalse();
    }

    [TestMethod]
    public void A_verdict_about_nobody_is_refused_rather_than_held_against_an_empty_name()
    {
        PlayerRoster roster = new PlayerRoster();

        roster.Invoking(r => r.Out(null!, 1)).Should().Throw<System.ArgumentException>();
        roster.Invoking(r => r.Out(" ", 1)).Should().Throw<System.ArgumentException>();
    }
}
