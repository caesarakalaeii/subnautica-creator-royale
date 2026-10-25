using System;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary><c>lms ghost</c> and <c>lms unghost</c>.</summary>
[TestClass]
public class MatchHostGhostVerbTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    private static MatchHost Host(params string[] ghosts) =>
        new MatchHost(Configured(ghosts), TimeSpan.FromSeconds(10));

    private static TournamentConfig Configured(params string[] ghosts)
    {
        string list = ghosts.Length == 0
            ? string.Empty
            : "\nghosts:\n" + string.Join(string.Empty, Array.ConvertAll(ghosts, name => $"  - {name}\n"));

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(@"
match:
  cycleLength: 5m
  warningLead: 1m
zone:
  damageInterval: 2s
  opening:
    centre: [0, -100, 0]
    radius: 1400
    damagePerTick: 1
  phases:
    - centre: [0, -100, 0]
      radius: 900
      shrinkFor: 90s
      damagePerTick: 3
" + list);

        result.Problems.Should().BeEmpty();

        return result.Config!;
    }

    [TestMethod]
    public void The_config_seeds_the_roster_the_verbs_change()
    {
        MatchHost host = Host("Caster");

        host.Ghosts.IsGhost("Caster").Should().BeTrue();
    }

    [TestMethod]
    public void Ghosting_somebody_says_what_it_took_away()
    {
        MatchHost host = Host();

        string answer = host.Command("ghost Caster", Noon);

        answer.Should().Contain("Caster");
        host.Ghosts.IsGhost("Caster").Should().BeTrue();
    }

    [TestMethod]
    public void Ghosting_somebody_says_the_camera_is_theirs_now()
    {
        MatchHost host = Host();

        host.Command("ghost Caster", Noon).Should().Contain("camera");
    }

    [TestMethod]
    public void Ghosting_somebody_twice_says_so_rather_than_pretending_it_did_something()
    {
        MatchHost host = Host("Caster");

        host.Command("ghost Caster", Noon).Should().Contain("already");
    }

    [TestMethod]
    public void Unghosting_puts_them_back_in_the_match()
    {
        MatchHost host = Host("Caster");

        host.Command("unghost Caster", Noon).Should().Contain("Caster");
        host.Ghosts.IsGhost("Caster").Should().BeFalse();
    }

    [TestMethod]
    public void Unghosting_somebody_who_was_never_one_says_so()
    {
        MatchHost host = Host();

        host.Command("unghost Vela", Noon).Should().Contain("not");
        host.Ghosts.Count.Should().Be(0);
    }

    [TestMethod]
    public void The_verb_with_no_name_is_refused_with_the_form_that_would_have_worked()
    {
        MatchHost host = Host();

        host.Command("ghost", Noon).Should().Contain("lms ghost <player>");
        host.Ghosts.Count.Should().Be(0, "a refusal changes nothing");
    }

    [TestMethod]
    public void Neither_verb_writes_anything_to_the_log()
    {
        MatchHost host = Host();

        host.Command("ghost Caster", Noon);
        host.Command("unghost Caster", Noon);

        host.Audit.Records.Should().BeEmpty();
    }

    [TestMethod]
    public void The_status_line_says_how_many_ghosts_there_are_because_nothing_else_does()
    {
        MatchHost host = Host("Caster");

        host.Status(Noon).Should().Contain("ghost");

        host.Command("unghost Caster", Noon);

        host.Status(Noon).Should().NotContain("ghost");
    }

    [TestMethod]
    public void A_name_is_taken_as_typed_rather_than_squeezed_into_the_verb()
    {
        MatchHost host = Host();

        host.Command("  ghost   Caster  ", Noon);

        host.Ghosts.Named.Should().Equal("Caster");
    }
}
