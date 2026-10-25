using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Results;

/// <summary>Placement within one match, read off the audit log.</summary>
[TestClass]
public class MatchLedgerTests
{
    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private AuditLog log = new AuditLog();

    [TestInitialize]
    public void NewLog()
    {
        log = new AuditLog();
    }

    [TestMethod]
    public void A_match_nobody_played_has_nobody_in_it()
    {
        Record(AuditRecordKind.MatchStarted);

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Should().BeEmpty();
        ledger.Winner.Should().BeNull();
        ledger.Group.Should().Be(1);
    }

    [TestMethod]
    public void The_last_player_standing_takes_first_and_the_rest_run_back_up_the_order_they_went_out_in()
    {
        Seen("Fenris", "Vela", "Rook", "Mira", "Tarn");
        Eliminate("Fenris", 4);
        Eliminate("Mira", 6);
        Eliminate("Tarn", 9);
        Eliminate("Rook", 11);

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook", "Tarn", "Mira", "Fenris");
        ledger.Entries.Select(e => e.Placement).Should().Equal(1, 2, 3, 4, 5);
        ledger.Winner.Should().Be("Vela");
        ledger.Entry("Vela")!.Survived.Should().BeTrue();
        ledger.Entry("Fenris")!.Survived.Should().BeFalse();
        ledger.Entry("Fenris")!.ExitedAt.Should().Be(TimeSpan.FromMinutes(4));
    }

    [TestMethod]
    public void A_zone_that_closes_on_everybody_still_leaves_the_last_one_out_on_top()
    {
        Seen("Vela", "Rook");
        Eliminate("Rook", 12);
        Eliminate("Vela", 12, seconds: 30);
        Record(AuditRecordKind.ZoneClosed, at: TimeSpan.FromMinutes(13));

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook");
        ledger.Winner.Should().Be("Vela", "last out is best placed");
    }

    [TestMethod]
    public void Two_players_still_standing_share_first_place_and_the_next_one_is_third()
    {
        Seen("Vela", "Rook", "Fenris");
        Eliminate("Fenris", 5);

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entry("Vela")!.Placement.Should().Be(1);
        ledger.Entry("Rook")!.Placement.Should().Be(1);
        ledger.Entry("Fenris")!.Placement.Should().Be(3);
        ledger.Winner.Should().BeNull("a shared first place names nobody");
    }

    [TestMethod]
    public void An_ending_that_names_a_winner_puts_them_ahead_of_the_others_left_standing()
    {
        Seen("Vela", "Rook");
        Record(AuditRecordKind.MatchEnded, player: "Rook", at: TimeSpan.FromMinutes(12));

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Winner.Should().Be("Rook");
        ledger.Entry("Rook")!.Placement.Should().Be(1);
        ledger.Entry("Vela")!.Placement.Should().Be(2);
    }

    [TestMethod]
    public void Somebody_who_disconnects_is_placed_by_when_they_went_rather_than_scored_as_a_death()
    {
        Seen("Vela", "Rook", "Fenris");
        Eliminate("Fenris", 3);
        Record(AuditRecordKind.PlayerLeft, player: "Rook", at: TimeSpan.FromMinutes(7));

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook", "Fenris");
        ledger.Entry("Rook")!.Left.Should().BeTrue();
        ledger.Entry("Rook")!.Survived.Should().BeFalse();
        ledger.Entry("Rook")!.ExitedAt.Should().Be(TimeSpan.FromMinutes(7));
        ledger.Entry("Fenris")!.Left.Should().BeFalse();
    }

    [TestMethod]
    public void An_elimination_carries_what_did_it_into_the_ledger()
    {
        Seen("Vela", "Fenris");
        Eliminate("Fenris", 4, cause: "creature");

        MatchLedger.Of(Replay()).Entry("Fenris")!.Cause.Should().Be("creature");
    }

    [TestMethod]
    public void Two_players_the_log_cannot_tell_apart_are_ordered_by_name_rather_than_by_luck()
    {
        Seen("Vela", "Rook", "Mira");
        Record(AuditRecordKind.PlayerLeft, player: "Rook", at: TimeSpan.FromMinutes(7));
        Record(AuditRecordKind.PlayerLeft, player: "Mira", at: TimeSpan.FromMinutes(7));

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Mira", "Rook");
        ledger.Entries.Select(e => e.Placement).Should().Equal(new[] { 1, 2, 2 },
            "two exits the log cannot separate share a placement rather than being sorted by luck");
    }

    [TestMethod]
    public void A_ledger_read_back_off_the_file_is_the_same_ledger()
    {
        Seen("Vela", "Rook", "Fenris");
        Eliminate("Fenris", 4);
        Eliminate("Rook", 8);

        List<AuditRecord> fromFile = new List<AuditRecord>();
        foreach (string line in log.Records.Select(AuditFormat.Write))
        {
            AuditFormat.TryRead(line, out AuditRecord record, out _).Should().BeTrue();
            fromFile.Add(record);
        }

        MatchLedger ledger = MatchLedger.Of(AuditReplay.Of(fromFile).Match!);

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook", "Fenris");
        ledger.Entries.Select(e => e.Placement).Should().Equal(1, 2, 3);
    }

    [TestMethod]
    public void Somebody_who_left_the_lobby_before_the_start_is_not_in_the_match()
    {
        Record(AuditRecordKind.PlayerJoined, player: "Ethalia");
        Record(AuditRecordKind.PlayerLeft, player: "Ethalia");
        Seen("Vela", "Rook");
        Eliminate("Rook", 4);

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook");
        ledger.Entry("Vela")!.Outlasted.Should().Be(1);
    }

    [TestMethod]
    public void Somebody_who_left_the_lobby_and_came_back_after_the_start_is_in_the_match()
    {
        Record(AuditRecordKind.PlayerJoined, player: "Rook");
        Record(AuditRecordKind.PlayerLeft, player: "Rook");
        Seen("Vela");
        Record(AuditRecordKind.PlayerJoined, player: "Rook", at: TimeSpan.FromMinutes(1));
        Eliminate("Rook", 4);

        MatchLedger ledger = MatchLedger.Of(Replay());

        ledger.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook");
    }

    private MatchAudit Replay() => AuditReplay.Of(log.Records).Match!;

    private void Seen(params string[] names)
    {
        Record(AuditRecordKind.MatchStarted);
        foreach (string name in names)
        {
            Record(AuditRecordKind.PlayerJoined, player: name);
        }
    }

    private void Eliminate(string name, int minutes, int seconds = 0, string cause = "zone") =>
        Record(AuditRecordKind.PlayerEliminated, player: name, detail: cause,
            at: new TimeSpan(0, minutes, seconds));

    private void Record(AuditRecordKind kind, string? player = null, string? detail = null,
        TimeSpan? at = null, int group = 1, int generation = 1)
    {
        TimeSpan elapsed = at ?? TimeSpan.Zero;

        log.Append(Noon + elapsed, new MatchStamp(group, generation, elapsed), kind,
            player: player, detail: detail);
    }
}
