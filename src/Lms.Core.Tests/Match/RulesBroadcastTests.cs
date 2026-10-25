using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The rules on their way to every client.</summary>
[TestClass]
public class RulesBroadcastTests
{
    private static string TestConfig() => File.ReadAllText("tournament.test.yaml");

    [TestMethod]
    public void Survives_the_round_trip_with_its_revision()
    {
        string line = new RulesBroadcast(7, TestConfig()).Encode();

        RulesBroadcast.TryDecode(line, out RulesBroadcast read).Should().BeTrue();

        read.Revision.Should().Be(7);
        read.Yaml.Should().Contain("cycleLength");
    }

    [TestMethod]
    public void Is_a_packet_rather_than_a_download()
    {
        string config = TestConfig();
        string commented = config + "\n# a note for the operator, not the client\n" +
                           string.Concat(Enumerable.Repeat("# more notes for the operator\n", 500));
        string line = new RulesBroadcast(1, config).Encode();

        new RulesBroadcast(1, commented).Encode().Should().Be(line, "comments are not sent");
        line.Length.Should().BeLessThan(4_000);
    }

    [TestMethod]
    public void Keeps_a_hash_that_is_inside_a_value()
    {
        string line = new RulesBroadcast(1, TestConfig()).Encode();

        RulesBroadcast.TryDecode(line, out RulesBroadcast read).Should().BeTrue();

        read.Yaml.Should().Contain("#", "a colour is a hash and six digits");
    }

    [TestMethod]
    public void What_arrives_is_still_a_config_the_loader_takes()
    {
        string line = new RulesBroadcast(3, TestConfig()).Encode();
        RulesBroadcast.TryDecode(line, out RulesBroadcast read).Should().BeTrue();

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(read.Yaml);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        TournamentConfigLoadResult original = TournamentConfigLoader.Load(TestConfig());

        result.Config!.Clock.CycleLength.Should().Be(original.Config!.Clock.CycleLength);
        result.Config.Ring.Should().BeEquivalentTo(original.Config.Ring);
        result.Config.Colours.Now.Should().Be(original.Config.Colours.Now);
    }

    [TestMethod]
    public void Strips_whole_line_comments_and_blank_lines_and_nothing_else()
    {
        string stripped = RulesBroadcast.Strip("# a heading\n\nmatch:\n  cycleLength: 60s  # a minute\n");

        stripped.Should().Be("match:\n  cycleLength: 60s  # a minute");
    }

    [TestMethod]
    public void A_line_from_another_build_is_declined_rather_than_half_read()
    {
        RulesBroadcast.TryDecode("lms-rules/2\trev=1\tyaml=match:", out _).Should().BeFalse();
        RulesBroadcast.TryDecode("lms-hidden/1\tname=Ada", out _).Should().BeFalse();
        RulesBroadcast.TryDecode(null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_line_with_no_document_in_it_is_declined()
    {
        RulesBroadcast.TryDecode("lms-rules/1\trev=4", out _).Should().BeFalse();
        RulesBroadcast.TryDecode("lms-rules/1\trev=4\tyaml=", out _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_the_way_every_other_message_here_ignores_one()
    {
        RulesBroadcast.TryDecode("lms-rules/1\trev=4\tmood=cheerful\tyaml=match:\n  cycleLength: 60s",
            out RulesBroadcast read).Should().BeTrue();

        read.Revision.Should().Be(4);
    }

    [TestMethod]
    public void Is_recognised_without_being_read()
    {
        RulesBroadcast.IsRules(new RulesBroadcast(1, "match:").Encode()).Should().BeTrue();
        RulesBroadcast.IsRules("lms-hidden/1\tname=Ada").Should().BeFalse();
        RulesBroadcast.IsRules(null).Should().BeFalse();
    }

    [TestMethod]
    public void A_document_with_a_tab_in_it_is_refused_rather_than_sent()
    {
        Action tabbed = () => new RulesBroadcast(1, "match:\n\tcycleLength: 60s").Encode();

        tabbed.Should().Throw<ArgumentException>().WithMessage("*tab*");
    }
}
