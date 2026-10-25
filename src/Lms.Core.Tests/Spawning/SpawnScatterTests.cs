using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spawning;

/// <summary>Picking the next pod among the game's validated start points, keeping players apart.</summary>
[TestClass]
public class SpawnScatterTests
{
    private static readonly ZoneSphere Arena = new ZoneSphere(new WorldPoint(0, -15, 0), 1000);

    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    [TestMethod]
    public void The_first_pod_can_go_anywhere_inside_the_arena()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(100, 0), Surface(-200, 300) };

        WorldPoint? chosen = SpawnScatter.Next(candidates, new List<WorldPoint>(), Arena, minSeparation: 400);

        chosen.Should().NotBeNull();
        candidates.Should().Contain(chosen!.Value);
    }

    [TestMethod]
    public void The_next_pod_goes_as_far_as_it_can_from_the_ones_already_placed()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(60, 0), Surface(900, 0), Surface(200, 0) };
        List<WorldPoint> taken = new List<WorldPoint> { Surface(0, 0) };

        SpawnScatter.Next(candidates, taken, Arena, minSeparation: 400)
            .Should().Be(Surface(900, 0));
    }

    [TestMethod]
    public void A_start_point_outside_the_opening_zone_is_never_used()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(1800, 0), Surface(300, 0) };

        SpawnScatter.Next(candidates, new List<WorldPoint>(), Arena, minSeparation: 400)
            .Should().Be(Surface(300, 0));
    }

    [TestMethod]
    public void A_crowded_arena_still_places_everybody_at_the_best_spot_left()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(10, 0), Surface(50, 0) };
        List<WorldPoint> taken = new List<WorldPoint> { Surface(0, 0) };

        SpawnScatter.Next(candidates, taken, Arena, minSeparation: 400)
            .Should().Be(Surface(50, 0), "the furthest of a bad set still beats no pod");
    }

    [TestMethod]
    public void No_candidate_inside_the_arena_means_nothing_to_choose()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(5000, 0) };

        SpawnScatter.Next(candidates, new List<WorldPoint>(), Arena, minSeparation: 400)
            .Should().BeNull("the caller falls back on Nitrox's own placement rather than inventing one");
    }

    [TestMethod]
    public void An_empty_candidate_list_is_nothing_to_choose()
    {
        SpawnScatter.Next(new List<WorldPoint>(), new List<WorldPoint>(), Arena, minSeparation: 400)
            .Should().BeNull();
    }

    [TestMethod]
    public void A_spot_already_taken_is_not_offered_twice()
    {
        List<WorldPoint> candidates = new List<WorldPoint> { Surface(900, 0), Surface(300, 0) };
        List<WorldPoint> taken = new List<WorldPoint> { Surface(900, 0) };

        SpawnScatter.Next(candidates, taken, Arena, minSeparation: 400)
            .Should().Be(Surface(300, 0));
    }
}
