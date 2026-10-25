using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>One audit line; a record normalises itself so writing and reading it is an exact round trip.</summary>
[TestClass]
public class AuditRecordTests
{
    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly MatchStamp FirstMatch = new MatchStamp(1, 1, TimeSpan.FromMinutes(3));

    [TestMethod]
    public void A_record_drops_the_time_its_line_cannot_carry()
    {
        MatchStamp stamp = new MatchStamp(1, 1, TimeSpan.FromTicks(12345678));
        AuditRecord record = new AuditRecord(1, Noon.AddTicks(9999), stamp, AuditRecordKind.MatchStarted);

        record.Elapsed.Should().Be(TimeSpan.FromMilliseconds(1234));
        record.TimestampUtc.Should().Be(Noon);
    }

    [TestMethod]
    public void A_record_drops_the_precision_its_line_cannot_carry()
    {
        AuditRecord record = new AuditRecord(1, Noon, FirstMatch, AuditRecordKind.ZoneBite,
            player: "Fenris",
            position: new WorldPoint(712.56789, -108.00049, 455),
            amount: 8.98765,
            health: 44.5);

        record.Position.Should().Be(new WorldPoint(712.567, -108.000, 455));
        record.Amount.Should().Be(8.987);
        record.Health.Should().Be(44.5);
    }

    [TestMethod]
    public void A_record_stamped_in_a_local_timezone_is_kept_in_utc()
    {
        AuditRecord record = new AuditRecord(1, new DateTimeOffset(2026, 10, 24, 14, 0, 0, TimeSpan.FromHours(2)),
            FirstMatch, AuditRecordKind.MatchStarted);

        record.TimestampUtc.Should().Be(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
        record.TimestampUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_record_reads_its_group_generation_and_elapsed_off_the_match_it_belongs_to()
    {
        AuditRecord record = new AuditRecord(4, Noon, new MatchStamp(3, 7, TimeSpan.FromMinutes(2)),
            AuditRecordKind.PhaseStarted, phase: 2);

        record.Group.Should().Be(3);
        record.Generation.Should().Be(7);
        record.Elapsed.Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void A_record_about_a_player_needs_a_name()
    {
        Action bitingNobody = () => new AuditRecord(1, Noon, FirstMatch, AuditRecordKind.ZoneBite, amount: 8);

        bitingNobody.Should().Throw<ArgumentException>().WithMessage("*ZoneBite*player*");
    }

    [TestMethod]
    public void A_player_name_of_nothing_but_spaces_is_no_name_at_all()
    {
        Action blank = () => new AuditRecord(1, Noon, FirstMatch, AuditRecordKind.PlayerEliminated, player: "   ");

        blank.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void A_match_level_record_needs_no_player_but_may_name_one()
    {
        AuditRecord ended = new AuditRecord(1, Noon, FirstMatch, AuditRecordKind.MatchEnded, player: "Fenris");

        ended.Player.Should().Be("Fenris");
        new AuditRecord(2, Noon, FirstMatch, AuditRecordKind.MatchEnded).Player.Should().BeNull();
    }

    [TestMethod]
    public void Sequence_numbers_start_at_one()
    {
        Action unnumbered = () => new AuditRecord(0, Noon, FirstMatch, AuditRecordKind.MatchStarted);

        unnumbered.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Groups_and_generations_start_at_one_and_match_time_does_not_run_backwards()
    {
        Action noGroup = () => new MatchStamp(0, 1, TimeSpan.Zero);
        Action noGeneration = () => new MatchStamp(1, 0, TimeSpan.Zero);
        Action backwards = () => new MatchStamp(1, 1, TimeSpan.FromSeconds(-1));

        noGroup.Should().Throw<ArgumentOutOfRangeException>();
        noGeneration.Should().Throw<ArgumentOutOfRangeException>();
        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void The_sixteen_kinds_that_are_about_a_player_are_the_ones_that_name_one()
    {
        AuditRecordKind[] aboutAPlayer =
        {
            AuditRecordKind.PlayerJoined, AuditRecordKind.PlayerLeft, AuditRecordKind.PlayerPosition,
            AuditRecordKind.ZoneWarned, AuditRecordKind.ZoneBite, AuditRecordKind.ZoneArrears,
            AuditRecordKind.PlayerHurt, AuditRecordKind.PlayerEliminated,
            AuditRecordKind.KillAttributed, AuditRecordKind.RingFired, AuditRecordKind.PlayerCaged,
            AuditRecordKind.PlayerReleased, AuditRecordKind.GameModeCorrected,
            AuditRecordKind.BeaconRefused, AuditRecordKind.PlayerReady,
        };

        foreach (AuditRecordKind kind in Enum.GetValues(typeof(AuditRecordKind)).Cast<AuditRecordKind>())
        {
            AuditRecord.IsAboutAPlayer(kind).Should().Be(aboutAPlayer.Contains(kind), $"{kind} is classified");
        }
    }

    [TestMethod]
    public void Every_kind_is_classified_deliberately()
    {
        Enum.GetValues(typeof(AuditRecordKind)).Length.Should().Be(30);
    }
}
