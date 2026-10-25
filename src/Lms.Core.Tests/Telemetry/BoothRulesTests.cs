using System;
using FluentAssertions;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The mod's half of the config channel: running and held revisions, and resend de-duplication.</summary>
[TestClass]
public class BoothRulesTests
{
    private const string Yaml = "match:\n  cycleLength: 45s\n";

    private static string Take(BoothRules rules)
    {
        rules.TryTake(out _, out string yaml).Should().BeTrue("something was waiting");

        return yaml;
    }

    [TestMethod]
    public void Takes_the_first_config_the_booth_sends()
    {
        BoothRules rules = new BoothRules();

        rules.Offer(1, Yaml).Should().BeTrue();

        Take(rules).Should().Be(Yaml);
    }

    [TestMethod]
    public void Says_which_revision_it_is_running_and_only_after_it_adopts_one()
    {
        BoothRules rules = new BoothRules();

        rules.Cursor.Should().Be(0, "a fresh run is running whatever is on its own disk");

        rules.Offer(4, Yaml);
        Take(rules);

        rules.Cursor.Should().Be(0, "taking it is not running it");

        rules.Settled(4, adopted: true, "the rules are revision 4");

        rules.Cursor.Should().Be(4);
    }

    [TestMethod]
    public void Ignores_a_resend_of_what_it_is_already_running()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, Yaml);
        Take(rules);
        rules.Settled(4, adopted: true, "adopted");

        rules.Offer(4, Yaml).Should().BeFalse();
        rules.Offer(3, Yaml).Should().BeFalse("an older revision is not news");
    }

    [TestMethod]
    public void Ignores_a_resend_of_what_it_is_holding_for_the_next_lobby()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, Yaml);
        Take(rules);

        rules.Holding.Should().Be(4);
        rules.Offer(4, Yaml).Should().BeFalse();
        rules.TryTake(out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_newer_revision_replaces_one_it_is_holding()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, Yaml);
        Take(rules);

        rules.Offer(5, "match:\n  cycleLength: 30s\n").Should().BeTrue();

        rules.TryTake(out long revision, out string yaml).Should().BeTrue();
        revision.Should().Be(5);
        yaml.Should().Contain("30s");
    }

    [TestMethod]
    public void Finishing_with_one_lets_the_next_be_taken()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, Yaml);
        Take(rules);
        rules.Settled(4, adopted: false, "a match is running");

        rules.Holding.Should().BeNull();
        rules.Revision.Should().Be(0, "it was not adopted, so it is not what this server is running");
        rules.Outcomes[0].Final.Should().BeTrue("this server has finished with it");
    }

    [TestMethod]
    public void Keeps_the_loaders_own_problems_word_for_word()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, "nonsense");
        Take(rules);

        rules.Settled(4, adopted: false, "that config would not load",
            new[] { "zone.phases[1].radius: must be a number of metres", "match.cycleLength: missing" });

        rules.Outcomes.Should().HaveCount(1);
        rules.Outcomes[0].Problems.Should().HaveCount(2);
        rules.Outcomes[0].Problems[0].Should().StartWith("zone.phases[1].radius",
            "the field path is the whole of what somebody needs to fix it");
    }

    [TestMethod]
    public void Says_the_in_between_state_without_finishing_with_the_revision()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(4, Yaml);
        Take(rules);

        rules.Note(4, "written to disk; it will be taken up at the next lobby.");

        rules.Outcomes.Should().HaveCount(1);
        rules.Outcomes[0].Final.Should().BeFalse(
            "the console must not read 'waiting for the lobby' as 'refused'");
        rules.Holding.Should().Be(4, "the resends must not make this parse again every second");
    }

    [TestMethod]
    public void An_outcome_is_forgotten_only_once_the_booth_has_it()
    {
        BoothRules rules = new BoothRules();
        rules.Offer(1, Yaml);
        Take(rules);
        rules.Settled(1, adopted: true, "adopted");

        int reported = rules.Outcomes.Count;

        rules.Offer(2, Yaml);
        Take(rules);
        rules.Settled(2, adopted: true, "adopted");

        rules.Forget(reported);

        rules.Outcomes.Should().HaveCount(1);
        rules.Outcomes[0].Revision.Should().Be(2);
    }

    [TestMethod]
    public void Switched_off_it_takes_nothing_and_tells_the_booth_nothing()
    {
        BoothRules rules = new BoothRules(accepts: false);

        rules.Offer(1, Yaml).Should().BeFalse();
        rules.TryTake(out _, out _).Should().BeFalse();
        rules.Cursor.Should().BeNull(
            "no header means the booth sends nothing rather than resending forever");
    }

    [TestMethod]
    public void A_config_bigger_than_this_server_will_read_is_refused_with_a_sentence()
    {
        BoothRules rules = new BoothRules();

        rules.Offer(1, new string('x', 300 * 1024)).Should().BeFalse();

        rules.Outcomes.Should().HaveCount(1);
        rules.Outcomes[0].Message.Should().Contain("larger than");
    }

    [TestMethod]
    public void Nothing_at_all_is_not_a_config()
    {
        BoothRules rules = new BoothRules();

        rules.Offer(1, null).Should().BeFalse();
        rules.Offer(1, "   ").Should().BeFalse();
        rules.Offer(0, Yaml).Should().BeFalse("a revision counts from one, like the booth's own");
    }

    [TestMethod]
    public void An_outcome_backlog_is_bounded_while_the_booth_is_refusing_them()
    {
        BoothRules rules = new BoothRules();

        for (long revision = 1; revision <= 200; revision++)
        {
            rules.Note(revision, "waiting");
        }

        rules.Outcomes.Should().HaveCountLessThan(200);
        rules.Outcomes.Should().NotBeEmpty();
    }
}
