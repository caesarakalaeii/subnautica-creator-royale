using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The client half of the match: it answers from the last broadcast and the shared world clock, so late joiners agree with everyone else.</summary>
[TestClass]
public class MatchFollowerTests
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WarningLead = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);
    private static readonly WorldPoint Second = new WorldPoint(200, -150, 100);

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Spawn, 1400), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(Second, 400), TimeSpan.FromSeconds(90), 8),
        });

    private static MatchDirector Server()
    {
        MatchDirector director = new MatchDirector(new MatchClockSettings(Cycle, WarningLead), Schedule());
        director.Execute(AdminCommand.Start, Noon);
        return director;
    }

    private static IReadOnlyList<MatchClockEvent> Tell(MatchDirector server, MatchFollower client, TimeSpan at)
    {
        server.AdvanceTo(at);
        client.Receive(server.Broadcast());
        return client.AdvanceTo(at);
    }

    [TestMethod]
    public void A_client_that_has_been_told_nothing_shows_no_match()
    {
        MatchFollower client = new MatchFollower();

        client.HasMatch.Should().BeFalse();
        client.IsLive.Should().BeFalse();
        client.AdvanceTo(Noon).Should().BeEmpty();
        client.Status.Should().Contain("no match");
    }

    [TestMethod]
    public void A_client_draws_the_wall_the_server_sent_it()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();

        Tell(server, client, Noon + Cycle + TimeSpan.FromSeconds(30));

        client.Zone.Should().BeEquivalentTo(server.Zone);
        client.Status.Should().Be(server.Status);
        client.IsLive.Should().BeTrue();
        client.CompletedPhases.Should().Be(1);
        client.TotalPhases.Should().Be(2);
    }

    [TestMethod]
    public void A_client_keeps_the_wall_closing_between_broadcasts()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + Cycle);

        client.AdvanceTo(Noon + Cycle + TimeSpan.FromSeconds(45));

        client.Zone.Sphere.Radius.Should().BeApproximately(1150, 1e-9);
    }

    [TestMethod]
    public void A_phase_is_announced_once_when_the_server_says_it_started()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon);

        IReadOnlyList<MatchClockEvent> started = Tell(server, client, Noon + Cycle);

        started.Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseStarted && e.PhaseNumber == 1);
        client.AdvanceTo(Noon + Cycle + TimeSpan.FromSeconds(1)).Should().BeEmpty();
        Tell(server, client, Noon + Cycle + TimeSpan.FromSeconds(2)).Should().BeEmpty("the same phase is not news twice");
    }

    [TestMethod]
    public void A_phase_warning_comes_off_the_clock_rather_than_off_a_packet()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon);

        client.AdvanceTo(Noon + TimeSpan.FromMinutes(3)).Should().BeEmpty();

        IReadOnlyList<MatchClockEvent> warned = client.AdvanceTo(Noon + TimeSpan.FromMinutes(4));

        warned.Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseWarning && e.PhaseNumber == 1);
        client.IsWarningActive.Should().BeTrue();
        client.AdvanceTo(Noon + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(30))
              .Should().BeEmpty("a warning is announced once");
    }

    [TestMethod]
    public void A_client_joining_a_match_in_progress_adopts_it_without_a_word()
    {
        MatchDirector server = Server();
        TimeSpan late = Noon + TimeSpan.FromMinutes(16);
        server.AdvanceTo(late);

        MatchFollower client = new MatchFollower();
        client.Receive(server.Broadcast());

        client.AdvanceTo(late).Should().BeEmpty();
        client.Zone.Should().BeEquivalentTo(server.Zone);
        client.CompletedPhases.Should().Be(3);
    }

    [TestMethod]
    public void A_client_that_missed_a_broadcast_says_where_the_match_is_rather_than_where_it_was()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon);

        IReadOnlyList<MatchClockEvent> events = Tell(server, client, Noon + TimeSpan.FromMinutes(11));

        events.Where(e => e.Kind == MatchClockEventKind.PhaseStarted)
              .Should().ContainSingle().Which.PhaseNumber.Should().Be(2);
    }

    [TestMethod]
    public void A_reset_puts_the_client_back_in_the_lobby_with_a_new_generation()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + TimeSpan.FromMinutes(6));
        int played = client.Generation;

        server.Execute(AdminCommand.Reset, Noon + TimeSpan.FromMinutes(7));
        client.Receive(server.Broadcast());

        client.AdvanceTo(Noon + TimeSpan.FromMinutes(7)).Should().BeEmpty("a lobby has nothing to announce");
        client.Generation.Should().Be(played + 1, "per-player state belongs to a match, not a client");
        client.CompletedPhases.Should().Be(0);
        client.IsLive.Should().BeFalse();
        client.Zone.Sphere.Should().Be(new ZoneSphere(Spawn, 1400));
    }

    [TestMethod]
    public void A_phase_after_a_reset_is_announced_again()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + Cycle);

        server.Execute(AdminCommand.Reset, Noon + Cycle);
        server.Execute(AdminCommand.Start, Noon + Cycle);
        client.Receive(server.Broadcast());
        client.AdvanceTo(Noon + Cycle);

        IReadOnlyList<MatchClockEvent> events = Tell(server, client, Noon + Cycle + Cycle);

        events.Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseStarted && e.PhaseNumber == 1);
    }

    [TestMethod]
    public void A_skip_reaches_the_client_as_the_phase_it_brought_forward()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + TimeSpan.FromMinutes(1));

        server.Execute(AdminCommand.Skip, Noon + TimeSpan.FromMinutes(1));
        client.Receive(server.Broadcast());

        client.AdvanceTo(Noon + TimeSpan.FromMinutes(1))
              .Should().ContainSingle(e => e.Kind == MatchClockEventKind.PhaseStarted && e.PhaseNumber == 1);
        client.Zone.Should().BeEquivalentTo(server.Zone);
    }

    [TestMethod]
    public void The_zone_stays_shut_once_the_collapse_has_finished()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();

        Tell(server, client, Noon + TimeSpan.FromHours(1));

        client.IsCollapsing.Should().BeTrue();
        client.IsClosed.Should().BeTrue();
        client.Zone.Sphere.Radius.Should().Be(0);
    }

    [TestMethod]
    public void Leaving_a_session_forgets_the_match()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + Cycle);

        client.Forget();

        client.HasMatch.Should().BeFalse();
        client.IsLive.Should().BeFalse();
        client.IsClosed.Should().BeFalse("a client with no match has no wall, closed or otherwise");
    }

    [TestMethod]
    public void A_client_draws_the_wall_at_whatever_moment_it_is_handed()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon + Cycle + TimeSpan.FromSeconds(45));

        client.AdvanceTo(Noon + Cycle + TimeSpan.FromSeconds(15));

        client.Zone.Sphere.Radius.Should().BeApproximately(1150 + 250 * 2 / 3d, 1e-9,
            "fifteen seconds into the shrink, not forty-five");
    }

    [TestMethod]
    public void A_clock_that_steps_backwards_does_not_announce_anything_twice()
    {
        MatchDirector server = Server();
        MatchFollower client = new MatchFollower();
        Tell(server, client, Noon);

        client.AdvanceTo(Noon + TimeSpan.FromMinutes(4)).Should().HaveCount(1, "the warning");
        client.AdvanceTo(Noon + TimeSpan.FromMinutes(3)).Should().BeEmpty();
        client.AdvanceTo(Noon + TimeSpan.FromMinutes(4)).Should().BeEmpty();
    }
}
