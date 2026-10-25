using System;
using System.IO;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The match and wall projections sent to the booth.</summary>
[TestClass]
public class TelemetryProjectionTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    [TestMethod]
    public void The_lobby_is_a_match_the_booth_can_draw()
    {
        MatchTelemetry match = NewHost().Telemetry(Noon, alive: 5);

        match.Group.Should().Be(1);
        match.Generation.Should().Be(1);
        match.State.Should().Be(MatchClockState.NotStarted);
        match.Phase.Should().Be(0);
        match.Phases.Should().Be(4, "three authored and then the collapse, which is a phase like any other");
        match.Alive.Should().Be(5);
    }

    [TestMethod]
    public void Nothing_is_counting_down_in_the_lobby()
    {
        NewHost().Telemetry(Noon, alive: 5).UntilNextPhase.Should().BeNull();
    }

    [TestMethod]
    public void A_running_match_counts_down_to_its_next_phase()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        MatchTelemetry match = host.Telemetry(Noon + TimeSpan.FromSeconds(20), alive: 5);

        match.State.Should().Be(MatchClockState.Running);
        match.Elapsed.Should().Be(TimeSpan.FromSeconds(20));
        match.UntilNextPhase.Should().Be(TimeSpan.FromSeconds(40));
    }

    [TestMethod]
    public void A_paused_match_is_not_counting_down_to_anything()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("pause", Noon + TimeSpan.FromSeconds(20));

        MatchTelemetry match = host.Telemetry(Noon + TimeSpan.FromSeconds(40), alive: 5);

        match.State.Should().Be(MatchClockState.Paused);
        match.Elapsed.Should().Be(TimeSpan.FromSeconds(20));
        match.UntilNextPhase.Should().BeNull();
    }

    [TestMethod]
    public void A_spent_squeeze_counts_down_to_nothing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        MatchTelemetry match = host.Telemetry(Noon + TimeSpan.FromMinutes(6), alive: 1);

        match.Phase.Should().Be(4, "the collapse fired, and the count it is measured against includes it");
        match.UntilNextPhase.Should().BeNull();
    }

    [TestMethod]
    public void The_booth_is_told_where_the_wall_is_and_where_it_is_going()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("skip", Noon + TimeSpan.FromSeconds(1));

        ZoneTelemetry zone = ZoneTelemetry.Of(host.Zone(Noon + TimeSpan.FromSeconds(16)));

        zone.Now.Radius.Should().BeInRange(900, 1400);
        zone.Next.Should().NotBeNull();
        zone.Next!.Value.Radius.Should().Be(400, "the map's second ring is where the wall is heading");
        zone.DamagePerTick.Should().Be(3);
    }

    [TestMethod]
    public void The_last_phase_has_nowhere_left_to_point_the_second_ring()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        ZoneTelemetry zone = ZoneTelemetry.Of(host.Zone(Noon + TimeSpan.FromMinutes(6)));

        zone.Next.Should().BeNull("the collapse closes onto its own centre");
    }

    [TestMethod]
    public void The_wall_travels_to_the_booth_as_four_numbers()
    {
        ZoneTelemetry zone = ZoneTelemetry.Of(NewHost().Zone(Noon));

        zone.Now.X.Should().Be(200);
        zone.Now.Y.Should().Be(-15);
        zone.Now.Z.Should().Be(100);
        zone.Now.Radius.Should().Be(1400);
    }
}
