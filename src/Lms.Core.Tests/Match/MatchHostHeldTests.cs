using System;
using System.IO;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Whether the log says a ring is holding somebody.</summary>
[TestClass]
public class MatchHostHeldTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly WorldPoint Inside = new WorldPoint(750, -110, 450);

    private readonly PlayerRoster roster = new PlayerRoster();

    private MatchHost Playing()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        MatchHost host = new MatchHost(result.Config!, Heartbeat);
        host.Command("start", Noon);
        Tick(host, Noon);

        return host;
    }

    private void Tick(MatchHost host, TimeSpan worldNow) =>
        host.Record(roster.Update(
            new[] { new PlayerReading(1, "Vela", Inside, true, 92, 40, 70, 65) },
            host.Zone(worldNow).Sphere, host.Generation).Changes);

    private void Says(MatchHost host, AuditRecordKind kind, TimeSpan at)
    {
        roster.Reported(1, new PlayerEventBatch(host.Generation,
            new[] { new PlayerEvent(kind, at - Noon) }));
        Tick(host, at);
    }

    [TestMethod]
    public void Nobody_is_held_in_a_match_where_no_ring_has_been_fired()
    {
        Playing().IsHeld("Vela").Should().BeFalse();
    }

    [TestMethod]
    public void A_name_the_log_has_never_heard_of_is_not_held()
    {
        MatchHost host = Playing();

        host.IsHeld("Nobody").Should().BeFalse();
        host.IsHeld(null).Should().BeFalse();
        host.IsHeld("  ").Should().BeFalse();
    }

    [TestMethod]
    public void A_caged_player_is_held()
    {
        MatchHost host = Playing();

        Says(host, AuditRecordKind.PlayerCaged, Noon + TimeSpan.FromSeconds(10));

        host.IsHeld("Vela").Should().BeTrue();
    }

    [TestMethod]
    public void A_released_player_is_not_held_any_more()
    {
        MatchHost host = Playing();

        Says(host, AuditRecordKind.PlayerCaged, Noon + TimeSpan.FromSeconds(10));
        Says(host, AuditRecordKind.PlayerReleased, Noon + TimeSpan.FromSeconds(30));

        host.IsHeld("Vela").Should().BeFalse();
    }

    [TestMethod]
    public void A_player_who_dropped_while_caged_is_not_held_when_they_come_back()
    {
        MatchHost host = Playing();
        Says(host, AuditRecordKind.PlayerCaged, Noon + TimeSpan.FromSeconds(10));

        host.Record(roster.Update(Array.Empty<PlayerReading>(),
            host.Zone(Noon + TimeSpan.FromSeconds(20)).Sphere, host.Generation).Changes);
        host.IsHeld("Vela").Should().BeFalse("they are not here to be held");

        Tick(host, Noon + TimeSpan.FromSeconds(40));
        host.IsHeld("Vela").Should().BeFalse("and the ring did not come back with them");
    }
}
