using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>A match takes new rules in the lobby and keeps everything it has done.</summary>
[TestClass]
public class MatchHostAdoptTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static TournamentConfig Rules(string? edit = null)
    {
        string yaml = File.ReadAllText("tournament.test.yaml");
        if (edit != null)
        {
            yaml = edit;
        }

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(yaml);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return result.Config!;
    }

    private static TournamentConfig Faster()
    {
        string yaml = File.ReadAllText("tournament.test.yaml");
        TournamentConfig original = Rules();

        string quicker = yaml.Replace(
            $"cycleLength: {(int)original.Clock.CycleLength.TotalSeconds}s",
            "cycleLength: 45s");

        quicker.Should().NotBe(yaml, "the test file has to have a cycleLength to edit");

        return Rules(quicker);
    }

    private static TournamentConfig WithClawDamage()
    {
        string yaml = File.ReadAllText("tournament.test.yaml");
        string harder = yaml.Replace("clawDamage: 20", "clawDamage: 35");

        harder.Should().NotBe(yaml, "the test file has to have a clawDamage to edit");

        return Rules(harder);
    }

    private static TournamentConfig WithoutDamageEnforced()
    {
        string yaml = File.ReadAllText("tournament.test.yaml");
        string harmless = yaml.Replace("enforceDamage: true", "enforceDamage: false");

        harmless.Should().NotBe(yaml, "the test file has to have enforceDamage on to switch off");

        return Rules(harmless);
    }

    private static MatchHost NewHost() => new MatchHost(Rules(), Heartbeat);

    [TestMethod]
    public void Takes_new_rules_in_the_lobby()
    {
        MatchHost host = NewHost();

        host.Adopt(Faster(), 7, "ops", out string said).Should().BeTrue();

        said.Should().Contain("revision 7");
        host.Rules.Clock.CycleLength.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestMethod]
    public void Refuses_while_a_match_is_running_and_says_when_it_will_be_taken_up()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Adopt(Faster(), 7, "ops", out string said).Should().BeFalse();

        said.Should().Contain("a match is running");
        said.Should().Contain("next lobby", "an operator needs to know it was not thrown away");

        host.Rules.Clock.CycleLength.Should().NotBe(TimeSpan.FromSeconds(45),
            "the wall must not move under people standing next to it");
    }

    [TestMethod]
    public void Refuses_while_a_match_is_paused()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("pause", Noon + TimeSpan.FromSeconds(10));

        host.Adopt(Faster(), 7, "ops", out _).Should().BeFalse(
            "a paused match is a match, and resuming it into different rules is not a resume");
    }

    [TestMethod]
    public void Takes_them_at_the_next_lobby()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Adopt(Faster(), 7, "ops", out _).Should().BeFalse();

        host.Command("next", Noon + TimeSpan.FromMinutes(5));

        host.Adopt(Faster(), 7, "ops", out _).Should().BeTrue();
    }

    [TestMethod]
    public void Keeps_everything_the_tournament_has_done()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("next", Noon + TimeSpan.FromMinutes(5));

        int written = host.Audit.Records.Count;
        written.Should().BeGreaterThan(0);

        host.Adopt(Faster(), 7, "ops", out _).Should().BeTrue();

        host.Audit.Records.Should().HaveCount(written + 1,
            "the log is the tournament's evidence, and a config change is not a reason to lose it");
    }

    [TestMethod]
    public void Keeps_the_tournaments_place_in_its_own_bracket()
    {
        MatchHost host = NewHost();
        host.Command("next", Noon);
        host.Command("next", Noon + TimeSpan.FromMinutes(1));

        int group = host.Group;
        group.Should().Be(3);

        host.Adopt(Faster(), 7, "ops", out _).Should().BeTrue();

        host.Group.Should().Be(group,
            "editing the pacing between groups is not starting the bracket again");
    }

    [TestMethod]
    public void Writes_down_that_the_rules_changed_because_the_config_file_is_not_evidence()
    {
        MatchHost host = NewHost();

        host.Adopt(Faster(), 7, "ops", out _).Should().BeTrue();

        AuditRecord written = host.Audit.Records.Last();

        written.Kind.Should().Be(AuditRecordKind.RulesAdopted);
        written.Amount.Should().Be(7, "the revision is what the booth can be asked about afterwards");
        written.Detail.Should().Contain("ops");
        written.Player.Should().BeNull("the rules change for everybody at once");
    }

    [TestMethod]
    public void Nothing_is_written_down_when_nothing_was_adopted()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        int written = host.Audit.Records.Count;

        host.Adopt(Faster(), 7, "ops", out _).Should().BeFalse();

        host.Audit.Records.Should().HaveCount(written,
            "a config the match could not take is not something that happened to it");
    }

    [TestMethod]
    public void Leaves_the_ghosts_alone_because_the_booth_owns_those()
    {
        MatchHost host = NewHost();
        host.Command("ghost Caster", Noon);

        host.Ghosts.IsGhost("Caster").Should().BeTrue();

        host.Adopt(Faster(), 7, "ops", out string said).Should().BeTrue();

        host.Ghosts.IsGhost("Caster").Should().BeTrue(
            "an edit to the file must not undo a weekend of reconciling");
        said.Should().Contain("Ghosts are unchanged");
    }

    [TestMethod]
    public void Refuses_while_a_round_is_armed_and_says_how_to_get_past_it()
    {
        TournamentConfig rules = Rules();
        MatchHost host = new MatchHost(rules, Heartbeat);

        List<PlayerReading> connected = new List<PlayerReading>();
        for (int i = 0; i < rules.Spawn.GroupSize; i++)
        {
            connected.Add(new PlayerReading((ushort)(i + 1), $"P{i + 1}",
                new WorldPoint(0, 0, 0), true, 100, 45, 100, 100));
        }

        host.Connected = () => connected;
        host.PreparesTheRound = plan => RoundOutcome.Prepared(
            $"{plan.Contestants.Count} seated and held.", "pods=5 cleared=0");

        host.Command("round", Noon);
        host.StartsAt.Should().NotBeNull("the round armed a start");

        host.Adopt(Faster(), 7, "ops", out string said).Should().BeFalse();

        said.Should().Contain("armed");
        said.Should().Contain("lms reset", "the way past it is the sentence's job to say");
    }

    [TestMethod]
    public void The_reset_its_refusal_names_actually_gets_a_push_through()
    {
        TournamentConfig rules = Rules();
        MatchHost host = new MatchHost(rules, Heartbeat);

        List<PlayerReading> connected = new List<PlayerReading>();
        for (int i = 0; i < rules.Spawn.GroupSize; i++)
        {
            connected.Add(new PlayerReading((ushort)(i + 1), $"P{i + 1}",
                new WorldPoint(0, 0, 0), true, 100, 45, 100, 100));
        }

        host.Connected = () => connected;
        host.PreparesTheRound = plan => RoundOutcome.Prepared(
            $"{plan.Contestants.Count} seated and held.", "pods=5 cleared=0");

        host.Command("round", Noon);
        host.Adopt(Faster(), 7, "ops", out _).Should().BeFalse("the round is armed");

        host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        host.WouldAdopt.Should().BeTrue();
        host.Adopt(Faster(), 8, "ops", out _).Should().BeTrue();
    }

    [TestMethod]
    public void An_adopted_claw_is_the_one_the_match_hands_back()
    {
        MatchHost host = NewHost();

        host.Rules.Prawn.ClawDamage.Should().Be(20, "the file the match started on");

        host.Adopt(WithClawDamage(), 7, "ops", out _).Should().BeTrue();

        host.Rules.Prawn.ClawDamage.Should().Be(35,
            "the whole prawn section moves with the config, and the server reads it per swing");
    }

    [TestMethod]
    public void The_judged_damage_follows_the_adopted_claw()
    {
        MatchHost host = NewHost();

        StrikeVerdict Swing() => StrikeRule.Judge(
            host.Rules.Prawn,
            StrikeWeapon.Claw,
            new WorldPoint(0, 0, 0),
            new WorldPoint(0, 0, 3),
            attackerIsHeld: false,
            attackerIsInAVehicle: true,
            attackerIsArmed: true,
            matchIsLive: true,
            victimIsInTheMatch: true,
            sinceLastStrike: TimeSpan.FromSeconds(5));

        Swing().Damage.Should().Be(20);

        host.Adopt(WithClawDamage(), 7, "ops", out _).Should().BeTrue();

        Swing().Damage.Should().Be(35,
            "this is the number the server ships and the client applies verbatim");
    }

    [TestMethod]
    public void A_refused_adoption_leaves_the_claw_alone()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Adopt(WithClawDamage(), 7, "ops", out _).Should().BeFalse();

        host.Rules.Prawn.ClawDamage.Should().Be(20,
            "no number may move under a match somebody is swinging in");
    }

    [TestMethod]
    public void An_adopted_enforceDamage_is_the_one_the_match_hands_back()
    {
        MatchHost host = NewHost();

        host.Rules.Flags.EnforceDamage.Should().BeTrue("the file the match started on");

        host.Adopt(WithoutDamageEnforced(), 7, "ops", out _).Should().BeTrue();

        host.Rules.Flags.EnforceDamage.Should().BeFalse(
            "an operator switching the bite off between groups means this group, not the next restart");
    }

    [TestMethod]
    public void A_config_that_is_not_there_is_a_programming_error_rather_than_a_refusal()
    {
        MatchHost host = NewHost();

        Action nothing = () => host.Adopt(null!, 1, "ops", out _);

        nothing.Should().Throw<ArgumentNullException>();
    }
}
