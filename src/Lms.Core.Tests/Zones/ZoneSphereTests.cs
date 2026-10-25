using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>The zone sphere: a point, a radius, and whether a position is inside it.</summary>
[TestClass]
public class ZoneSphereTests
{
    private static readonly WorldPoint Origin = new WorldPoint(0, -100, 0);

    [TestMethod]
    public void A_point_at_the_centre_is_inside()
    {
        new ZoneSphere(Origin, 500).Contains(Origin).Should().BeTrue();
    }

    [TestMethod]
    public void A_point_beyond_the_radius_is_outside()
    {
        ZoneSphere sphere = new ZoneSphere(Origin, 500);

        sphere.Contains(new WorldPoint(501, -100, 0)).Should().BeFalse();
        sphere.DistanceOutside(new WorldPoint(600, -100, 0)).Should().BeApproximately(100, 1e-9);
    }

    [TestMethod]
    public void A_point_exactly_on_the_surface_counts_as_inside()
    {
        new ZoneSphere(Origin, 500).Contains(new WorldPoint(500, -100, 0)).Should().BeTrue();
    }

    [TestMethod]
    public void Depth_counts_the_same_as_horizontal_distance()
    {
        ZoneSphere sphere = new ZoneSphere(Origin, 300);

        sphere.Contains(new WorldPoint(0, -500, 0)).Should().BeFalse();
        sphere.DistanceOutside(new WorldPoint(0, -500, 0)).Should().BeApproximately(100, 1e-9);
    }

    [TestMethod]
    public void A_point_inside_is_no_distance_outside()
    {
        new ZoneSphere(Origin, 500).DistanceOutside(new WorldPoint(100, -100, 100)).Should().Be(0);
    }

    [TestMethod]
    public void A_sphere_partway_through_a_shrink_is_between_the_two()
    {
        ZoneSphere from = new ZoneSphere(new WorldPoint(0, 0, 0), 1000);
        ZoneSphere to = new ZoneSphere(new WorldPoint(200, -100, 0), 400);

        ZoneSphere half = ZoneSphere.Between(from, to, 0.5);

        half.Radius.Should().BeApproximately(700, 1e-9);
        half.Centre.X.Should().BeApproximately(100, 1e-9);
        half.Centre.Y.Should().BeApproximately(-50, 1e-9);
    }

    [TestMethod]
    public void A_shrink_that_has_not_started_is_the_sphere_it_started_from()
    {
        ZoneSphere from = new ZoneSphere(new WorldPoint(0, 0, 0), 1000);
        ZoneSphere to = new ZoneSphere(new WorldPoint(200, -100, 0), 400);

        ZoneSphere.Between(from, to, 0).Should().Be(from);
        ZoneSphere.Between(from, to, 1).Should().Be(to);
    }

    [TestMethod]
    public void A_sphere_is_contained_in_another_when_it_fits_entirely_inside_it()
    {
        ZoneSphere outer = new ZoneSphere(new WorldPoint(0, 0, 0), 1000);

        outer.Contains(new ZoneSphere(new WorldPoint(500, 0, 0), 500)).Should().BeTrue();
        outer.Contains(new ZoneSphere(new WorldPoint(600, 0, 0), 500)).Should().BeFalse();
    }

    [TestMethod]
    public void A_sphere_of_nothing_is_a_closed_zone_rather_than_an_error()
    {
        FluentActions.Invoking(() => new ZoneSphere(Origin, 0)).Should().NotThrow();
    }

    [TestMethod]
    public void A_sphere_can_close_to_nothing()
    {
        ZoneSphere closed = new ZoneSphere(Origin, 0);

        closed.Radius.Should().Be(0);
    }

    [TestMethod]
    public void A_closed_zone_holds_nobody_at_all()
    {
        ZoneSphere closed = new ZoneSphere(Origin, 0);

        closed.Contains(Origin).Should().BeFalse();
        closed.DistanceOutside(new WorldPoint(0, -110, 0)).Should().BeApproximately(10, 1e-9);
    }

    [TestMethod]
    public void A_negative_radius_is_still_refused()
    {
        FluentActions.Invoking(() => new ZoneSphere(Origin, -1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_shrink_towards_nothing_ends_at_nothing()
    {
        ZoneSphere closing = ZoneSphere.Between(new ZoneSphere(Origin, 120), new ZoneSphere(Origin, 0), 0.5);

        closing.Radius.Should().BeApproximately(60, 1e-9);
        ZoneSphere.Between(new ZoneSphere(Origin, 120), new ZoneSphere(Origin, 0), 1).Radius.Should().Be(0);
    }
}
