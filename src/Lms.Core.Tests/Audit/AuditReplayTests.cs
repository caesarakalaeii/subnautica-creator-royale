using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>Folding the audit log back into match state: the file alone says who was where and who went out when.</summary>
[TestClass]
public class AuditReplayTests
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
    public void A_replay_of_nothing_has_no_match_in_it()
    {
        AuditReplay replay = AuditReplay.Of(new AuditRecord[0]);

        replay.Matches.Should().BeEmpty();
        replay.Match.Should().BeNull();
        replay.Sequence.Should().Be(0);
    }

    [TestMethod]
    public void A_started_match_is_running_and_a_paused_one_is_paused()
    {
        Record(AuditRecordKind.MatchStarted);
        AuditReplay.Of(log.Records).Match!.State.Should().Be(MatchClockState.Running);

        Record(AuditRecordKind.MatchPaused);
        AuditReplay.Of(log.Records).Match!.State.Should().Be(MatchClockState.Paused);

        Record(AuditRecordKind.MatchResumed);
        AuditReplay.Of(log.Records).Match!.State.Should().Be(MatchClockState.Running);
    }

    [TestMethod]
    public void A_phase_that_fired_counts_and_the_warning_before_it_does_not()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PhaseWarned, phase: 1);
        Record(AuditRecordKind.PhaseStarted, phase: 1);
        Record(AuditRecordKind.PhaseWarned, phase: 2);

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.PhasesFired.Should().Be(1);
        match.IsClosed.Should().BeFalse();
    }

    [TestMethod]
    public void A_closed_zone_is_the_end_of_the_squeeze()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.ZoneClosed, at: TimeSpan.FromMinutes(20));

        AuditReplay.Of(log.Records).Match!.IsClosed.Should().BeTrue();
    }

    [TestMethod]
    public void Match_time_is_as_far_as_the_last_record_got()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PhaseStarted, phase: 1, at: TimeSpan.FromMinutes(5));

        AuditReplay.Of(log.Records).Match!.Elapsed.Should().Be(TimeSpan.FromMinutes(5));
    }

    [TestMethod]
    public void A_reset_is_a_second_attempt_at_the_same_group()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone");
        Record(AuditRecordKind.MatchReset, generation: 2);
        Record(AuditRecordKind.PlayerPosition, player: "Fenris", generation: 2,
            position: new WorldPoint(700, -100, 450));

        AuditReplay replay = AuditReplay.Of(log.Records);

        replay.Matches.Should().HaveCount(2);
        replay.Matches.Select(m => m.Group).Should().Equal(1, 1);
        replay.Matches.Select(m => m.Generation).Should().Equal(1, 2);
        replay.Matches[0].Player("Fenris")!.IsAlive.Should().BeFalse();
        replay.Match!.Player("Fenris")!.IsAlive.Should().BeTrue();
    }

    [TestMethod]
    public void The_next_group_is_a_new_match()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.GroupAdvanced, group: 2, generation: 2);

        AuditReplay replay = AuditReplay.Of(log.Records);

        replay.Matches.Should().HaveCount(2);
        replay.Match!.Group.Should().Be(2);
        replay.Match.State.Should().Be(MatchClockState.NotStarted, "a new match is waiting in the lobby");
    }

    [TestMethod]
    public void A_player_joins_a_match_the_first_time_it_mentions_them()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PlayerPosition, player: "Vela", position: new WorldPoint(1, -2, 3));

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.Players.Select(p => p.Name).Should().Equal("Vela");
        match.Player("Vela")!.IsAlive.Should().BeTrue();
        match.Player("nobody").Should().BeNull();
    }

    [TestMethod]
    public void Where_everybody_was_is_the_last_place_they_were_seen()
    {
        Record(AuditRecordKind.PlayerPosition, player: "Vela", position: new WorldPoint(1, -2, 3),
            biome: "kelpForest");
        Record(AuditRecordKind.PlayerPosition, player: "Vela", position: new WorldPoint(700, -108, 455),
            biome: "grandReef", at: TimeSpan.FromMinutes(2));

        PlayerAudit vela = AuditReplay.Of(log.Records).Match!.Player("Vela")!;

        vela.LastPosition.Should().Be(new WorldPoint(700, -108, 455));
        vela.LastBiome.Should().Be("grandReef");
        vela.LastSeen.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void The_order_players_went_out_in_is_the_order_the_log_says()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone",
            at: TimeSpan.FromMinutes(4));
        Record(AuditRecordKind.PlayerEliminated, player: "Vela", detail: "creature",
            at: TimeSpan.FromMinutes(9));
        Record(AuditRecordKind.PlayerPosition, player: "Rook", position: new WorldPoint(1, -2, 3));

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.EliminationOrder.Select(p => p.Name).Should().Equal("Fenris", "Vela");
        match.Player("Fenris")!.EliminationOrder.Should().Be(1);
        match.Player("Fenris")!.EliminatedAt.Should().Be(TimeSpan.FromMinutes(4));
        match.Player("Fenris")!.Cause.Should().Be("zone");
        match.Player("Vela")!.EliminationOrder.Should().Be(2);
        match.Player("Rook")!.EliminationOrder.Should().Be(0, "still standing");
        match.Survivors.Select(p => p.Name).Should().Equal("Rook");
    }

    [TestMethod]
    public void A_death_told_twice_does_not_move_anybody_elses_placement()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone",
            at: TimeSpan.FromMinutes(4));
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "creature",
            at: TimeSpan.FromMinutes(4) + TimeSpan.FromMilliseconds(300));
        Record(AuditRecordKind.PlayerEliminated, player: "Vela", detail: "zone",
            at: TimeSpan.FromMinutes(5));

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.EliminationOrder.Select(p => p.Name).Should().Equal("Fenris", "Vela");
        match.Player("Vela")!.EliminationOrder.Should().Be(2);
        match.Player("Fenris")!.Cause.Should().Be("zone", "the first telling is the deciding one");
    }

    [TestMethod]
    public void A_phase_line_lost_to_a_crash_does_not_shift_the_phases_after_it()
    {
        Record(AuditRecordKind.PhaseStarted, phase: 1, at: TimeSpan.FromMinutes(5));
        Record(AuditRecordKind.PhaseStarted, phase: 3, at: TimeSpan.FromMinutes(15));

        AuditReplay.Of(log.Records).Match!.PhasesFired.Should().Be(3);
    }

    [TestMethod]
    public void What_the_zone_took_adds_up_and_the_last_health_recorded_is_the_last_word()
    {
        Record(AuditRecordKind.ZoneWarned, player: "Fenris");
        Record(AuditRecordKind.ZoneBite, player: "Fenris", amount: 8, health: 92);
        Record(AuditRecordKind.ZoneBite, player: "Fenris", amount: 8, health: 84);
        Record(AuditRecordKind.ZoneBite, player: "Fenris", amount: 20, health: 64);

        PlayerAudit fenris = AuditReplay.Of(log.Records).Match!.Player("Fenris")!;

        fenris.Bites.Should().Be(3);
        fenris.DamageTaken.Should().Be(36);
        fenris.Warnings.Should().Be(1);
        fenris.Health.Should().Be(64);
    }

    [TestMethod]
    public void A_ring_that_catches_somebody_counts_for_both_of_them()
    {
        Record(AuditRecordKind.RingFired, player: "Vela", position: new WorldPoint(700, -100, 450));
        Record(AuditRecordKind.PlayerCaged, player: "Fenris", actor: "Vela");
        Record(AuditRecordKind.PlayerReleased, player: "Fenris");

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.Player("Vela")!.RingsFired.Should().Be(1);
        match.Player("Vela")!.Catches.Should().Be(1, "the shooter caught somebody with it");
        match.Player("Fenris")!.TimesCaged.Should().Be(1);
    }

    [TestMethod]
    public void Somebody_who_disconnects_is_not_standing_at_the_end_and_was_not_eliminated_either()
    {
        Record(AuditRecordKind.PlayerJoined, player: "Rook");
        Record(AuditRecordKind.PlayerLeft, player: "Rook", at: TimeSpan.FromMinutes(6));

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.Player("Rook")!.HasLeft.Should().BeTrue();
        match.Player("Rook")!.IsAlive.Should().BeTrue();
        match.Player("Rook")!.LeftAt.Should().Be(TimeSpan.FromMinutes(6));
        match.Survivors.Should().BeEmpty();
    }

    [TestMethod]
    public void The_end_of_a_match_names_its_winner()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone");
        Record(AuditRecordKind.MatchEnded, player: "Rook", at: TimeSpan.FromMinutes(12));

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.HasEnded.Should().BeTrue();
        match.Winner.Should().Be("Rook");
    }

    [TestMethod]
    public void A_match_that_ended_with_nobody_named_has_no_winner()
    {
        Record(AuditRecordKind.MatchStarted);
        Record(AuditRecordKind.MatchEnded);

        MatchAudit match = AuditReplay.Of(log.Records).Match!;

        match.HasEnded.Should().BeTrue();
        match.Winner.Should().BeNull();
    }

    [TestMethod]
    public void A_log_that_goes_backwards_is_refused()
    {
        AuditReplay replay = new AuditReplay();
        replay.Apply(Record(AuditRecordKind.MatchStarted));

        Action backwards = () => replay.Apply(new AuditRecord(1, Noon, new MatchStamp(1, 1, TimeSpan.Zero),
            AuditRecordKind.MatchPaused));

        backwards.Should().Throw<InvalidOperationException>().WithMessage("*append-only*");
    }

    [TestMethod]
    public void A_record_seen_twice_is_refused()
    {
        AuditReplay replay = new AuditReplay();
        AuditRecord started = Record(AuditRecordKind.MatchStarted);
        replay.Apply(started);

        Action again = () => replay.Apply(started);

        again.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void A_whole_match_is_reconstructed_from_its_file_alone()
    {
        string[] names = { "Fenris", "Vela", "Rook", "Mira", "Tarn" };
        Record(AuditRecordKind.MatchStarted);
        foreach (string name in names)
        {
            Record(AuditRecordKind.PlayerJoined, player: name);
        }

        Record(AuditRecordKind.PhaseStarted, phase: 1, at: TimeSpan.FromMinutes(5));
        Record(AuditRecordKind.ZoneBite, player: "Fenris", amount: 3, health: 97,
            at: TimeSpan.FromMinutes(5));
        Record(AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone", health: 0,
            at: TimeSpan.FromMinutes(6));
        Record(AuditRecordKind.RingFired, player: "Vela", at: TimeSpan.FromMinutes(8));
        Record(AuditRecordKind.PlayerCaged, player: "Mira", actor: "Vela", at: TimeSpan.FromMinutes(8));
        Record(AuditRecordKind.PlayerEliminated, player: "Mira", detail: "creature",
            at: TimeSpan.FromMinutes(9));
        Record(AuditRecordKind.PhaseStarted, phase: 2, at: TimeSpan.FromMinutes(10));
        Record(AuditRecordKind.PlayerEliminated, player: "Tarn", detail: "zone",
            at: TimeSpan.FromMinutes(11));
        Record(AuditRecordKind.PlayerEliminated, player: "Rook", detail: "drowned",
            at: TimeSpan.FromMinutes(12));
        Record(AuditRecordKind.MatchEnded, player: "Vela", at: TimeSpan.FromMinutes(12));

        List<AuditRecord> fromFile = new List<AuditRecord>();
        foreach (string line in log.Records.Select(AuditFormat.Write))
        {
            AuditFormat.TryRead(line, out AuditRecord record, out _).Should().BeTrue();
            fromFile.Add(record);
        }

        MatchAudit match = AuditReplay.Of(fromFile).Match!;

        match.Group.Should().Be(1);
        match.PhasesFired.Should().Be(2);
        match.HasEnded.Should().BeTrue();
        match.Winner.Should().Be("Vela");
        match.Players.Should().HaveCount(5);
        match.EliminationOrder.Select(p => p.Name).Should().Equal("Fenris", "Mira", "Tarn", "Rook");
        match.Survivors.Select(p => p.Name).Should().Equal("Vela");
        match.Player("Vela")!.RingsFired.Should().Be(1);
        match.Player("Mira")!.TimesCaged.Should().Be(1);
        match.Player("Fenris")!.DamageTaken.Should().Be(3);
        match.Elapsed.Should().Be(TimeSpan.FromMinutes(12));
    }

    private AuditRecord Record(AuditRecordKind kind, string? player = null, string? actor = null,
        int? phase = null, WorldPoint? position = null, string? biome = null, double? amount = null,
        double? health = null, string? detail = null, TimeSpan? at = null, int group = 1,
        int generation = 1)
    {
        TimeSpan elapsed = at ?? TimeSpan.Zero;

        return log.Append(Noon + elapsed, new MatchStamp(group, generation, elapsed), kind,
            player, actor, phase, position, biome, amount, health, detail);
    }
}
