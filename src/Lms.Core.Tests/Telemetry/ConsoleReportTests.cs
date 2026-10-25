using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The console channel: the verb vocabulary and connected-player list reported to the booth.</summary>
[TestClass]
public class ConsoleReportTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static ConsoleReport Report(DateTimeOffset? at = null,
        IReadOnlyList<ConsolePresence>? connected = null,
        IReadOnlyList<BoothCommandAnswer>? answers = null,
        long ran = 0, bool accepts = true) =>
        new ConsoleReport(at ?? Noon,
            connected ?? new[] { new ConsolePresence("Ada", true) },
            ConsoleJson.Vocabulary(),
            answers ?? new BoothCommandAnswer[0],
            ran, accepts);

    [TestMethod]
    public void CarriesEverybodyConnectedAndSaysWhichOfThemTheMatchCounts()
    {
        ConsoleReport report = Report(connected: new[]
        {
            new ConsolePresence("Ada", true),
            new ConsolePresence("Caster", false),
        });

        string json = ConsoleJson.Write(report);

        json.Should().Contain("\"name\":\"Ada\",\"inMatch\":true");
        json.Should().Contain("\"name\":\"Caster\",\"inMatch\":false");
    }

    [TestMethod]
    public void CarriesEveryVerbThisBuildHasSoTheConsoleCannotDriftFromIt()
    {
        string json = ConsoleJson.Write(Report());

        foreach (string verb in AdminCommands.Verbs)
        {
            json.Should().Contain($"\"verb\":\"{verb}\"", $"the console draws a control for {verb}");
        }
    }

    [TestMethod]
    public void SaysWhichVerbsTakeANameAndWhichHaveToBeTyped()
    {
        ConsoleVerb[] vocabulary = ConsoleJson.Vocabulary();

        ConsoleVerb ghost = vocabulary.Single(verb => verb.Verb == "ghost");
        ghost.TakesASubject.Should().BeTrue("a control with no name field would always fail");
        ghost.MustBeTyped.Should().BeFalse();

        ConsoleVerb sweep = vocabulary.Single(verb => verb.Verb == "podclear all");
        sweep.MustBeTyped.Should().BeTrue(
            "deleting every pod in the world must never be a routine keystroke");

        ConsoleVerb forced = vocabulary.Single(verb => verb.Verb == "round force");
        forced.MustBeTyped.Should().BeTrue("it is past the refusal that stops a four-player group");

        ConsoleVerb status = vocabulary.Single(verb => verb.Verb == "status");
        status.TakesASubject.Should().BeFalse();
        status.MustBeTyped.Should().BeFalse();
    }

    [TestMethod]
    public void TheFollowAndTheLetGoBothReachTheBoothAsControls()
    {
        ConsoleVerb[] vocabulary = ConsoleJson.Vocabulary();

        ConsoleVerb follow = vocabulary.Single(verb => verb.Verb == "follow");
        follow.TakesASubject.Should().BeTrue("a camera has to be told which contestant");
        follow.MustBeTyped.Should().BeFalse("it moves a camera and nothing else; there is nothing to guard");

        ConsoleVerb release = vocabulary.Single(verb => verb.Verb == "unfollow");
        release.TakesASubject.Should().BeFalse("there is one thing to stop doing");
        release.MustBeTyped.Should().BeFalse();
    }

    [TestMethod]
    public void CarriesTheMatchesOwnSentenceWordForWord()
    {
        const string refused =
            "a group is 5 and this counted 4: Ada, Bea, Cy, Dov. Run `lms round force` to prepare it anyway.";

        string json = ConsoleJson.Write(Report(
            answers: new[] { new BoothCommandAnswer(7, "round", refused) }));

        json.Should().Contain(refused.Replace("\"", "\\\""),
            "on the day that sentence is the whole help system");
        json.Should().Contain("\"id\":7");
    }

    [TestMethod]
    public void SaysWhenThisServerWillNotObeyTheBoothAtAll()
    {
        ConsoleJson.Write(Report(accepts: false)).Should().Contain("\"acceptsCommands\":false");
    }

    [TestMethod]
    public void CarriesTheWatermarkSoTheConsoleCanTellASentCommandFromARunOne()
    {
        ConsoleJson.Write(Report(ran: 12)).Should().Contain("\"ran\":12");
    }

    [TestMethod]
    public void IsItsOwnDocumentWithItsOwnVersion()
    {
        ConsoleJson.Write(Report()).Should().StartWith("{\"v\":1,");
    }

    [TestMethod]
    public void TwoReportsATickApartAreTheSameNews()
    {
        Report(at: Noon).Equals(Report(at: Noon.AddSeconds(1))).Should().BeTrue(
            "comparing the clock would turn a change-driven channel into a per-tick one");
    }

    [TestMethod]
    public void SomebodyBecomingAGhostIsNews()
    {
        ConsoleReport before = Report(connected: new[] { new ConsolePresence("Ada", true) });
        ConsoleReport after = Report(connected: new[] { new ConsolePresence("Ada", false) });

        before.Equals(after).Should().BeFalse();
    }

    [TestMethod]
    public void SomebodyJoiningIsNews()
    {
        ConsoleReport before = Report(connected: new[] { new ConsolePresence("Ada", true) });
        ConsoleReport after = Report(connected: new[]
        {
            new ConsolePresence("Ada", true), new ConsolePresence("Bea", true),
        });

        before.Equals(after).Should().BeFalse();
    }

    [TestMethod]
    public void AnAnswerIsNews()
    {
        ConsoleReport before = Report();
        ConsoleReport after = Report(answers: new[] { new BoothCommandAnswer(1, "status", "in the lobby") });

        before.Equals(after).Should().BeFalse();
    }
}

/// <summary>When the console channel is posted: on change and on a slow heartbeat.</summary>
[TestClass]
public class ConsolePumpTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static ConsoleReport Report(params string[] connected)
    {
        List<ConsolePresence> present = new List<ConsolePresence>();
        foreach (string name in connected)
        {
            present.Add(new ConsolePresence(name, true));
        }

        return new ConsoleReport(Noon, present, ConsoleJson.Vocabulary(),
            new BoothCommandAnswer[0], 0, true);
    }

    [TestMethod]
    public void SendsTheFirstOneStraightAway()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        pump.IsDue(Noon, Report("Ada")).Should().BeTrue(
            "a booth with no report at all cannot draw the console");
    }

    [TestMethod]
    public void SaysNothingWhileNothingChanges()
    {
        ConsolePump pump = new ConsolePump(Timeout);
        ConsoleReport report = Report("Ada");

        pump.Take(Noon, report);
        pump.Accepted();

        pump.IsDue(Noon.AddSeconds(5), report).Should().BeFalse();
    }

    [TestMethod]
    public void SpeaksTheMomentSomebodyJoins()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        pump.Take(Noon, Report("Ada"));
        pump.Accepted();

        pump.IsDue(Noon.AddSeconds(1), Report("Ada", "Bea")).Should().BeTrue();
    }

    [TestMethod]
    public void RepeatsItselfOnTheHeartbeatSoARestartedBoothGetsTheRosterBack()
    {
        ConsolePump pump = new ConsolePump(Timeout);
        ConsoleReport report = Report("Ada");

        pump.Take(Noon, report);
        pump.Accepted();

        pump.IsDue(Noon + ConsolePump.Heartbeat - TimeSpan.FromSeconds(1), report).Should().BeFalse();
        pump.IsDue(Noon + ConsolePump.Heartbeat, report).Should().BeTrue();
    }

    [TestMethod]
    public void NeverHasTwoInFlight()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        pump.Take(Noon, Report("Ada"));

        pump.IsDue(Noon.AddSeconds(1), Report("Ada", "Bea")).Should().BeFalse(
            "two in flight race to be the report the booth keeps");
    }

    [TestMethod]
    public void AbandonsAnUnansweredReportRatherThanGoingSilentForTheWeekend()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        pump.Take(Noon, Report("Ada"));

        pump.IsDue(Noon + Timeout + ConsolePump.Heartbeat, Report("Ada")).Should().BeTrue();
    }

    [TestMethod]
    public void ARefusedReportIsSentAgainBecauseItDidNotHappen()
    {
        ConsolePump pump = new ConsolePump(Timeout);
        ConsoleReport report = Report("Ada");

        pump.Take(Noon, report);
        pump.Refused();

        pump.IsDue(Noon.AddSeconds(1), report).Should().BeFalse("it backs off while the booth is down");
        pump.IsDue(Noon + ConsolePump.Heartbeat, report).Should().BeTrue();
    }

    [TestMethod]
    public void OnlyAnAcceptedReportSaysWhatMayBeForgotten()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        ConsoleReport carrying = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) }, ConsoleJson.Vocabulary(),
            new[] { new BoothCommandAnswer(1, "status", "in the lobby") }, 1, true);

        pump.Take(Noon, carrying);

        pump.Refused();

        pump.Accepted().Answers.Should().Be(0, "a refused report forgets nothing");
    }

    [TestMethod]
    public void AnAcceptedReportSaysHowManyAnswersItCarried()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        ConsoleReport carrying = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) }, ConsoleJson.Vocabulary(),
            new[]
            {
                new BoothCommandAnswer(1, "status", "in the lobby"),
                new BoothCommandAnswer(2, "start", "the match is running"),
            }, 2, true);

        pump.Take(Noon, carrying);

        pump.Accepted().Answers.Should().Be(2);
    }

    [TestMethod]
    public void AnAcceptedReportSaysHowManyRulesOutcomesItCarried()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        ConsoleReport carrying = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) }, ConsoleJson.Vocabulary(),
            new BoothCommandAnswer[0], 0, true, 4,
            new[] { new BoothRulesOutcome(4, true, "the rules are revision 4", null) });

        pump.Take(Noon, carrying);

        pump.Accepted().Rules.Should().Be(1,
            "the outcome went to the booth, so it is the one thing that may be forgotten");
    }

    [TestMethod]
    public void CountsTheTwoQueuesSeparatelyBecauseTheyAreUnrelated()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        ConsoleReport carrying = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) }, ConsoleJson.Vocabulary(),
            new[]
            {
                new BoothCommandAnswer(1, "status", "in the lobby"),
                new BoothCommandAnswer(2, "start", "the match is running"),
            }, 2, true, 4,
            new[] { new BoothRulesOutcome(4, true, "the rules are revision 4", null) });

        pump.Take(Noon, carrying);

        ConsoleCarried carried = pump.Accepted();

        carried.Answers.Should().Be(2);
        carried.Rules.Should().Be(1, "one number for both is the defect");
    }

    [TestMethod]
    public void ARefusedReportForgetsNeitherQueue()
    {
        ConsolePump pump = new ConsolePump(Timeout);

        ConsoleReport carrying = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) }, ConsoleJson.Vocabulary(),
            new[] { new BoothCommandAnswer(1, "status", "in the lobby") }, 1, true, 4,
            new[] { new BoothRulesOutcome(4, true, "the rules are revision 4", null) });

        pump.Take(Noon, carrying);
        pump.Refused();

        ConsoleCarried carried = pump.Accepted();

        carried.Answers.Should().Be(0, "a refused report forgets nothing");
        carried.Rules.Should().Be(0, "and that is as true of the revision as it is of the verb");
    }

    [TestMethod]
    public void ATimeoutOfNothingIsRefusedRatherThanAbandoningEveryReport()
    {
        Action none = () => _ = new ConsolePump(TimeSpan.Zero);

        none.Should().Throw<ArgumentOutOfRangeException>();
    }


    [TestMethod]
    public void The_report_carries_the_config_the_server_is_actually_running()
    {
        ConsoleReport report = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) },
            ConsoleJson.Vocabulary(), new BoothCommandAnswer[0], 0, true,
            running: "match:\n  cycleLength: 10m\n");

        ConsoleJson.Write(report).Should().Contain("\"config\":");
        ConsoleJson.Write(report).Should().Contain("cycleLength: 10m");
    }

    [TestMethod]
    public void A_server_with_no_config_sends_an_empty_one_rather_than_omitting_it()
    {
        ConsoleReport report = Report();

        ConsoleJson.Write(report).Should().Contain("\"config\":\"\"");
    }

    [TestMethod]
    public void Two_reports_differing_only_in_the_config_are_not_equal()
    {
        ConsoleReport before = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) },
            ConsoleJson.Vocabulary(), new BoothCommandAnswer[0], 0, true, running: "a: 1");

        ConsoleReport after = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) },
            ConsoleJson.Vocabulary(), new BoothCommandAnswer[0], 0, true, running: "a: 2");

        before.Equals(after).Should().BeFalse();
        before.Equals(before).Should().BeTrue();
    }

    [TestMethod]
    public void A_config_full_of_quotes_and_newlines_survives_the_encoder()
    {
        ConsoleReport report = new ConsoleReport(Noon,
            new[] { new ConsolePresence("Ada", true) },
            ConsoleJson.Vocabulary(), new BoothCommandAnswer[0], 0, true,
            running: "zone:\n  colours:\n    now: \"#FFD65C\"\n");

        string json = ConsoleJson.Write(report);

        json.Should().Contain("\\n");
        json.Should().Contain("\\\"#FFD65C\\\"");
    }
}
