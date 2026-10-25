using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Configuration;

/// <summary>Zone destination pools: loading from config and wiring into the phase centres.</summary>
[TestClass]
public class ZoneDestinationPoolTests
{
    private const string WithPool = @"
match:
  cycleLength: 5m
  warningLead: 30s
zone:
  destinationSeed: 4071
  opening:
    centre: [0, -400, 0]
    radius: 1400
    damagePerTick: 1
  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
    - name: Crash Zone
      centre: [1092, -91, 296]
    - name: Lost River, near the skull
      centre: [-731, -761, -272]
  phases:
    - radius: 900
      shrinkFor: 90s
      damagePerTick: 3
    - radius: 400
      shrinkFor: 90s
      damagePerTick: 8
    - radius: 120
      shrinkFor: 60s
      damagePerTick: 20
";

    private static TournamentConfigLoadResult Load(string yaml) => TournamentConfigLoader.Load(yaml);

    [TestMethod]
    public void A_pool_loads_and_the_phases_need_no_centres()
    {
        TournamentConfigLoadResult result = Load(WithPool);

        result.Problems.Should().BeEmpty();
        result.Config!.Squeeze.Should().NotBeNull();
        result.Config.Squeeze!.Destinations.Select(d => d.Name)
            .Should().Contain("Jellyshroom Cave").And.Contain("Crash Zone");
    }

    [TestMethod]
    public void The_seed_is_read_and_kept()
    {
        Load(WithPool).Config!.DestinationSeed.Should().Be(4071);
    }

    [TestMethod]
    public void Without_a_pool_nothing_changes()
    {
        TournamentConfigLoadResult result = Load(WithPool
            .Replace("  destinationSeed: 4071\n", string.Empty)
            .Replace(@"  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
    - name: Crash Zone
      centre: [1092, -91, 296]
    - name: Lost River, near the skull
      centre: [-731, -761, -272]
", string.Empty)
            .Replace("    - radius: 900", "    - centre: [0, -400, 0]\n      radius: 900")
            .Replace("    - radius: 400", "    - centre: [0, -400, 0]\n      radius: 400")
            .Replace("    - radius: 120", "    - centre: [0, -400, 0]\n      radius: 120"));

        result.Problems.Should().BeEmpty();
        result.Config!.Squeeze.Should().BeNull();
        result.Config.DestinationFor(1).Should().BeNull();
    }

    [TestMethod]
    public void A_phase_that_also_names_a_centre_is_refused_rather_than_overruled()
    {
        TournamentConfigLoadResult result = Load(WithPool
            .Replace("    - radius: 900", "    - centre: [0, -400, 0]\n      radius: 900"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(p => p.Path == "zone.phases[0].centre");
    }

    [TestMethod]
    public void A_destination_the_squeeze_cannot_reach_refuses_the_file_by_name()
    {
        TournamentConfigLoadResult result = Load(WithPool
            .Replace("      centre: [1092, -91, 296]", "      centre: [1092, -91, 1296]"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(p => p.Path == "zone.destinations" && p.Message.Contains("Crash Zone"));
    }

    [TestMethod]
    public void An_empty_pool_is_refused_rather_than_read_as_no_pool()
    {
        TournamentConfigLoadResult result = Load(WithPool.Replace(@"  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
    - name: Crash Zone
      centre: [1092, -91, 296]
    - name: Lost River, near the skull
      centre: [-731, -761, -272]
", "  destinations: []\n"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(p => p.Path == "zone.destinations");
    }

    [TestMethod]
    public void A_destination_with_no_name_is_refused()
    {
        TournamentConfigLoadResult result = Load(WithPool.Replace("    - name: Crash Zone\n", "    - "));

        result.Config.Should().BeNull();
    }



    private static string Listed => WithPool.Replace("  destinationSeed: 4071\n", "  destinationOrder: listed\n");

    [TestMethod]
    public void A_listed_order_sends_each_group_to_its_own_entry_in_the_file()
    {
        TournamentConfig config = Load(Listed).Config!;

        Enumerable.Range(1, 3).Select(group => config.DestinationFor(group)!.Name).Should().Equal(
            "Jellyshroom Cave", "Crash Zone", "Lost River, near the skull");
    }

    [TestMethod]
    public void A_group_past_the_end_of_the_list_plays_its_last_entry()
    {
        Load(Listed).Config!.DestinationFor(6)!.Name.Should().Be("Lost River, near the skull");
    }

    [TestMethod]
    public void A_listed_order_with_a_seed_as_well_is_refused_rather_than_overruled()
    {
        TournamentConfigLoadResult result = Load(Listed.Replace("  destinationOrder: listed\n",
            "  destinationOrder: listed\n  destinationSeed: 4071\n"));

        result.Problems.Should().ContainSingle(p => p.Path == "zone.destinationOrder")
            .Which.Message.Should().Contain("nothing to shuffle");
    }

    [TestMethod]
    public void A_listed_order_with_nothing_to_list_is_refused()
    {
        TournamentConfigLoadResult result = Load(Listed.Replace(@"  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
    - name: Crash Zone
      centre: [1092, -91, 296]
    - name: Lost River, near the skull
      centre: [-731, -761, -272]
", string.Empty));

        result.Problems.Should().ContainSingle(p => p.Path == "zone.destinationOrder")
            .Which.Message.Should().Contain("needs destinations");
    }

    [TestMethod]
    public void An_order_that_is_neither_drawn_nor_listed_is_refused()
    {
        Load(Listed.Replace("listed", "hardest")).Problems
            .Should().ContainSingle(p => p.Path == "zone.destinationOrder")
            .Which.Message.Should().Contain("drawn").And.Contain("listed");
    }

    [TestMethod]
    public void Drawn_is_what_a_file_that_says_nothing_already_does()
    {
        TournamentConfig said = Load(WithPool.Replace("  destinationSeed: 4071\n",
            "  destinationSeed: 4071\n  destinationOrder: drawn\n")).Config!;
        TournamentConfig silent = Load(WithPool).Config!;

        Enumerable.Range(1, 6).Select(group => said.DestinationFor(group)!.Name)
            .Should().Equal(Enumerable.Range(1, 6).Select(group => silent.DestinationFor(group)!.Name));
    }

    [TestMethod]
    public void Each_group_is_squeezed_towards_a_different_place()
    {
        TournamentConfig config = Load(WithPool).Config!;

        config.DestinationFor(1)!.Name.Should().NotBe(config.DestinationFor(2)!.Name);
        config.DestinationFor(2)!.Name.Should().NotBe(config.DestinationFor(3)!.Name);
    }

    [TestMethod]
    public void The_squeeze_for_a_group_ends_where_that_group_was_drawn()
    {
        TournamentConfig config = Load(WithPool).Config!;

        for (int group = 1; group <= 3; group++)
        {
            ZoneSchedule schedule = config.SqueezeFor(group);
            schedule.FinalCollapse.Target.Centre.Should().Be(config.DestinationFor(group)!.Centre);
        }
    }

    [TestMethod]
    public void A_replayed_group_is_replayed_in_the_same_arena()
    {
        TournamentConfig config = Load(WithPool).Config!;

        MatchHost host = Host(config);
        host.Command("start", TimeSpan.Zero);
        string first = DrawnIn(host);

        host.Command("reset", TimeSpan.Zero);
        host.Command("start", TimeSpan.Zero);

        host.Audit.Records.Where(Drawn).Select(r => r.Detail).Should().AllBe(first);
    }

    [TestMethod]
    public void Starting_a_match_writes_where_it_is_going()
    {
        MatchHost host = Host(Load(WithPool).Config!);

        host.Command("start", TimeSpan.Zero);

        AuditRecord record = host.Audit.Records.Single(Drawn);
        record.Detail.Should().NotBeNullOrWhiteSpace();
        record.Amount.Should().Be(4071, "the seed rides with it so the whole order can be rebuilt");
        record.Player.Should().BeNull("it happens to the group, not to anybody in particular");
    }

    [TestMethod]
    public void A_match_that_was_never_started_claims_nothing_about_where_it_would_have_gone()
    {
        MatchHost host = Host(Load(WithPool).Config!);

        host.Command("next", TimeSpan.Zero);

        host.Audit.Records.Should().NotContain(r => Drawn(r));
    }

    [TestMethod]
    public void An_authored_config_writes_no_destination_at_all()
    {
        TournamentConfig config = Load(WithPool
            .Replace("  destinationSeed: 4071\n", string.Empty)
            .Replace(@"  destinations:
    - name: Jellyshroom Cave
      centre: [-632, -262, -93]
    - name: Crash Zone
      centre: [1092, -91, 296]
    - name: Lost River, near the skull
      centre: [-731, -761, -272]
", string.Empty)
            .Replace("    - radius: 900", "    - centre: [0, -400, 0]\n      radius: 900")
            .Replace("    - radius: 400", "    - centre: [0, -400, 0]\n      radius: 400")
            .Replace("    - radius: 120", "    - centre: [0, -400, 0]\n      radius: 120")).Config!;

        MatchHost host = Host(config);
        host.Command("start", TimeSpan.Zero);

        host.Audit.Records.Should().NotContain(r => Drawn(r));
    }

    private static bool Drawn(AuditRecord record) => record.Kind == AuditRecordKind.ZoneDestinationDrawn;

    private static string DrawnIn(MatchHost host) => host.Audit.Records.Single(Drawn).Detail!;

    private static MatchHost Host(TournamentConfig config) =>
        new MatchHost(config, TimeSpan.FromSeconds(1), () => DateTimeOffset.UnixEpoch);
    [TestMethod]
    public void A_pool_phase_that_closes_backwards_names_the_field_rather_than_throwing()
    {
        TournamentConfigLoadResult result = Load(WithPool.Replace("shrinkFor: 90s", "shrinkFor: -90s"));

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Path == "zone.phases[0].shrinkFor");
    }

}
