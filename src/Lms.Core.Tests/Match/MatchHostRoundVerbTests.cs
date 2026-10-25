using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary><c>lms round</c>, end to end on this side of the wire.</summary>
[TestClass]
public class MatchHostRoundVerbTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(30);

    private static PlayerReading Connected(ushort id, string name) =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 100, 45, 100, 100);

    private const string Yaml = @"
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
spawn:
  ownPod: true
  groupSize: 2
  minSeparation: 50
  circleRadius: 600
start:
  hold: 30s
  kit:
    - Knife
";

    private static TournamentConfig Configured(string extra = "")
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(Yaml + extra);

        result.Problems.Should().BeEmpty(string.Join("; ", result.Problems.Select(p => p.ToString())));

        return result.Config!;
    }

    private sealed class Prepared
    {
        internal Prepared(TournamentConfig config, params string[] connected)
        {
            Host = new MatchHost(config, TimeSpan.FromSeconds(10));

            List<PlayerReading> readings = new List<PlayerReading>();
            for (int i = 0; i < connected.Length; i++)
            {
                readings.Add(Connected((ushort)(i + 1), connected[i]));
            }

            Host.Connected = () => readings;
            Host.PreparesTheRound = plan =>
            {
                Asked = plan;
                Runs++;

                return Refuse
                    ? RoundOutcome.Nothing("the pods could not be swept, so nothing was changed.")
                    : RoundOutcome.Prepared(
                        $"{plan.Contestants.Count} seated and held.", "pods=2 cleared=3");
            };
        }

        internal MatchHost Host { get; }

        internal RoundPlan? Asked { get; private set; }

        internal int Runs { get; private set; }

        internal bool Refuse { get; set; }
    }

    private static Prepared Ready(params string[] connected) =>
        new Prepared(Configured(), connected.Length == 0 ? new[] { "Ada", "Bea" } : connected);

    private static TournamentConfig Without(string key)
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(string.Join("\n",
            Yaml.Split('\n').Where(line => !line.TrimStart().StartsWith(key + ":")
                                            && !line.TrimStart().StartsWith("- Knife"))));

        result.Problems.Should().BeEmpty(string.Join("; ", result.Problems.Select(p => p.ToString())));

        return result.Config!;
    }

    [TestMethod]
    public void The_verb_hands_the_contestants_to_the_server_and_says_what_it_did()
    {
        Prepared prepared = Ready();

        string answer = prepared.Host.Command("round", Noon);

        prepared.Runs.Should().Be(1);
        prepared.Asked!.Contestants.Should().Equal("Ada", "Bea");
        answer.Should().Contain("seated and held");
    }

    [TestMethod]
    public void The_contestants_are_connected_minus_the_ghosts()
    {
        Prepared prepared = new Prepared(Configured(), "Ada", "Caster", "Bea");
        prepared.Host.Command("ghost Caster", Noon);

        prepared.Host.Command("round", Noon);

        prepared.Asked!.Contestants.Should().Equal("Ada", "Bea");
    }

    [TestMethod]
    public void A_refused_plan_never_reaches_the_server_and_writes_nothing()
    {
        Prepared prepared = new Prepared(Configured(), "Ada");

        string answer = prepared.Host.Command("round", Noon);

        prepared.Runs.Should().Be(0, "the count was wrong, so nothing destructive was attempted");
        answer.Should().Contain("lms round force");
        prepared.Host.Audit.Records.Should().BeEmpty();
    }

    [TestMethod]
    public void Force_is_the_two_word_override_and_it_is_said_out_loud()
    {
        Prepared prepared = new Prepared(Configured(), "Ada");

        string answer = prepared.Host.Command("round force", Noon);

        prepared.Runs.Should().Be(1);
        answer.Should().Contain("forced");
    }

    [TestMethod]
    public void A_server_that_could_not_do_it_writes_nothing_and_arms_nothing()
    {
        Prepared prepared = Ready();
        prepared.Refuse = true;

        string answer = prepared.Host.Command("round", Noon);

        answer.Should().Contain("could not be swept");
        prepared.Host.Audit.Records.Should().BeEmpty();
        prepared.Host.StartsAt.Should().BeNull();
    }

    [TestMethod]
    public void One_record_names_the_contestants_and_the_kit()
    {
        Prepared prepared = Ready();

        prepared.Host.Command("round", Noon);

        IReadOnlyList<AuditRecord> written = prepared.Host.Audit.Records;
        written.Should().HaveCount(1);

        AuditRecord record = written[0];
        record.Kind.Should().Be(AuditRecordKind.RoundPrepared);
        record.Detail.Should().Contain("Ada").And.Contain("Bea");
        record.Detail.Should().Contain("kit=Knife");
        record.Amount.Should().Be(Hold.TotalSeconds);
    }

    [TestMethod]
    public void An_empty_kit_says_so_rather_than_leaving_the_field_blank()
    {
        Prepared prepared = new Prepared(Without("kit"), "Ada", "Bea");

        prepared.Host.Command("round", Noon);

        prepared.Host.Audit.Records[0].Detail.Should().Contain("kit=nothing");
    }

    [TestMethod]
    public void The_match_is_still_in_the_lobby_while_the_hold_is_on()
    {
        Prepared prepared = Ready();

        prepared.Host.Command("round", Noon);

        prepared.Host.StartsAt.Should().Be(Noon + Hold);
        prepared.Host.Audit.Records.Should().OnlyContain(
            record => record.Kind == AuditRecordKind.RoundPrepared);

        prepared.Host.Advance(Noon + Hold - TimeSpan.FromSeconds(1));
        prepared.Host.Audit.Records.Should().NotContain(
            record => record.Kind == AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void And_it_starts_itself_as_the_fields_expire()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Advance(Noon + Hold);

        prepared.Host.Audit.Records.Should().Contain(
            record => record.Kind == AuditRecordKind.MatchStarted);
        prepared.Host.StartsAt.Should().BeNull("the hold is spent");
        prepared.Host.IsLive.Should().BeTrue();
    }

    [TestMethod]
    public void The_start_is_written_once_however_often_the_clock_is_asked()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        for (int i = 0; i < 5; i++)
        {
            prepared.Host.Advance(Noon + Hold + TimeSpan.FromSeconds(i));
        }

        prepared.Host.Audit.Records
            .Count(record => record.Kind == AuditRecordKind.MatchStarted)
            .Should().Be(1);
    }

    [TestMethod]
    public void A_reset_during_the_hold_disarms_the_round_it_replaced()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        prepared.Host.StartsAt.Should().BeNull("the round the hold belonged to was thrown away");

        prepared.Host.Advance(Noon + Hold);

        prepared.Host.IsLive.Should().BeFalse();
        prepared.Host.Audit.Records.Should().NotContain(
            record => record.Kind == AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void And_so_does_taking_the_next_group()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Command("next", Noon + TimeSpan.FromSeconds(5));

        prepared.Host.StartsAt.Should().BeNull();
        prepared.Host.Group.Should().Be(2);

        prepared.Host.Advance(Noon + Hold);

        prepared.Host.IsLive.Should().BeFalse();
        prepared.Host.Audit.Records.Should().NotContain(
            record => record.Kind == AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void An_operator_who_starts_it_themselves_disarms_the_hold()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Command("start", Noon + TimeSpan.FromSeconds(5));

        prepared.Host.StartsAt.Should().BeNull();

        prepared.Host.Advance(Noon + Hold);
        prepared.Host.Audit.Records
            .Count(record => record.Kind == AuditRecordKind.MatchStarted)
            .Should().Be(1);
    }

    [TestMethod]
    public void A_round_with_one_contestant_still_loading_is_refused_and_names_them()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");

        string answer = prepared.Host.Command("round", Noon);

        prepared.Runs.Should().Be(0, "the round never reached the world");
        answer.Should().Contain("Bea").And.Contain("lms round force");
        prepared.Host.StartsAt.Should().BeNull();
        prepared.Host.Audit.Records.Should().BeEmpty();
    }

    [TestMethod]
    public void A_round_with_every_contestant_ready_prepares_ordinarily()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");
        prepared.Host.AnnounceReady("Bea");

        string answer = prepared.Host.Command("round", Noon);

        prepared.Runs.Should().Be(1);
        prepared.Asked!.Contestants.Should().Equal("Ada", "Bea");
        answer.Should().Contain("seated and held");
    }

    [TestMethod]
    public void Force_prepares_a_round_with_a_contestant_still_loading()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");

        prepared.Host.Command("round force", Noon);

        prepared.Runs.Should().Be(1);
        prepared.Host.StartsAt.Should().Be(Noon + Hold);
    }

    [TestMethod]
    public void A_reset_throws_the_readiness_reports_away_with_the_round()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");
        prepared.Host.AnnounceReady("Bea");
        prepared.Host.Command("round", Noon);
        prepared.Host.Ready.Should().HaveCount(2);

        prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        prepared.Host.Ready.Should().BeEmpty();

        prepared.Host.Command("round", Noon + TimeSpan.FromSeconds(6));
        prepared.Runs.Should().Be(2, "the replacement round is not held hostage by the old gate");
    }

    [TestMethod]
    public void A_reset_names_who_is_still_loading_but_proceeds()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");
        prepared.Host.Command("round", Noon);

        string answer = prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        answer.Should().Contain("Bea").And.Contain("not reported ready");
        answer.Should().Contain("reset", "the reset itself still happened");
    }

    [TestMethod]
    public void A_reset_with_everybody_ready_says_nothing_about_readiness()
    {
        Prepared prepared = Ready();
        prepared.Host.AnnounceReady("Ada");
        prepared.Host.AnnounceReady("Bea");
        prepared.Host.Command("round", Noon);

        string answer = prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        answer.Should().NotContain("not reported ready");
    }

    [TestMethod]
    public void A_ghost_who_is_still_loading_is_not_named_by_the_reset_warning()
    {
        Prepared prepared = new Prepared(Configured(), "Ada", "Bea", "Caster");
        prepared.Host.Command("ghost Caster", Noon);
        prepared.Host.AnnounceReady("Ada");
        prepared.Host.AnnounceReady("Bea");
        prepared.Host.Command("round", Noon);

        string answer = prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        answer.Should().NotContain("Caster");
    }

    [TestMethod]
    public void A_hold_of_nothing_starts_the_match_in_the_same_breath()
    {
        Prepared prepared = new Prepared(Configured(@"
"), "Ada", "Bea");

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(
            Yaml.Replace("hold: 30s", "hold: 0s"));
        result.Problems.Should().BeEmpty();

        Prepared instant = new Prepared(result.Config!, "Ada", "Bea");
        string answer = instant.Host.Command("round", Noon);

        instant.Host.IsLive.Should().BeTrue();
        instant.Host.StartsAt.Should().BeNull();
        answer.Should().Contain("no hold");
        prepared.Runs.Should().Be(0);
    }

    [TestMethod]
    public void The_status_line_says_the_match_is_about_to_start_itself()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Status(Noon + TimeSpan.FromSeconds(10)).Should().Contain("held, starting in 20");
    }

    [TestMethod]
    public void A_round_start_mid_match_is_refused_and_leaves_the_armed_start_alone()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);
        prepared.Host.Advance(Noon + Hold);

        string answer = prepared.Host.Command("round", Noon + Hold + TimeSpan.FromSeconds(1));

        answer.Should().Contain("Reset it first");
        prepared.Runs.Should().Be(1, "the second attempt never reached the world");
    }

    [TestMethod]
    public void A_verb_typed_after_the_hold_ran_out_finds_the_match_already_started()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Command("reset", Noon + Hold + TimeSpan.FromSeconds(5));

        prepared.Host.Audit.Records.Select(record => record.Kind).Should().Equal(
            AuditRecordKind.RoundPrepared,
            AuditRecordKind.MatchStarted,
            AuditRecordKind.MatchReset);
        prepared.Host.StartsAt.Should().BeNull();
        prepared.Host.IsLive.Should().BeFalse();
    }

    [TestMethod]
    public void A_round_typed_after_the_hold_ran_out_is_refused_rather_than_re_prepared()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        string answer = prepared.Host.Command("round", Noon + Hold + TimeSpan.FromSeconds(5));

        answer.Should().Contain("Reset it first");
        prepared.Runs.Should().Be(1, "the second attempt never reached the world");
        prepared.Host.IsLive.Should().BeTrue("the hold ran out while nobody was looking");
    }

    /// <remarks>
    /// Each seating is a teleport, and the game freezes a player's stats per teleport but releases
    /// them once for any number of them, so a group seated twice never loses air, food or water.
    /// </remarks>
    [TestMethod]
    public void A_second_round_during_the_hold_is_refused_and_never_reaches_the_world()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        string answer = prepared.Host.Command("round", Noon + TimeSpan.FromMilliseconds(235));

        answer.Should().Contain("already armed");
        prepared.Runs.Should().Be(1, "a second seating freezes everybody's vitals");
        prepared.Host.Audit.Records.Count(record => record.Kind == AuditRecordKind.RoundPrepared)
            .Should().Be(1);
        prepared.Host.StartsAt.Should().Be(Noon + Hold, "the first round's start stands");
    }

    [TestMethod]
    public void Force_does_not_override_the_hold_either()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);

        prepared.Host.Command("round force", Noon + TimeSpan.FromSeconds(1));

        prepared.Runs.Should().Be(1);
    }

    [TestMethod]
    public void A_round_after_a_reset_during_the_hold_prepares_again()
    {
        Prepared prepared = Ready();
        prepared.Host.Command("round", Noon);
        prepared.Host.Command("reset", Noon + TimeSpan.FromSeconds(5));

        prepared.Host.Command("round", Noon + TimeSpan.FromSeconds(10));

        prepared.Runs.Should().Be(2);
    }

    [TestMethod]
    public void A_host_with_no_server_behind_it_answers_the_verb_rather_than_throwing()
    {
        MatchHost host = new MatchHost(Configured(), TimeSpan.FromSeconds(10));

        host.Command("round", Noon).Should().Contain("no server here");
        host.Audit.Records.Should().BeEmpty();
    }
}
