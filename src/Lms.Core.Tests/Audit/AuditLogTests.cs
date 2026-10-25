using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>The append-only audit log; it owns the sequence number and does no file I/O.</summary>
[TestClass]
public class AuditLogTests
{
    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly MatchStamp FirstMatch = new MatchStamp(1, 1, TimeSpan.FromMinutes(3));

    [TestMethod]
    public void An_empty_log_has_handed_out_no_numbers()
    {
        AuditLog log = new AuditLog();

        log.Sequence.Should().Be(0);
        log.Records.Should().BeEmpty();
    }

    [TestMethod]
    public void Sequence_numbers_start_at_one_and_climb_by_one()
    {
        AuditLog log = new AuditLog();

        log.Append(Noon, FirstMatch, AuditRecordKind.MatchStarted).Sequence.Should().Be(1);
        log.Append(Noon, FirstMatch, AuditRecordKind.PhaseWarned, phase: 1).Sequence.Should().Be(2);
        log.Append(Noon, FirstMatch, AuditRecordKind.PhaseStarted, phase: 1).Sequence.Should().Be(3);
        log.Sequence.Should().Be(3);
    }

    [TestMethod]
    public void The_log_keeps_what_it_stamped_in_the_order_it_was_appended()
    {
        AuditLog log = new AuditLog();

        AuditRecord started = log.Append(Noon, FirstMatch, AuditRecordKind.MatchStarted);
        AuditRecord bitten = log.Append(Noon.AddSeconds(2), FirstMatch, AuditRecordKind.ZoneBite,
            player: "Fenris", position: new WorldPoint(700, -100, 450), amount: 8, health: 92);

        log.Records.Should().HaveCount(2);
        log.Records[0].Should().Be(started);
        log.Records[1].Should().Be(bitten);
        log.Records[1].Player.Should().Be("Fenris");
    }

    [TestMethod]
    public void The_log_is_append_only_from_the_outside()
    {
        AuditLog log = new AuditLog();
        log.Append(Noon, FirstMatch, AuditRecordKind.MatchStarted);

        Action editing = () => ((IList<AuditRecord>)log.Records).Clear();

        editing.Should().Throw<NotSupportedException>();
    }

    [TestMethod]
    public void A_player_record_with_nobody_in_it_is_refused_at_the_door()
    {
        AuditLog log = new AuditLog();

        Action nameless = () => log.Append(Noon, FirstMatch, AuditRecordKind.PlayerCaged, actor: "Vela");

        nameless.Should().Throw<ArgumentException>();
        log.Records.Should().BeEmpty("a refused record is not a gap in the numbering");
        log.Sequence.Should().Be(0);
    }

    [TestMethod]
    public void A_log_written_out_and_read_back_is_the_same_log()
    {
        AuditLog log = new AuditLog();
        log.Append(Noon, FirstMatch, AuditRecordKind.MatchStarted);
        log.Append(Noon.AddSeconds(1), FirstMatch, AuditRecordKind.RingFired,
            player: "Vela", position: new WorldPoint(701.125, -100.5, 450));
        log.Append(Noon.AddSeconds(1), FirstMatch, AuditRecordKind.PlayerCaged,
            player: "Fenris", actor: "Vela", position: new WorldPoint(700, -100, 450));
        log.Append(Noon.AddSeconds(9), new MatchStamp(1, 1, TimeSpan.FromMinutes(4)),
            AuditRecordKind.PlayerEliminated, player: "Fenris", detail: "zone", health: 0);

        IEnumerable<string> lines = new[] { AuditFormat.Header }
            .Concat(log.Records.Select(AuditFormat.Write));

        List<AuditRecord> read = new List<AuditRecord>();
        foreach (string line in lines)
        {
            if (AuditFormat.TryRead(line, out AuditRecord record, out string? problem))
            {
                read.Add(record);
            }
            else
            {
                problem.Should().BeNull("nothing this log wrote is unreadable");
            }
        }

        read.Should().Equal(log.Records);
    }
}
