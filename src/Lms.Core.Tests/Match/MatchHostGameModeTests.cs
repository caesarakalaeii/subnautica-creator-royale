using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Writing down that somebody's body was silently made mortal.</summary>
[TestClass]
public class MatchHostGameModeTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static AuditRecord Only(MatchHost host) =>
        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.GameModeCorrected);

    [TestMethod]
    public void A_correction_names_the_player_and_the_mode_they_were_in()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Corrected("Vela", "CREATIVE");

        AuditRecord record = Only(host);
        record.Player.Should().Be("Vela");
        record.Detail.Should().Contain("CREATIVE");
    }

    [TestMethod]
    public void And_says_it_happened_on_the_way_in_rather_than_at_a_round()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Corrected("Vela", "CREATIVE");

        Only(host).Detail.Should().Contain("joined");
    }

    [TestMethod]
    public void It_is_stamped_with_the_match_that_was_running()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("next", Noon + TimeSpan.FromMinutes(1));

        host.Corrected("Vela", "CREATIVE");

        AuditRecord record = Only(host);
        record.Group.Should().Be(host.Group);
        record.Generation.Should().Be(host.Generation);
    }

    [TestMethod]
    public void Two_corrections_are_two_records_because_it_is_a_thing_that_happened()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Corrected("Vela", "CREATIVE");
        host.Corrected("Vela", "CREATIVE");

        host.Audit.Records.Count(record => record.Kind == AuditRecordKind.GameModeCorrected)
            .Should().Be(2);
    }

    [TestMethod]
    public void A_correction_about_nobody_is_refused()
    {
        MatchHost host = NewHost();

        host.Invoking(h => h.Corrected(" ", "CREATIVE")).Should().Throw<ArgumentException>();
        host.Invoking(h => h.Corrected(null!, "CREATIVE")).Should().Throw<ArgumentException>();
    }
}
