using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Spawning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The two pod verbs, which are the ones that do nothing to the clock.</summary>
[TestClass]
public class MatchHostPodVerbTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static IReadOnlyList<AuditRecordKind> Kinds(MatchHost host) =>
        host.Audit.Records.Select(record => record.Kind).ToList();

    [TestMethod]
    public void A_host_with_no_server_behind_it_says_so_rather_than_throwing()
    {
        MatchHost host = NewHost();

        string answer = host.Command("podspawn", Noon);

        answer.Should().Contain("no server");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void The_verb_reaches_whatever_the_adapter_set()
    {
        MatchHost host = NewHost();
        int asked = 0;
        host.SeatsTheGroup = () =>
        {
            asked++;
            return new PodCommandOutcome(5, "5 players onto their pods");
        };

        host.Command("podspawn", Noon).Should().Be("5 players onto their pods");
        asked.Should().Be(1);
    }

    [TestMethod]
    public void Seating_somebody_is_written_down()
    {
        MatchHost host = NewHost();
        host.SeatsTheGroup = () => new PodCommandOutcome(5, "done", detail: "5 players, 0 pods made");

        host.Command("podspawn", Noon);

        Kinds(host).Should().Equal(AuditRecordKind.PodsPlaced);
        host.Audit.Records.Single().Detail.Should().Be("5 players, 0 pods made");
    }

    [TestMethod]
    public void Seating_nobody_is_not()
    {
        MatchHost host = NewHost();
        host.SeatsTheGroup = () => PodCommandOutcome.Nothing("nobody is connected");

        host.Command("podspawn", Noon).Should().Be("nobody is connected");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void The_clock_is_untouched_by_it()
    {
        MatchHost host = NewHost();
        host.SeatsTheGroup = () => new PodCommandOutcome(5, "done");

        string before = host.Status(Noon);
        host.Command("podspawn", Noon);

        host.Status(Noon).Should().Be(before);
        host.Group.Should().Be(1);
        host.Generation.Should().Be(1);
    }

    [TestMethod]
    public void It_is_spelled_pods_as_well()
    {
        MatchHost host = NewHost();
        host.SeatsTheGroup = () => new PodCommandOutcome(3, "3 players onto their pods");

        host.Command("pods", Noon).Should().Be("3 players onto their pods");
    }

    [TestMethod]
    public void It_is_in_the_usage_line()
    {
        NewHost().Command("podspwan", Noon).Should().Contain("podspawn");
    }

    [TestMethod]
    public void Podclear_is_in_the_usage_line_too()
    {
        NewHost().Command("podcelar", Noon).Should().Contain("podclear");
    }

    [TestMethod]
    public void Seating_the_group_never_reaches_the_clearing()
    {
        MatchHost host = NewHost();
        int cleared = 0;
        host.SeatsTheGroup = () => new PodCommandOutcome(5, "seated");
        host.ClearsTheDerelicts = () =>
        {
            cleared++;
            return new PodCommandOutcome(1, "cleared");
        };

        host.Command("podspawn", Noon);

        cleared.Should().Be(0);
    }

    [TestMethod]
    public void Clearing_something_is_written_down()
    {
        MatchHost host = NewHost();
        host.ClearsTheDerelicts = () =>
            new PodCommandOutcome(2, "2 derelicts gone", detail: "2 pods, 3 things");

        host.Command("podclear", Noon).Should().Be("2 derelicts gone");

        Kinds(host).Should().Equal(AuditRecordKind.PodsCleared);
        host.Audit.Records.Single().Detail.Should().Be("2 pods, 3 things");
    }

    [TestMethod]
    public void Clearing_nothing_is_not()
    {
        MatchHost host = NewHost();
        host.ClearsTheDerelicts = () => PodCommandOutcome.Nothing("no derelicts");

        host.Command("podclear", Noon).Should().Be("no derelicts");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void Clearing_with_no_server_behind_it_says_so_rather_than_throwing()
    {
        MatchHost host = NewHost();

        host.Command("podclear", Noon).Should().Contain("no server");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void Clearing_leaves_the_clock_alone()
    {
        MatchHost host = NewHost();
        host.ClearsTheDerelicts = () => new PodCommandOutcome(2, "done");

        string before = host.Status(Noon);
        host.Command("podclear", Noon);

        host.Status(Noon).Should().Be(before);
        host.Generation.Should().Be(1);
    }


    [TestMethod]
    public void Bare_podclear_never_reaches_the_sweep()
    {
        MatchHost host = NewHost();
        int swept = 0;
        host.ClearsTheDerelicts = () => new PodCommandOutcome(1, "one derelict gone");
        host.ClearsEveryPod = () =>
        {
            swept++;
            return new PodCommandOutcome(5, "five pods gone");
        };

        host.Command("podclear", Noon).Should().Be("one derelict gone");
        swept.Should().Be(0);
    }

    [TestMethod]
    public void The_sweep_never_reaches_the_derelict_rule_either()
    {
        MatchHost host = NewHost();
        int derelicts = 0;
        host.ClearsTheDerelicts = () =>
        {
            derelicts++;
            return new PodCommandOutcome(1, "one derelict gone");
        };
        host.ClearsEveryPod = () => new PodCommandOutcome(5, "five pods gone");

        host.Command("podclear all", Noon).Should().Be("five pods gone");
        derelicts.Should().Be(0);
    }

    [TestMethod]
    public void Sweeping_is_written_down_as_pods_cleared()
    {
        MatchHost host = NewHost();
        host.ClearsEveryPod = () =>
            new PodCommandOutcome(5, "5 pods gone", detail: "5 pods removed (all), 3 things");

        host.Command("podclear all", Noon);

        Kinds(host).Should().Equal(AuditRecordKind.PodsCleared);
        host.Audit.Records.Single().Detail.Should().Be("5 pods removed (all), 3 things");
    }

    [TestMethod]
    public void A_refused_sweep_is_not()
    {
        MatchHost host = NewHost();
        host.ClearsEveryPod = () => PodCommandOutcome.Nothing("2 players are standing in a pod");

        host.Command("podclear all", Noon).Should().Be("2 players are standing in a pod");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void Sweeping_with_no_server_behind_it_says_so_rather_than_throwing()
    {
        MatchHost host = NewHost();

        host.Command("podclear all", Noon).Should().Contain("no server");
        Kinds(host).Should().BeEmpty();
    }

    [TestMethod]
    public void Sweeping_leaves_the_clock_alone()
    {
        MatchHost host = NewHost();
        host.ClearsEveryPod = () => new PodCommandOutcome(5, "done");

        string before = host.Status(Noon);
        host.Command("podclear all", Noon);

        host.Status(Noon).Should().Be(before);
        host.Generation.Should().Be(1);
    }
}
