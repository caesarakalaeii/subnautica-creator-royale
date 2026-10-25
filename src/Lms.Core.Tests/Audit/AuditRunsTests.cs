using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>Reading a log file back as the runs of the server that wrote it.</summary>
[TestClass]
public class AuditRunsTests
{
    private static readonly DateTimeOffset Stamped = new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    private static string Line(long sequence, int group) =>
        AuditFormat.Write(new AuditRecord(sequence, Stamped, new MatchStamp(group, 1, TimeSpan.Zero),
            AuditRecordKind.MatchStarted));

    [TestMethod]
    public void Every_header_starts_another_run()
    {
        IReadOnlyList<IReadOnlyList<AuditRecord>> runs = AuditFormat.ReadRuns(new[]
        {
            AuditFormat.Header, Line(1, 1), Line(2, 1),
            AuditFormat.Header, Line(1, 2),
        }, out int unreadable);

        runs.Select(run => run.Count).Should().Equal(2, 1);
        runs[1][0].Group.Should().Be(2);
        unreadable.Should().Be(0);
    }

    [TestMethod]
    public void A_sequence_that_goes_back_starts_another_run_even_without_a_header()
    {
        AuditFormat.ReadRuns(new[] { Line(1, 1), Line(2, 1), Line(1, 2) }, out _)
            .Select(run => run.Count).Should().Equal(2, 1);
    }

    [TestMethod]
    public void A_line_that_does_not_read_is_counted_and_left_out()
    {
        IReadOnlyList<IReadOnlyList<AuditRecord>> runs = AuditFormat.ReadRuns(new[]
        {
            AuditFormat.Header, Line(1, 1), "", "3\t2026-10-24T18:0",
        }, out int unreadable);

        runs.Should().ContainSingle().Which.Should().HaveCount(1);
        unreadable.Should().Be(1, "a blank line is nothing, not a fault");
    }

    [TestMethod]
    public void A_file_with_only_a_header_is_no_run()
    {
        AuditFormat.ReadRuns(new[] { AuditFormat.Header }, out _).Should().BeEmpty();
    }
}
