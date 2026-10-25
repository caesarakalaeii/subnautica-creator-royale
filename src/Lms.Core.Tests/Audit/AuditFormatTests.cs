using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>The audit line format: six fixed columns then tab-separated <c>key=value</c> pairs; a record must never be lost, even quietly.</summary>
[TestClass]
public class AuditFormatTests
{
    private static readonly DateTimeOffset Stamped =
        new DateTimeOffset(2026, 10, 24, 18, 3, 11, 123, TimeSpan.Zero);

    private static readonly MatchStamp FirstMatch = new MatchStamp(1, 1, TimeSpan.FromMinutes(3));

    private static AuditRecord Bite() =>
        new AuditRecord(17, Stamped, new MatchStamp(1, 1, new TimeSpan(0, 3, 4) + TimeSpan.FromMilliseconds(500)),
            AuditRecordKind.ZoneBite,
            player: "Fenris",
            position: new WorldPoint(712.5, -108.25, 455),
            biome: "grandReef",
            amount: 8,
            health: 44);

    [TestMethod]
    public void A_record_writes_its_six_columns_and_then_its_payload()
    {
        AuditFormat.Write(Bite()).Should().Be(
            "17\t2026-10-24T18:03:11.123Z\t184500\t1\t1\tZoneBite\t" +
            "player=Fenris\tpos=712.5,-108.25,455\tbiome=grandReef\tamount=8\thealth=44");
    }

    [TestMethod]
    public void A_written_record_reads_back_as_the_record_it_was()
    {
        AuditRecord written = Bite();

        AuditFormat.TryRead(AuditFormat.Write(written), out AuditRecord read, out string? problem)
            .Should().BeTrue();

        problem.Should().BeNull();
        read.Sequence.Should().Be(written.Sequence);
        read.TimestampUtc.Should().Be(written.TimestampUtc);
        read.Elapsed.Should().Be(written.Elapsed);
        read.Group.Should().Be(written.Group);
        read.Generation.Should().Be(written.Generation);
        read.Kind.Should().Be(written.Kind);
        read.Player.Should().Be(written.Player);
        read.Position.Should().Be(written.Position);
        read.Biome.Should().Be(written.Biome);
        read.Amount.Should().Be(written.Amount);
        read.Health.Should().Be(written.Health);
    }

    [TestMethod]
    public void Every_kind_survives_a_round_trip()
    {
        foreach (AuditRecordKind kind in Enum.GetValues(typeof(AuditRecordKind)).Cast<AuditRecordKind>())
        {
            AuditRecord written = new AuditRecord(3, Stamped, FirstMatch, kind,
                player: AuditRecord.IsAboutAPlayer(kind) ? "Fenris" : null,
                actor: "Vela", phase: 2, position: new WorldPoint(1, -2, 3), biome: "kelpForest",
                amount: 8.5, health: 91.5, detail: "zone");

            AuditFormat.TryRead(AuditFormat.Write(written), out AuditRecord read, out _)
                .Should().BeTrue($"{kind} writes a readable line");

            read.Kind.Should().Be(kind);
            read.Actor.Should().Be("Vela");
            read.Phase.Should().Be(2);
            read.Detail.Should().Be("zone");
        }
    }

    [TestMethod]
    public void A_record_with_nothing_to_say_about_a_field_leaves_the_key_out()
    {
        string line = AuditFormat.Write(new AuditRecord(2, Stamped, FirstMatch, AuditRecordKind.MatchResumed));

        line.Should().NotContain("player=").And.NotContain("pos=").And.NotContain("amount=");
        AuditFormat.TryRead(line, out AuditRecord read, out _).Should().BeTrue();
        read.Position.Should().BeNull();
        read.Amount.Should().BeNull();
    }

    [TestMethod]
    public void A_creator_with_a_tab_in_their_name_does_not_split_a_line_in_two()
    {
        AuditRecord written = new AuditRecord(5, Stamped, FirstMatch, AuditRecordKind.PlayerJoined,
            player: "Fen\tris\\ \r\nthe Bold");

        string line = AuditFormat.Write(written);

        line.Split('\t').Length.Should().Be(7);
        line.Should().NotContain("\n");
        AuditFormat.TryRead(line, out AuditRecord read, out _).Should().BeTrue();
        read.Player.Should().Be("Fen\tris\\ \r\nthe Bold");
    }

    [TestMethod]
    public void A_blank_line_is_nothing_and_nothing_to_complain_about()
    {
        AuditFormat.TryRead(string.Empty, out _, out string? problem).Should().BeFalse();
        problem.Should().BeNull();

        AuditFormat.TryRead("   ", out _, out problem).Should().BeFalse();
        problem.Should().BeNull();
    }

    [TestMethod]
    public void The_header_names_the_format_and_its_version_and_reads_as_a_comment()
    {
        AuditFormat.Header.Should().Be("#lms-audit 1");

        AuditFormat.TryRead(AuditFormat.Header, out _, out string? problem).Should().BeFalse();
        problem.Should().BeNull();
    }

    [TestMethod]
    public void A_half_written_final_line_is_named_rather_than_thrown_or_swallowed()
    {
        string truncated = AuditFormat.Write(Bite()).Substring(0, 20);

        AuditFormat.TryRead(truncated, out _, out string? problem).Should().BeFalse();
        problem.Should().NotBeNull();
    }

    [TestMethod]
    public void A_payload_key_from_a_newer_writer_is_ignored_so_the_record_still_replays()
    {
        string line = AuditFormat.Write(Bite()) + "\tvehicle=Seamoth";

        AuditFormat.TryRead(line, out AuditRecord read, out string? problem).Should().BeTrue();
        problem.Should().BeNull();
        read.Player.Should().Be("Fenris");
    }

    [TestMethod]
    public void A_kind_nobody_here_has_heard_of_is_reported()
    {
        string line = "17\t2026-10-24T18:03:11.123Z\t184500\t1\t1\tPlayerAscended\tplayer=Fenris";

        AuditFormat.TryRead(line, out _, out string? problem).Should().BeFalse();
        problem.Should().Contain("PlayerAscended");
    }

    [TestMethod]
    public void A_column_that_is_not_a_number_is_reported_by_name()
    {
        string line = "17\t2026-10-24T18:03:11.123Z\tsoon\t1\t1\tMatchStarted";

        AuditFormat.TryRead(line, out _, out string? problem).Should().BeFalse();
        problem.Should().Contain("elapsed");
    }

    [TestMethod]
    public void A_payload_token_with_no_value_is_reported()
    {
        string line = "17\t2026-10-24T18:03:11.123Z\t184500\t1\t1\tPlayerJoined\tplayer";

        AuditFormat.TryRead(line, out _, out string? problem).Should().BeFalse();
        problem.Should().Contain("player");
    }

    [TestMethod]
    public void A_player_record_that_names_nobody_is_reported_rather_than_read()
    {
        string line = "17\t2026-10-24T18:03:11.123Z\t184500\t1\t1\tZoneBite\tamount=8";

        AuditFormat.TryRead(line, out _, out string? problem).Should().BeFalse();
        problem.Should().Contain("player");
    }

    [TestMethod]
    public void A_position_that_is_not_three_numbers_is_reported()
    {
        string line = "17\t2026-10-24T18:03:11.123Z\t184500\t1\t1\tPlayerPosition\tplayer=Fenris\tpos=712.5,-108.25";

        AuditFormat.TryRead(line, out _, out string? problem).Should().BeFalse();
        problem.Should().Contain("pos");
    }
}
