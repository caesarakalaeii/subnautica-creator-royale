using System;
using System.IO;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The server side of the match: one match, one clock, and the decision of when clients need telling.</summary>
[TestClass]
public class MatchHostTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static TournamentConfig Config()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return result.Config!;
    }

    private static MatchHost NewHost() => new MatchHost(Config(), Heartbeat);

    [TestMethod]
    public void A_host_has_something_to_say_before_anybody_has_started_anything()
    {
        MatchHost host = NewHost();

        MatchBroadcast.TryDecode(host.Line(Noon), out MatchBroadcast broadcast).Should().BeTrue();

        broadcast.State.Should().Be(MatchClockState.NotStarted);
        broadcast.ZoneAt(Noon).Sphere.Radius.Should().Be(1400);
    }

    [TestMethod]
    public void The_first_tick_tells_everybody_where_the_zone_is()
    {
        NewHost().Advance(Noon).Should().NotBeNull();
    }

    [TestMethod]
    public void Nothing_is_sent_while_nothing_changes()
    {
        MatchHost host = NewHost();
        host.Advance(Noon);

        host.Advance(Noon + TimeSpan.FromSeconds(1)).Should().BeNull();
        host.Advance(Noon + TimeSpan.FromSeconds(2)).Should().BeNull();
    }

    [TestMethod]
    public void The_heartbeat_says_it_again_in_case_nobody_heard()
    {
        MatchHost host = NewHost();
        host.Advance(Noon);

        host.Advance(Noon + Heartbeat - TimeSpan.FromSeconds(1)).Should().BeNull();
        host.Advance(Noon + Heartbeat).Should().NotBeNull();
        host.Advance(Noon + Heartbeat + TimeSpan.FromSeconds(1)).Should().BeNull("the heartbeat starts again");
    }

    [TestMethod]
    public void A_phase_is_sent_the_moment_it_fires()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Advance(Noon);

        MatchBroadcast.TryDecode(host.Line(Noon + TimeSpan.FromSeconds(59)), out MatchBroadcast waiting)
                      .Should().BeTrue();
        waiting.CompletedPhases.Should().Be(0);

        string? line = host.Advance(Noon + TimeSpan.FromSeconds(60));

        line.Should().NotBeNull();
        MatchBroadcast.TryDecode(line, out MatchBroadcast fired).Should().BeTrue();
        fired.CompletedPhases.Should().Be(1);
    }

    [TestMethod]
    public void An_admin_command_is_on_the_wire_before_the_next_tick()
    {
        MatchHost host = NewHost();
        host.Advance(Noon);

        host.Command("start", Noon).Should().Contain("started");

        MatchBroadcast.TryDecode(host.Advance(Noon), out MatchBroadcast broadcast).Should().BeTrue();
        broadcast.State.Should().Be(MatchClockState.Running);
    }

    [TestMethod]
    public void Asking_where_the_match_is_changes_nothing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Advance(Noon);

        host.Command("status", Noon + TimeSpan.FromSeconds(1)).Should().Contain("group 1");
        host.Advance(Noon + TimeSpan.FromSeconds(1)).Should().BeNull();
    }

    [TestMethod]
    public void No_verb_at_all_is_a_question_rather_than_a_mistake()
    {
        NewHost().Command(null, Noon).Should().Contain("group 1");
    }

    [TestMethod]
    public void A_verb_nobody_implemented_lists_the_ones_that_exist()
    {
        string reply = NewHost().Command("explode", Noon);

        reply.Should().Contain("explode").And.Contain("start").And.Contain("skip");
    }

    [TestMethod]
    public void A_refused_command_says_why_and_repeats_the_status()
    {
        MatchHost host = NewHost();

        host.Command("pause", Noon).Should().Contain("not running").And.Contain("group 1");
    }

    [TestMethod]
    public void What_the_server_sends_is_what_a_client_reads()
    {
        MatchHost host = NewHost();
        MatchFollower client = new MatchFollower();

        host.Command("start", Noon);

        foreach (int second in new[] { 0, 10, 45, 60, 75, 90, 130, 200, 260 })
        {
            TimeSpan at = Noon + TimeSpan.FromSeconds(second);

            string? line = host.Advance(at);
            if (line != null)
            {
                MatchBroadcast.TryDecode(line, out MatchBroadcast broadcast).Should().BeTrue();
                client.Receive(broadcast);
            }

            client.AdvanceTo(at);

            client.Zone.Should().BeEquivalentTo(host.Zone(at), $"at {second}s");
            client.Status.Should().Be(host.Status(at), $"at {second}s");
        }
    }

    [TestMethod]
    public void A_host_needs_a_heartbeat_that_beats()
    {
        Action build = () => new MatchHost(Config(), TimeSpan.Zero);

        build.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_host_needs_a_config()
    {
        Action build = () => new MatchHost(null!, Heartbeat);

        build.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Resetting_a_group_puts_the_opening_sphere_back_on_the_wire()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Advance(Noon + TimeSpan.FromMinutes(3));

        host.Command("reset", Noon + TimeSpan.FromMinutes(3)).Should().Contain("reset");

        MatchBroadcast.TryDecode(host.Advance(Noon + TimeSpan.FromMinutes(3)), out MatchBroadcast broadcast)
                      .Should().BeTrue();
        broadcast.CompletedPhases.Should().Be(0);
        broadcast.ZoneAt(Noon + TimeSpan.FromMinutes(3)).Sphere.Radius.Should().Be(1400);
    }
}
