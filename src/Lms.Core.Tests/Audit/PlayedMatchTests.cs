using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>Replays a log written by a real Nitrox server (<c>docs/audit/played-match.tsv</c>) to pin the format against an artifact the writer did not produce.</summary>
[TestClass]
public class PlayedMatchTests
{
    private static IReadOnlyList<AuditRecord> Played()
    {
        List<AuditRecord> records = new List<AuditRecord>();

        foreach (string line in File.ReadAllLines("played-match.tsv"))
        {
            if (AuditFormat.TryRead(line, out AuditRecord record, out string? problem))
            {
                records.Add(record);
                continue;
            }

            problem.Should().BeNull($"'{line}' should either read or be skipped quietly");
        }

        return records;
    }

    [TestMethod]
    public void Every_line_of_a_real_match_reads_back()
    {
        Played().Should().HaveCount(14, "thirteen records and a reset, behind one header line");
    }

    [TestMethod]
    public void The_sequence_has_no_gaps_in_it()
    {
        Played().Select(record => record.Sequence).Should().Equal(Enumerable.Range(1, 14).Select(n => (long)n));
    }

    [TestMethod]
    public void The_match_that_was_played_folds_back_into_what_happened()
    {
        AuditReplay replay = AuditReplay.Of(Played());

        MatchAudit played = replay.Matches.Should().HaveCount(2).And.Subject.First();

        played.Group.Should().Be(1);
        played.Generation.Should().Be(1);
        played.PhasesFired.Should().Be(4, "three authored phases and the collapse");
        played.IsClosed.Should().BeTrue();
        played.HasEnded.Should().BeTrue();
        played.Winner.Should().BeNull("the wall closed on an empty arena");
    }

    [TestMethod]
    public void The_player_who_died_is_in_it_with_where_and_when()
    {
        PlayerAudit player = AuditReplay.Of(Played()).Matches.First().Player("Fenris")!;

        player.Should().NotBeNull();
        player.IsAlive.Should().BeFalse();
        player.EliminationOrder.Should().Be(1, "the only player, so the first out");
        player.LastBiome.Should().Be("kelpForest");
        player.LastPosition.Should().NotBeNull();
        player.LastPosition!.Value.X.Should().BeApproximately(343.507, 0.001);
    }

    [TestMethod]
    public void A_reset_starts_a_second_attempt_at_the_same_group()
    {
        IReadOnlyList<MatchAudit> matches = AuditReplay.Of(Played()).Matches;

        matches[1].Group.Should().Be(1);
        matches[1].Generation.Should().Be(2);
        matches[1].HasEnded.Should().BeFalse();
    }
}
