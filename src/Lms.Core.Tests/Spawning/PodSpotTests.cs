using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spawning;

/// <summary>Spot matching with a tolerance: a planned spot is a double, a placed pod a float, so exact equality never holds.</summary>
[TestClass]
public class PodSpotTests
{
    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    [TestMethod]
    public void A_spot_is_itself()
    {
        PodSpot.IsAt(Surface(100, 200), Surface(100, 200)).Should().BeTrue();
    }

    [TestMethod]
    public void A_spot_survives_the_trip_through_a_float()
    {
        WorldPoint planned = Surface(-352.67117404411681, -485.41019662496845);
        WorldPoint saved = Surface((float)planned.X, (float)planned.Z);

        saved.Equals(planned).Should().BeFalse("a float and a double are not the same number");
        PodSpot.IsAt(planned, saved).Should().BeTrue();
    }

    [TestMethod]
    public void Two_pods_a_metre_apart_are_in_the_same_place()
    {
        PodSpot.IsAt(Surface(0, 600), Surface(0, 599.5)).Should().BeTrue();
    }

    [TestMethod]
    public void Two_pods_ten_metres_apart_are_not()
    {
        PodSpot.IsAt(Surface(0, 600), Surface(0, 590)).Should().BeFalse();
    }

    [TestMethod]
    public void Depth_counts_as_much_as_distance_along_the_surface()
    {
        PodSpot.IsAt(new WorldPoint(0, 0, 600), new WorldPoint(0, -10, 600)).Should().BeFalse();
    }

    [TestMethod]
    public void A_spot_nobody_is_on_is_free()
    {
        List<WorldPoint> pods = new List<WorldPoint> { Surface(0, 600), Surface(571, 185) };

        PodSpot.AnyAt(pods, Surface(353, -485)).Should().BeFalse();
    }

    [TestMethod]
    public void A_spot_somebody_is_on_is_not()
    {
        List<WorldPoint> pods = new List<WorldPoint> { Surface(0, 600), Surface(571, 185) };

        PodSpot.AnyAt(pods, Surface(570.6, 185.4)).Should().BeTrue();
    }

    [TestMethod]
    public void Nothing_in_the_world_means_every_spot_is_free()
    {
        PodSpot.AnyAt(new List<WorldPoint>(), Surface(0, 600)).Should().BeFalse();
    }
}
