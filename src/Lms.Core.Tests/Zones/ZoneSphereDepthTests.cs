using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>The wall's width at the depth a player is swimming at, rather than at the equator.</summary>
[TestClass]
public class ZoneSphereDepthTests
{
    private static readonly ZoneSphere Sphere = new ZoneSphere(new WorldPoint(0, -100, 0), 400);

    [TestMethod]
    public void At_the_centre_s_own_depth_the_wall_is_the_whole_radius()
    {
        Sphere.RadiusAtHeight(-100).Should().BeApproximately(400, 1e-9);
    }

    [TestMethod]
    public void Deeper_than_the_centre_the_wall_is_tighter()
    {
        Sphere.RadiusAtHeight(-300).Should().BeApproximately(Math.Sqrt(400.0 * 400 - 200 * 200), 1e-9);
        Sphere.RadiusAtHeight(-300).Should().BeLessThan(Sphere.Radius);
    }

    [TestMethod]
    public void Above_and_below_are_the_same_width()
    {
        Sphere.RadiusAtHeight(-100 + 150).Should().BeApproximately(Sphere.RadiusAtHeight(-100 - 150), 1e-9);
    }

    [TestMethod]
    public void Exactly_a_radius_away_the_wall_is_a_point()
    {
        Sphere.RadiusAtHeight(-100 + 400).Should().BeApproximately(0, 1e-9);
        Sphere.RadiusAtHeight(-100 - 400).Should().BeApproximately(0, 1e-9);
    }

    [TestMethod]
    public void Past_the_sphere_there_is_no_inside_at_all_at_that_depth()
    {
        Sphere.RadiusAtHeight(-600).Should().Be(0);
        Sphere.RadiusAtHeight(500).Should().Be(0);
    }

    [TestMethod]
    public void A_closed_zone_is_nothing_at_every_depth()
    {
        new ZoneSphere(new WorldPoint(0, -100, 0), 0).RadiusAtHeight(-100).Should().Be(0);
    }

    [TestMethod]
    public void The_last_phase_is_where_this_stops_being_a_rounding_error()
    {
        ZoneSphere last = new ZoneSphere(new WorldPoint(750, -110, 450), 120);

        last.RadiusAtHeight(-200).Should().BeApproximately(79.37, 0.01);
        last.RadiusAtHeight(-300).Should().Be(0);
    }

    [TestMethod]
    public void The_width_at_a_depth_agrees_with_whether_a_point_is_inside()
    {
        ZoneSphere sphere = new ZoneSphere(new WorldPoint(0, -100, 0), 400);
        double width = sphere.RadiusAtHeight(-300);

        sphere.Contains(new WorldPoint(width - 0.001, -300, 0)).Should().BeTrue();
        sphere.Contains(new WorldPoint(width + 0.001, -300, 0)).Should().BeFalse();
    }
}
