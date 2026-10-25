using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spawning;

/// <summary>Circle spawn layout: every player equally far from the centre and from both neighbours, on any world.</summary>
[TestClass]
public class SpawnCircleTests
{
    private static readonly List<WorldPoint> Nothing = new List<WorldPoint>();

    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    private static double Neighbours(int count, double radius) =>
        2 * radius * Math.Sin(Math.PI / count);

    [TestMethod]
    public void Every_pod_sits_on_the_circumference()
    {
        SpawnLayout planned = SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400);

        planned.Pods.Should().HaveCount(5);

        foreach (WorldPoint pod in planned.Pods)
        {
            pod.DistanceTo(Surface(0, 0)).Should().BeApproximately(600, 1e-9);
        }
    }

    [TestMethod]
    public void The_circle_lies_on_the_surface()
    {
        SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400)
            .Pods.Should().OnlyContain(pod => pod.Y == 0);
    }

    [TestMethod]
    public void The_pods_are_evenly_spaced_around_it()
    {
        SpawnLayout planned = SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400);

        planned.Separation.Should().BeApproximately(Neighbours(5, 600), 1e-6);
        planned.Separation.Should().BeApproximately(705.3, 0.1);
    }

    [TestMethod]
    public void The_first_spot_is_due_north_of_the_origin()
    {
        SpawnCircle.Plan(600, Nothing, wanted: 4, minSeparation: 400)
            .Pods[0].Should().Be(Surface(0, 600));
    }

    [TestMethod]
    public void The_spots_run_clockwise_from_there()
    {
        IReadOnlyList<WorldPoint> pods = SpawnCircle.Plan(600, Nothing, wanted: 4, minSeparation: 400).Pods;

        pods.Should().Contain(pod => Math.Abs(pod.X - 600) < 1e-6 && Math.Abs(pod.Z) < 1e-6);
    }

    [TestMethod]
    public void A_group_that_turns_up_short_gets_opposite_sides_of_the_circle()
    {
        IReadOnlyList<WorldPoint> pods = SpawnCircle.Plan(600, Nothing, wanted: 4, minSeparation: 400).Pods;

        pods[0].DistanceTo(pods[1]).Should().BeApproximately(1200, 1e-6);
    }

    [TestMethod]
    public void An_odd_circle_gives_a_pair_the_longest_chord_it_has()
    {
        IReadOnlyList<WorldPoint> pods = SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400).Pods;

        pods[0].DistanceTo(pods[1]).Should().BeApproximately(2 * 600 * Math.Sin(2 * Math.PI / 5), 1e-6);
    }

    [TestMethod]
    public void No_prefix_of_the_plan_is_more_crowded_than_the_whole_circle()
    {
        IReadOnlyList<WorldPoint> pods = SpawnCircle.Plan(600, Nothing, wanted: 6, minSeparation: 400).Pods;

        for (int count = 2; count <= pods.Count; count++)
        {
            double worst = double.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    worst = Math.Min(worst, pods[i].DistanceTo(pods[j]));
                }
            }

            worst.Should().BeGreaterThanOrEqualTo(Neighbours(6, 600) - 1e-6,
                $"the first {count} of the plan should not be tighter than all six");
        }
    }

    [TestMethod]
    public void The_same_circle_is_planned_every_time()
    {
        SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400).Pods
            .Should().Equal(SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400).Pods);
    }

    [TestMethod]
    public void A_circle_too_tight_for_the_target_says_so_and_still_places_everybody()
    {
        SpawnLayout planned = SpawnCircle.Plan(300, Nothing, wanted: 5, minSeparation: 400);

        planned.Pods.Should().HaveCount(5);
        planned.MeetsTarget.Should().BeFalse();
        planned.Target.Should().Be(400);
    }

    [TestMethod]
    public void A_circle_wide_enough_for_the_target_meets_it()
    {
        SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400)
            .MeetsTarget.Should().BeTrue();
    }

    [TestMethod]
    public void A_pod_the_world_already_has_counts_towards_the_separation()
    {
        List<WorldPoint> taken = new List<WorldPoint> { Surface(0, 590) };

        SpawnCircle.Plan(600, taken, wanted: 5, minSeparation: 400)
            .Separation.Should().BeApproximately(10, 1e-6);
    }

    [TestMethod]
    public void A_pod_already_standing_on_a_circle_spot_is_that_spot()
    {
        List<WorldPoint> circle = new List<WorldPoint>(
            SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400).Pods);

        SpawnCircle.Plan(600, circle, wanted: 5, minSeparation: 400)
            .Separation.Should().BeApproximately(Neighbours(5, 600), 1e-6);
    }

    [TestMethod]
    public void A_pod_the_save_rounded_to_a_float_is_still_on_its_spot()
    {
        List<WorldPoint> rounded = new List<WorldPoint>();
        foreach (WorldPoint pod in SpawnCircle.Plan(600, Nothing, wanted: 5, minSeparation: 400).Pods)
        {
            rounded.Add(new WorldPoint((float)pod.X, (float)pod.Y, (float)pod.Z));
        }

        SpawnCircle.Plan(600, rounded, wanted: 5, minSeparation: 400)
            .Separation.Should().BeApproximately(Neighbours(5, 600), 1e-6);
    }

    [TestMethod]
    public void Nobody_to_place_is_no_circle_at_all()
    {
        SpawnLayout planned = SpawnCircle.Plan(600, Nothing, wanted: 0, minSeparation: 400);

        planned.Pods.Should().BeEmpty();
        planned.Separation.Should().Be(0);
    }

    [TestMethod]
    public void One_pod_on_a_circle_is_still_on_the_circumference()
    {
        SpawnLayout planned = SpawnCircle.Plan(600, Nothing, wanted: 1, minSeparation: 400);

        planned.Pods.Should().Equal(Surface(0, 600));
        planned.Separation.Should().Be(0);
        planned.MeetsTarget.Should().BeTrue();
    }

    [TestMethod]
    public void A_circle_has_to_have_a_radius()
    {
        Action noRadius = () => SpawnCircle.Plan(0, Nothing, wanted: 5, minSeparation: 400);
        Action inwards = () => SpawnCircle.Plan(-600, Nothing, wanted: 5, minSeparation: 400);

        noRadius.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("radius");
        inwards.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("radius");
    }

    [TestMethod]
    public void A_group_cannot_be_a_negative_number_of_people()
    {
        Action fewerThanNobody = () => SpawnCircle.Plan(600, Nothing, wanted: -1, minSeparation: 400);

        fewerThanNobody.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("wanted");
    }

    [TestMethod]
    public void A_circle_can_hold_the_whole_field()
    {
        SpawnLayout planned = SpawnCircle.Plan(1400, Nothing, wanted: 25, minSeparation: 300);

        planned.Pods.Should().HaveCount(25);
        planned.Pods.Distinct().Should().HaveCount(25);
        planned.Separation.Should().BeApproximately(Neighbours(25, 1400), 1e-6);
    }
}
