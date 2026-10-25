using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The offset half of the follow.</summary>
[TestClass]
public class CasterShotTests
{
    private static readonly WorldPoint Target = new WorldPoint(100, -20, 200);

    private static double Distance(WorldPoint camera, WorldPoint target) => camera.DistanceTo(target);

    [TestMethod]
    public void Opening_a_shot_puts_the_camera_a_set_distance_out_and_above()
    {
        WorldPoint camera = CasterShot.Open(new WorldPoint(3000, 0, 200), Target, distance: 12, height: 4);

        camera.Y.Should().BeApproximately(Target.Y + 4, 0.001);

        camera.X.Should().BeApproximately(112, 0.001);
        camera.Z.Should().BeApproximately(200, 0.001);
    }

    [TestMethod]
    public void Opening_a_shot_keeps_the_bearing_the_camera_was_on()
    {
        WorldPoint camera = CasterShot.Open(new WorldPoint(100, 0, 900), Target, distance: 12, height: 4);

        camera.X.Should().BeApproximately(100, 0.001);
        camera.Z.Should().BeApproximately(212, 0.001);
    }

    [TestMethod]
    public void A_camera_standing_on_the_player_still_gets_a_shot()
    {
        WorldPoint camera = CasterShot.Open(Target, Target, distance: 12, height: 4);

        double.IsNaN(camera.X).Should().BeFalse();
        double.IsNaN(camera.Z).Should().BeFalse();

        camera.Y.Should().BeApproximately(Target.Y + 4, 0.001);
        Distance(camera, Target).Should().BeApproximately(System.Math.Sqrt(12 * 12 + 4 * 4), 0.001);
    }

    [TestMethod]
    public void A_camera_directly_overhead_still_gets_a_bearing()
    {
        WorldPoint camera = CasterShot.Open(new WorldPoint(100, 500, 200), Target, distance: 12, height: 4);

        double.IsNaN(camera.X).Should().BeFalse();
        Distance(camera, Target).Should().BeApproximately(System.Math.Sqrt(12 * 12 + 4 * 4), 0.001);
    }

    [TestMethod]
    public void A_shot_the_caster_has_framed_is_left_alone()
    {
        WorldPoint framed = new WorldPoint(108, -14, 204);

        CasterShot.Keep(framed, Target, Target, closest: 3, furthest: 40).Should().Be(framed);
    }

    [TestMethod]
    public void A_player_swimming_the_shot_rides_the_camera_so_the_distance_does_not_change()
    {
        WorldPoint framed = new WorldPoint(108, -14, 204);
        WorldPoint swum = new WorldPoint(105, -18, 197);

        WorldPoint camera = CasterShot.Keep(framed, swum, Target, closest: 3, furthest: 40);

        Distance(camera, swum).Should().BeApproximately(Distance(framed, Target), 0.001);
        (camera.X - swum.X).Should().BeApproximately(framed.X - Target.X, 0.001);
        (camera.Y - swum.Y).Should().BeApproximately(framed.Y - Target.Y, 0.001);
    }

    [TestMethod]
    public void A_caster_who_falls_behind_is_pulled_back_to_the_furthest_the_shot_allows()
    {
        WorldPoint camera = CasterShot.Keep(new WorldPoint(1100, -20, 200), Target, Target,
            closest: 3, furthest: 40);

        Distance(camera, Target).Should().BeApproximately(40, 0.001);

        camera.X.Should().BeApproximately(140, 0.001);
        camera.Z.Should().BeApproximately(200, 0.001);
    }

    [TestMethod]
    public void A_player_who_swims_into_the_camera_pushes_it_out_rather_than_through()
    {
        WorldPoint camera = CasterShot.Keep(new WorldPoint(100.5, -20, 200), Target, Target,
            closest: 3, furthest: 40);

        Distance(camera, Target).Should().BeApproximately(3, 0.001);
    }

    [TestMethod]
    public void A_camera_exactly_on_the_player_is_pushed_somewhere_real()
    {
        WorldPoint camera = CasterShot.Keep(Target, Target, Target, closest: 3, furthest: 40);

        double.IsNaN(camera.X).Should().BeFalse();
        Distance(camera, Target).Should().BeApproximately(3, 0.001);
    }

    [TestMethod]
    public void A_band_that_makes_no_sense_falls_back_rather_than_stranding_the_camera()
    {
        WorldPoint camera = CasterShot.Keep(new WorldPoint(1100, -20, 200), Target, Target,
            closest: 90, furthest: 3);

        Distance(camera, Target).Should().BeApproximately(CasterShot.FurthestKeep, 0.001);

        WorldPoint negative = CasterShot.Open(new WorldPoint(1100, -20, 200), Target,
            distance: -5, height: 4);

        Distance(negative, Target).Should().BeGreaterThan(0);
    }
}
