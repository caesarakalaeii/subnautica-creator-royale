using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Results;

/// <summary>The standings table: ledgers folded into one order, a function of the audit log and the scoring rules.</summary>
[TestClass]
public class StandingsTests
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
    public void The_default_pays_a_point_per_player_outlasted_and_two_per_kill()
    {
        ScoringRules rules = ScoringRules.Default;

        rules.PointsPerPlayerOutlasted.Should().Be(1);
        rules.PointsPerKill.Should().Be(2);
        rules.PointsByPlacement.Should().BeEmpty("a placing pays nothing on top of the players it took to get there");
    }

    [TestMethod]
    public void An_attributed_kill_is_worth_two_and_lands_on_the_killer()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.KillAttributed, player: "Rook", actor: "Vela");

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Entry("Vela")!.Points.Should().Be(2 + 2, "two outlasted and one kill");
        standings.Entry("Rook")!.Points.Should().Be(1, "being killed costs nothing beyond going out");
    }

    [TestMethod]
    public void A_placement_off_the_end_of_the_table_scores_nothing()
    {
        ScoringRules four = new ScoringRules(new[] { 5, 3, 2, 1 });

        four.PointsFor(4).Should().Be(1);
        four.PointsFor(5).Should().Be(0);
        four.PointsFor(9).Should().Be(0);
        ScoringRules.Default.PointsFor(9).Should().Be(0);
    }

    [TestMethod]
    public void A_table_with_nothing_in_it_is_refused()
    {
        Action empty = () => new ScoringRules(new int[0]);

        empty.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void What_a_player_did_in_a_match_carries_into_the_standings()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris" });
        Record(AuditRecordKind.ZoneBite, player: "Fenris", amount: 8, health: 92);
        Record(AuditRecordKind.RingFired, player: "Vela");
        Record(AuditRecordKind.PlayerCaged, player: "Fenris", actor: "Vela");

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Entry("Vela")!.Points.Should().Be(1, "she outlasted one player and killed nobody");
        standings.Entry("Vela")!.Wins.Should().Be(1);
        standings.Entry("Vela")!.MatchesPlayed.Should().Be(1);
        standings.Entry("Vela")!.BestPlacement.Should().Be(1);
        standings.Entry("Vela")!.Catches.Should().Be(1);
        standings.Entry("Fenris")!.DamageTaken.Should().Be(8);
        standings.Entry("Fenris")!.Placements.Should().Equal(2);
    }

    [TestMethod]
    public void Points_add_up_across_the_groups()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Group(2, won: "Rook", eliminated: new[] { "Vela" }, generation: 2);

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Matches.Should().Be(2);
        standings.Entry("Rook")!.MatchesPlayed.Should().Be(2);
        standings.Entry("Rook")!.Points.Should().Be(1 + 1, "second of three, then first of two");
        standings.Entry("Rook")!.Placements.Should().Equal(2, 1);
        standings.Entry("Vela")!.Points.Should().Be(2 + 0);
        standings.Entry("Fenris")!.Points.Should().Be(0, "he went out first and never outlasted anybody");
    }

    [TestMethod]
    public void Only_the_last_attempt_at_a_group_counts()
    {
        Group(1, won: "Vela", eliminated: new[] { "Rook" });
        Record(AuditRecordKind.MatchReset, generation: 2);
        Group(1, won: "Rook", eliminated: new[] { "Vela" }, generation: 2);

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Matches.Should().Be(1);
        standings.Entry("Rook")!.Points.Should().Be(1);
        standings.Entry("Vela")!.Points.Should().Be(0);
    }

    [TestMethod]
    public void A_shared_first_place_pays_both_players_a_win()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PlayerJoined, player: "Vela");
        Record(AuditRecordKind.PlayerJoined, player: "Rook");

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Entry("Vela")!.Points.Should().Be(0);
        standings.Entry("Rook")!.Points.Should().Be(0);
        standings.Entry("Rook")!.Wins.Should().Be(1, "a shared first place is still a first place");
    }

    [TestMethod]
    public void Level_on_points_the_one_who_won_more_groups_is_above()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Group(2, won: "Tarn", eliminated: new[] { "Fenris", "Rook" }, generation: 2);

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Entries.Select(e => e.Name).Should().Equal("Tarn", "Vela", "Rook", "Fenris");
        standings.Entries.Select(e => e.Points).Should().Equal(2, 2, 2, 0);
        standings.Entry("Vela")!.Wins.Should().Be(1);
        standings.Entry("Rook")!.Wins.Should().Be(0);
        standings.Entry("Rook")!.BestPlacement.Should().Be(2, "the tie is on wins, not on the best result");
    }

    [TestMethod]
    public void Level_on_points_and_wins_the_better_single_result_is_above_and_then_it_is_the_name()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Tarn", "Sable", "Rook" });
        Group(2, won: "Vela", eliminated: new[] { "Rook", "Sable", "Tarn" }, generation: 2);

        Standings standings = Standings.Of(Replay(), ScoringRules.Default);

        standings.Entries.Select(e => e.Name).Should().Equal("Vela", "Rook", "Tarn", "Sable", "Fenris");
        standings.Entries.Select(e => e.Points).Should().Equal(7, 3, 3, 3, 0);
        standings.Entry("Rook")!.BestPlacement.Should().Be(2);
        standings.Entry("Sable")!.BestPlacement.Should().Be(3);
    }

    [TestMethod]
    public void The_same_log_gives_the_same_table_twice()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook", "Mira", "Tarn" });
        Group(2, won: "Tarn", eliminated: new[] { "Mira", "Rook", "Fenris", "Vela" }, generation: 2);

        Standings first = Standings.Of(Replay(), ScoringRules.Default);
        Standings again = Standings.Of(Replay(), ScoringRules.Default);

        again.Entries.Select(e => e.Name).Should().Equal(first.Entries.Select(e => e.Name));
        again.Entries.Select(e => e.Points).Should().Equal(first.Entries.Select(e => e.Points));
    }

    [TestMethod]
    public void The_ledgers_to_fold_are_the_callers_choice()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris" });
        Group(2, won: "Fenris", eliminated: new[] { "Vela" }, generation: 2);

        List<MatchLedger> firstGroupOnly = AuditReplay.Of(log.Records).Matches
            .Where(match => match.Group == 1)
            .Select(MatchLedger.Of)
            .ToList();

        Standings standings = Standings.Of(firstGroupOnly, ScoringRules.Default);

        standings.Matches.Should().Be(1);
        standings.Entry("Vela")!.Points.Should().Be(1);
        standings.Entry("Fenris")!.Points.Should().Be(0);
    }

    [TestMethod]
    public void Nobody_who_never_played_is_in_the_table()
    {
        Standings standings = Standings.Of(new MatchLedger[0], ScoringRules.Default);

        standings.Entries.Should().BeEmpty();
        standings.Entry("Vela").Should().BeNull();
        standings.Matches.Should().Be(0);
    }

    [TestMethod]
    public void The_broadcast_table_leaves_out_the_match_being_played()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.MatchStarted, group: 2);
        Record(AuditRecordKind.PlayerJoined, player: "Quill", group: 2);
        Record(AuditRecordKind.PlayerEliminated, player: "Quill", detail: "zone",
            at: TimeSpan.FromMinutes(3), group: 2);

        Standings finished = Standings.OfFinished(Replay(), ScoringRules.Default);

        finished.Matches.Should().Be(1, "group 2 has not ended");
        finished.Entry("Quill").Should().BeNull("nobody scores for a match still being played");
        Standings.Of(Replay(), ScoringRules.Default).Matches.Should().Be(2,
            "the unfiltered fold still takes every group, which is what an operator asks for");
    }

    [TestMethod]
    public void An_attempt_an_operator_threw_away_leaves_the_broadcast_table_until_it_is_replayed()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.MatchReset, group: 1, generation: 2);

        Standings.OfFinished(Replay(), ScoringRules.Default).Matches.Should().Be(0);

        Group(1, won: "Rook", eliminated: new[] { "Vela", "Fenris" }, generation: 2);

        Standings replayed = Standings.OfFinished(Replay(), ScoringRules.Default);

        replayed.Matches.Should().Be(1);
        replayed.Entries[0].Name.Should().Be("Rook", "the attempt that actually finished");
    }

    [TestMethod]
    public void Every_line_says_what_its_points_are_made_of()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.KillAttributed, player: "Rook", actor: "Vela");

        StandingsEntry vela = Standings.OfFinished(Replay(), ScoringRules.Default).Entry("Vela")!;

        vela.Outlasted.Should().Be(2);
        vela.Kills.Should().Be(1);
        vela.Points.Should().Be(vela.Outlasted + 2 * vela.Kills,
            "the columns have to add up to the total, or the table argues with itself on camera");
    }

    [TestMethod]
    public void What_a_player_played_and_what_the_tournament_played_are_different_numbers()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris" });
        Group(2, won: "Quill", eliminated: new[] { "Rook" });

        Standings standings = Standings.OfFinished(Replay(), ScoringRules.Default);

        standings.Matches.Should().Be(2);
        standings.Entry("Vela")!.MatchesPlayed.Should().Be(1);
    }

    [TestMethod]
    public void Each_finished_game_is_kept_whole_beside_the_table_and_adds_up_to_it()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.KillAttributed, player: "Rook", actor: "Vela");
        Group(2, won: "Rook", eliminated: new[] { "Vela" });

        Standings standings = Standings.OfFinished(Replay(), ScoringRules.Default);

        standings.Games.Select(game => game.Group).Should().Equal(1, 2);
        standings.Games[0].Lines[0].Name.Should().Be("Vela", "a game's lines are its ledger, winner first");
        standings.Games[0].Lines[0].Kills.Should().Be(1);

        foreach (StandingsEntry entry in standings.Entries)
        {
            standings.Games.SelectMany(game => game.Lines).Where(line => line.Name == entry.Name)
                .Sum(line => line.Points).Should().Be(entry.Points, $"{entry.Name}'s games add up to the table");
        }
    }

    [TestMethod]
    public void A_game_being_played_or_thrown_away_is_not_one_of_the_games()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });
        Record(AuditRecordKind.MatchReset, group: 1, generation: 2);
        Group(1, won: "Rook", eliminated: new[] { "Vela", "Fenris" }, generation: 2);
        Record(AuditRecordKind.MatchStarted, group: 2);
        Record(AuditRecordKind.PlayerJoined, player: "Quill", group: 2);

        Standings standings = Standings.OfFinished(Replay(), ScoringRules.Default);

        standings.Games.Should().ContainSingle();
        standings.Games[0].Generation.Should().Be(2, "only the attempt that counts");
        standings.Games[0].Lines[0].Name.Should().Be("Rook");
    }

    [TestMethod]
    public void Players_the_table_cannot_separate_share_a_place()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris" });
        Group(2, won: "Ada", eliminated: new[] { "Rook" });

        Standings standings = Standings.OfFinished(Replay(), ScoringRules.Default);

        standings.Entry("Ada")!.Place.Should().Be(1);
        standings.Entry("Vela")!.Place.Should().Be(1);
        standings.Entry("Fenris")!.Place.Should().Be(3, "placed as though the tie above had not been one");
        standings.Entry("Rook")!.Place.Should().Be(3);
    }

    [TestMethod]
    public void The_leader_is_first_and_alone_when_something_separates_them()
    {
        Group(1, won: "Vela", eliminated: new[] { "Fenris", "Rook" });

        Standings standings = Standings.OfFinished(Replay(), ScoringRules.Default);

        standings.Entries[0].Place.Should().Be(1);
        standings.Entries[0].Name.Should().Be("Vela");
        standings.Entries[1].Place.Should().Be(2);
    }

    private AuditReplay Replay() => AuditReplay.Of(log.Records);

    private void Group(int group, string won, string[] eliminated, int generation = 1)
    {
        Record(AuditRecordKind.MatchStarted, group: group, generation: generation);
        Record(AuditRecordKind.PlayerJoined, player: won, group: group, generation: generation);
        foreach (string name in eliminated)
        {
            Record(AuditRecordKind.PlayerJoined, player: name, group: group, generation: generation);
        }

        int minute = 4;
        foreach (string name in eliminated)
        {
            Record(AuditRecordKind.PlayerEliminated, player: name, detail: "zone",
                at: TimeSpan.FromMinutes(minute), group: group, generation: generation);
            minute += 2;
        }

        Record(AuditRecordKind.MatchEnded, player: won, at: TimeSpan.FromMinutes(minute),
            group: group, generation: generation);
    }

    private void Record(AuditRecordKind kind, string? player = null, string? actor = null,
        double? amount = null, double? health = null, string? detail = null, TimeSpan? at = null,
        int group = 1, int generation = 1)
    {
        TimeSpan elapsed = at ?? TimeSpan.Zero;

        log.Append(Noon + elapsed, new MatchStamp(group, generation, elapsed), kind,
            player: player, actor: actor, amount: amount, health: health, detail: detail);
    }
}
