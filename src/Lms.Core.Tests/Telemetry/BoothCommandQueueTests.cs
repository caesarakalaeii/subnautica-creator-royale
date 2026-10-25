using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The mod's half of the reverse channel: command ordering and resend de-duplication.</summary>
[TestClass]
public class BoothCommandQueueTests
{
    private static IReadOnlyList<BoothCommand> Offered(params (long Id, string Verb)[] commands)
    {
        List<BoothCommand> offered = new List<BoothCommand>();
        foreach ((long id, string verb) in commands)
        {
            offered.Add(new BoothCommand(id, verb));
        }

        return offered;
    }

    private static string Take(BoothCommandQueue queue)
    {
        queue.TryTake(out BoothCommand command).Should().BeTrue("something was waiting");

        return command.Verb;
    }

    [TestMethod]
    public void RunsWhatTheBoothOfferedInTheOrderItWasAskedFor()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        queue.Offer(Offered((3, "podspawn"), (1, "podclear all"), (2, "status"))).Should().Be(3);

        Take(queue).Should().Be("podclear all");
        Take(queue).Should().Be("status");
        Take(queue).Should().Be("podspawn");

        queue.TryTake(out _).Should().BeFalse();
    }

    [TestMethod]
    public void TakingDoesNotMoveTheWatermarkBecauseATakenCommandMayStillNotHaveRun()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Offer(Offered((7, "start")));

        Take(queue);

        queue.Ran.Should().Be(0, "nothing has been answered for yet");

        queue.Answered(7, "start", "the match is running");

        queue.Ran.Should().Be(7);
    }

    [TestMethod]
    public void ARepeatIsIgnoredBecauseTheBoothResendsUntilItIsTold()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Offer(Offered((4, "skip")));
        Take(queue);
        queue.Answered(4, "skip", "phase 3 of 6");

        queue.Offer(Offered((4, "skip"))).Should().Be(0);

        queue.TryTake(out _).Should().BeFalse("running a skip twice would skip two phases");
    }

    [TestMethod]
    public void AnOfferAtOrBelowTheWatermarkIsDroppedWhicheverCommandItIs()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Offer(Offered((5, "pause")));
        Take(queue);
        queue.Answered(5, "pause", "paused");

        queue.Offer(Offered((2, "start"), (5, "pause"), (6, "resume"))).Should().Be(1);

        Take(queue).Should().Be("resume");
    }

    [TestMethod]
    public void TheSameCommandOfferedTwiceBeforeItRunsIsQueuedOnce()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        queue.Offer(Offered((9, "reset"))).Should().Be(1);
        queue.Offer(Offered((9, "reset"))).Should().Be(0);

        queue.Waiting.Should().Be(1);
    }

    [TestMethod]
    public void AnswersAreKeptForTheConsoleUntilTheBoothHasTakenThem()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Offer(Offered((1, "round")));
        Take(queue);

        queue.Answered(1, "round", "a group is 5 and this counted 4: Ada, Bea, Cy, Dov.");

        queue.Answers.Should().HaveCount(1);
        queue.Answers[0].Answer.Should().Be("a group is 5 and this counted 4: Ada, Bea, Cy, Dov.",
            "the sentence is the help system, so it travels word for word");

        queue.Forget(1);

        queue.Answers.Should().BeEmpty();
    }

    [TestMethod]
    public void ForgettingTakesAPrefixSoAnAnswerAddedSinceTheReportIsStillOwed()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Answered(1, "status", "in the lobby");

        int reported = queue.Answers.Count;

        queue.Answered(2, "start", "the match is running");

        queue.Forget(reported);

        queue.Answers.Should().HaveCount(1);
        queue.Answers[0].Id.Should().Be(2);
    }

    [TestMethod]
    public void ARefusedReportLeavesEveryAnswerOwed()
    {
        BoothCommandQueue queue = new BoothCommandQueue();
        queue.Answered(1, "status", "in the lobby");

        queue.Answers.Should().HaveCount(1);
    }

    [TestMethod]
    public void SwitchedOffItObeysNothingAndTellsTheBoothNothing()
    {
        BoothCommandQueue queue = new BoothCommandQueue(accepts: false);

        queue.Offer(Offered((1, "start"))).Should().Be(0);
        queue.TryTake(out _).Should().BeFalse();

        queue.Cursor.Should().BeNull(
            "no header means the booth offers nothing rather than queueing what nobody will run");
    }

    [TestMethod]
    public void SwitchedOnItTellsTheBoothHowFarItHasGot()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        queue.Cursor.Should().Be(0, "a fresh run has run nothing, and says so");

        queue.Answered(12, "next", "group 2 of 5");

        queue.Cursor.Should().Be(12);
    }

    [TestMethod]
    public void ARunOfNonsenseFromWhateverIsOnThatUrlCannotGrowWithoutBound()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        for (long id = 1; id <= 500; id++)
        {
            queue.Offer(Offered((id, "status")));
        }

        queue.Waiting.Should().BeLessThan(500);
        queue.Waiting.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void TheOldestCommandIsTheOneKeptWhenTheQueueIsFull()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        for (long id = 1; id <= 500; id++)
        {
            queue.Offer(Offered((id, "status")));
        }

        queue.TryTake(out BoothCommand next).Should().BeTrue();
        next.Id.Should().Be(1, "the oldest is the one somebody has been waiting on");
    }

    [TestMethod]
    public void AnAnswerBacklogIsBoundedTooWhileTheBoothIsRefusingThem()
    {
        BoothCommandQueue queue = new BoothCommandQueue();

        int dropped = 0;
        for (long id = 1; id <= 500; id++)
        {
            dropped += queue.Answered(id, "status", "in the lobby");
        }

        queue.Answers.Should().HaveCountLessThan(500);
        dropped.Should().BeGreaterThan(0, "and the caller is told, rather than losing them quietly");
    }

    [TestMethod]
    public void ACommandWithNoIdOrNoVerbIsNotACommand()
    {
        Action noId = () => _ = new BoothCommand(0, "start");
        Action noVerb = () => _ = new BoothCommand(1, "   ");

        noId.Should().Throw<ArgumentOutOfRangeException>();
        noVerb.Should().Throw<ArgumentException>();
    }
}
