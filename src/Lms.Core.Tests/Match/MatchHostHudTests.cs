using System;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What the player's own HUD is told.</summary>
[TestClass]
public class MatchHostHudTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    private static MatchHost Host(bool showDestination)
    {
        string flags = showDestination ? "\nflags:\n  showDestination: true\n" : string.Empty;

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(@"
match:
  cycleLength: 5m
  warningLead: 1m
zone:
  damageInterval: 2s
  destinationSeed: 7
  destinations:
    # Near the opening centre on purpose: the loader checks a destination is reachable by the
    # squeeze it is given, and this fixture is about the HUD rather than about the arithmetic.
    - name: Jellyshroom Cave
      centre: [200, -150, 100]
    - name: The Ghost Tree
      centre: [-150, -200, -180]
  opening:
    centre: [0, -100, 0]
    radius: 1400
    damagePerTick: 1
  phases:
    - radius: 900
      shrinkFor: 90s
      damagePerTick: 3
" + flags);

        result.Problems.Should().BeEmpty();

        return new MatchHost(result.Config!, TimeSpan.FromSeconds(10));
    }

    private static MatchBroadcast Read(string line)
    {
        MatchBroadcast.TryDecode(line, out MatchBroadcast broadcast).Should().BeTrue();

        return broadcast;
    }

    [TestMethod]
    public void The_count_the_caller_had_in_hand_is_what_travels()
    {
        Read(Host(showDestination: false).Line(Noon, alive: 4)).Alive.Should().Be(4);
    }

    [TestMethod]
    public void A_caller_with_no_count_says_nothing_rather_than_zero()
    {
        Read(Host(showDestination: false).Line(Noon)).Alive.Should().BeNull();
    }

    [TestMethod]
    public void The_destination_is_not_on_the_wire_at_all_unless_the_flag_is_on()
    {
        string line = Host(showDestination: false).Line(Noon, alive: 5);

        line.Should().NotContain("dest=");
        Read(line).Destination.Should().BeNull();
    }

    [TestMethod]
    public void The_destination_is_named_when_the_flag_is_on()
    {
        string? named = Read(Host(showDestination: true).Line(Noon, alive: 5)).Destination;

        named.Should().BeOneOf("Jellyshroom Cave", "The Ghost Tree");
    }

    [TestMethod]
    public void It_is_the_same_place_the_audit_log_names()
    {
        MatchHost host = Host(showDestination: true);

        string? told = Read(host.Line(Noon, alive: 5)).Destination;

        host.Command("start", Noon);

        host.Audit.Records.Should().Contain(record =>
            record.Kind == Lms.Core.Audit.AuditRecordKind.ZoneDestinationDrawn
            && record.Detail == told);
    }
}
