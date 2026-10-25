using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spawning;

/// <summary>Planning a whole group's pods at once, so the result is deterministic and the first pod is chosen for what it leaves the rest.</summary>
[TestClass]
public class SpawnLayoutTests
{
    private static readonly ZoneSphere Arena = new ZoneSphere(new WorldPoint(0, -15, 0), 1000);

    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    private static List<WorldPoint> Ring(int count, double radius)
    {
        List<WorldPoint> points = new List<WorldPoint>();

        for (int i = 0; i < count; i++)
        {
            double angle = 2 * Math.PI * i / count;
            points.Add(Surface(Math.Cos(angle) * radius, Math.Sin(angle) * radius));
        }

        return points;
    }

    private static double Worst(IReadOnlyList<WorldPoint> points)
    {
        double worst = double.PositiveInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                worst = Math.Min(worst, points[i].DistanceTo(points[j]));
            }
        }

        return worst;
    }

    [TestMethod]
    public void A_plan_for_nobody_is_empty_rather_than_an_error()
    {
        SpawnLayout plan = SpawnScatter.Plan(Ring(8, 800), new WorldPoint[0], Arena, 0, 400);

        plan.Pods.Should().BeEmpty();
        plan.Separation.Should().Be(0);
        plan.MeetsTarget.Should().BeTrue();
    }

    [TestMethod]
    public void A_plan_takes_the_opposite_ends_of_what_it_is_given()
    {
        SpawnLayout plan = SpawnScatter.Plan(Ring(8, 800), new WorldPoint[0], Arena, 2, 400);

        plan.Pods.Should().HaveCount(2);
        plan.Separation.Should().BeApproximately(1600, 1);
    }

    [TestMethod]
    public void A_plan_beats_placing_them_one_at_a_time_on_the_case_that_was_played()
    {
        List<WorldPoint> candidates = new List<WorldPoint>
        {
            Surface(0, 0), Surface(40, 0), Surface(0, 40), Surface(40, 40),
            Surface(700, 0), Surface(-700, 0), Surface(0, 700)
        };

        List<WorldPoint> greedy = new List<WorldPoint>();
        for (int i = 0; i < 3; i++)
        {
            WorldPoint? next = SpawnScatter.Next(candidates, greedy, Arena, 400);
            greedy.Add(next!.Value);
        }

        SpawnLayout plan = SpawnScatter.Plan(candidates, new WorldPoint[0], Arena, 3, 400);

        plan.Separation.Should().BeGreaterThan(Worst(greedy));
        plan.Separation.Should().BeApproximately(Worst(plan.Pods), 1e-9);
    }

    [TestMethod]
    public void The_same_arena_plans_the_same_way_twice()
    {
        List<WorldPoint> candidates = Ring(20, 900);

        SpawnLayout first = SpawnScatter.Plan(candidates, new WorldPoint[0], Arena, 5, 400);
        SpawnLayout again = SpawnScatter.Plan(candidates, new WorldPoint[0], Arena, 5, 400);

        again.Pods.Should().Equal(first.Pods);
        again.Separation.Should().Be(first.Separation);
    }

    [TestMethod]
    public void The_order_the_candidates_arrive_in_does_not_change_the_plan()
    {
        List<WorldPoint> candidates = Ring(12, 900);
        List<WorldPoint> shuffled = candidates.AsEnumerable().Reverse().ToList();

        SpawnLayout plan = SpawnScatter.Plan(candidates, new WorldPoint[0], Arena, 4, 400);
        SpawnLayout other = SpawnScatter.Plan(shuffled, new WorldPoint[0], Arena, 4, 400);

        other.Separation.Should().BeApproximately(plan.Separation, 1e-9);
    }

    [TestMethod]
    public void A_pod_already_in_the_world_is_planned_around_rather_than_ignored()
    {
        WorldPoint existing = Surface(900, 0);

        SpawnLayout plan = SpawnScatter.Plan(Ring(8, 900), new[] { existing }, Arena, 2, 400);

        plan.Pods.Should().NotContain(existing);
        foreach (WorldPoint pod in plan.Pods)
        {
            pod.DistanceTo(existing).Should().BeGreaterThan(400);
        }
    }

    [TestMethod]
    public void A_plan_only_uses_points_inside_the_arena()
    {
        List<WorldPoint> candidates = new List<WorldPoint>
        {
            Surface(100, 0), Surface(5000, 0), Surface(-100, 0)
        };

        SpawnLayout plan = SpawnScatter.Plan(candidates, new WorldPoint[0], Arena, 3, 400);

        plan.Pods.Should().HaveCount(2);
        plan.Pods.Should().NotContain(Surface(5000, 0));
    }

    [TestMethod]
    public void An_arena_with_too_few_candidates_plans_what_it_can_and_says_so()
    {
        SpawnLayout plan = SpawnScatter.Plan(
            new[] { Surface(0, 0), Surface(50, 0) }, new WorldPoint[0], Arena, 5, 400);

        plan.Pods.Should().HaveCount(2);
        plan.Separation.Should().BeApproximately(50, 1e-9);
        plan.MeetsTarget.Should().BeFalse();
    }

    [TestMethod]
    public void An_arena_with_nothing_in_it_plans_nothing()
    {
        SpawnLayout plan = SpawnScatter.Plan(
            new[] { Surface(5000, 0) }, new WorldPoint[0], Arena, 5, 400);

        plan.Pods.Should().BeEmpty();
        plan.MeetsTarget.Should().BeFalse();
    }

    [TestMethod]
    public void A_plan_that_meets_the_target_says_it_does()
    {
        SpawnLayout plan = SpawnScatter.Plan(Ring(8, 900), new WorldPoint[0], Arena, 3, 400);

        plan.MeetsTarget.Should().BeTrue();
        plan.Separation.Should().BeGreaterThan(400);
    }

    [TestMethod]
    public void One_pod_has_no_separation_to_report_and_meets_any_target()
    {
        SpawnLayout plan = SpawnScatter.Plan(Ring(8, 900), new WorldPoint[0], Arena, 1, 400);

        plan.Pods.Should().HaveCount(1);
        plan.Separation.Should().Be(0);
        plan.MeetsTarget.Should().BeTrue();
    }

    [TestMethod]
    public void A_plan_for_a_group_bigger_than_the_arena_is_still_worth_reading()
    {
        SpawnLayout plan = SpawnScatter.Plan(Ring(30, 900), new WorldPoint[0], Arena, 5, 400);

        plan.ToString().Should().Contain("5 pods");
    }

    [TestMethod]
    public void Nothing_is_not_a_candidate_list()
    {
        Action nothing = () => SpawnScatter.Plan(null!, new WorldPoint[0], Arena, 2, 400);

        nothing.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void A_negative_group_is_refused()
    {
        Action negative = () => SpawnScatter.Plan(Ring(4, 900), new WorldPoint[0], Arena, -1, 400);

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}
