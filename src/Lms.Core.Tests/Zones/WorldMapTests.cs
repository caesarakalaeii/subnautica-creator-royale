using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>The single world-to-flat-map transform shared by the HUD minimap and the booth map (south-up image).</summary>
[TestClass]
public class WorldMapTests
{
    private const double Edge = WorldMap.SpanMetres / 2;

    private static WorldPoint At(double x, double z) => new WorldPoint(x, 0, z);

    [TestMethod]
    public void The_world_origin_is_the_middle_of_the_map()
    {
        MapPoint middle = WorldMap.NorthUp(At(0, 0));

        middle.U.Should().BeApproximately(0.5, 1e-9);
        middle.V.Should().BeApproximately(0.5, 1e-9);
    }

    [TestMethod]
    public void North_is_at_the_top()
    {
        MapPoint north = WorldMap.NorthUp(At(0, 1000));
        MapPoint south = WorldMap.NorthUp(At(0, -1000));

        north.V.Should().BeLessThan(south.V);
        north.V.Should().BeApproximately(0.5 - 1000.0 / WorldMap.SpanMetres, 1e-9);
    }

    [TestMethod]
    public void East_is_on_the_right()
    {
        WorldMap.NorthUp(At(1000, 0)).U.Should().BeGreaterThan(WorldMap.NorthUp(At(-1000, 0)).U);
    }

    [TestMethod]
    public void The_four_corners_are_the_corners()
    {
        MapPoint northWest = WorldMap.NorthUp(At(-Edge, Edge));
        MapPoint southEast = WorldMap.NorthUp(At(Edge, -Edge));

        northWest.U.Should().BeApproximately(0, 1e-9);
        northWest.V.Should().BeApproximately(0, 1e-9);
        southEast.U.Should().BeApproximately(1, 1e-9);
        southEast.V.Should().BeApproximately(1, 1e-9);
    }

    [TestMethod]
    public void Somewhere_off_the_map_reads_as_off_the_map_rather_than_on_its_edge()
    {
        MapPoint far = WorldMap.NorthUp(At(Edge + 500, 0));

        far.U.Should().BeGreaterThan(1);
        far.IsOnTheMap.Should().BeFalse();
        WorldMap.NorthUp(At(0, 0)).IsOnTheMap.Should().BeTrue();
    }

    [TestMethod]
    public void Depth_is_not_part_of_it()
    {
        WorldMap.NorthUp(new WorldPoint(100, 0, 200)).Should()
            .Be(WorldMap.NorthUp(new WorldPoint(100, -1200, 200)));
    }

    [TestMethod]
    public void A_radius_is_a_fraction_of_the_span()
    {
        WorldMap.Fraction(WorldMap.SpanMetres).Should().BeApproximately(1, 1e-9);
        WorldMap.Fraction(1024).Should().BeApproximately(0.25, 1e-9);
        WorldMap.Fraction(0).Should().Be(0);
    }

    [TestMethod]
    public void The_span_is_the_game_s_own_number()
    {
        WorldMap.SpanMetres.Should().Be(4096);
    }

    [TestMethod]
    public void The_image_the_script_writes_is_upside_down_against_this()
    {
        WorldMap.ImageIsSouthUp.Should().BeTrue();
    }
}
