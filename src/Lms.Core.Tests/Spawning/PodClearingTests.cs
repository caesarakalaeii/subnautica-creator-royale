using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spawning;

/// <summary>Which escape pods are derelict: a pod that is assigned, occupied or holds items is not.</summary>
[TestClass]
public class PodClearingTests
{
    private static readonly List<WorldPoint> Nobody = new List<WorldPoint>();

    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    private static IReadOnlyList<WorldPoint> Circle(double radius) =>
        SpawnCircle.Plan(radius, new List<WorldPoint>(), 5, minSeparation: 400).Pods;

    private static PodInTheWorld Empty(WorldPoint where) => new PodInTheWorld(where, new int[0], 0);

    [TestMethod]
    public void A_pod_on_one_of_the_plans_spots_is_the_layout_and_is_kept()
    {
        IReadOnlyList<WorldPoint> circle = Circle(600);
        List<PodInTheWorld> pods = new List<PodInTheWorld>();
        foreach (WorldPoint spot in circle)
        {
            pods.Add(Empty(spot));
        }

        PodClearingPlan plan = PodClearing.Plan(circle, pods, Nobody);

        plan.Clear.Should().BeEmpty();
        plan.Kept.Should().Be(5);
    }

    [TestMethod]
    public void A_pod_a_previous_radius_left_behind_is_cleared()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, Nobody);

        plan.Clear.Should().Equal(0);
        plan.Kept.Should().Be(0);
    }

    [TestMethod]
    public void A_pod_the_save_rounded_is_still_on_its_spot_and_is_kept()
    {
        IReadOnlyList<WorldPoint> circle = Circle(600);
        List<PodInTheWorld> pods = new List<PodInTheWorld>();
        foreach (WorldPoint spot in circle)
        {
            pods.Add(Empty(new WorldPoint((float)spot.X, (float)spot.Y, (float)spot.Z)));
        }

        PodClearing.Plan(circle, pods, Nobody).Clear.Should().BeEmpty();
    }

    [TestMethod]
    public void A_pod_somebody_is_assigned_to_is_never_cleared()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            new PodInTheWorld(Surface(0, 600), players: new[] { 1 }, stowedItems: 0)
        };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, Nobody);

        plan.Clear.Should().BeEmpty();
        plan.Kept.Should().Be(1);
    }

    [TestMethod]
    public void A_pod_somebody_is_standing_in_is_never_cleared_even_with_nobody_assigned()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };
        List<WorldPoint> players = new List<WorldPoint> { new WorldPoint(2, 2, 601) };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, players);

        plan.Clear.Should().BeEmpty();
        plan.SomebodyInside.Should().Be(1);
    }

    [TestMethod]
    public void A_player_swimming_past_is_not_standing_in_it()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };
        List<WorldPoint> players = new List<WorldPoint> { Surface(0, 560) };

        PodClearing.Plan(Circle(700), pods, players).Clear.Should().Equal(0);
    }

    [TestMethod]
    public void A_pod_with_something_stowed_in_it_is_cleared_and_counted()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            new PodInTheWorld(Surface(0, 600), players: new int[0], stowedItems: 3)
        };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, Nobody);

        plan.Clear.Should().Equal(0);
        plan.StowedItems.Should().Be(3);
    }

    [TestMethod]
    public void The_indices_are_the_callers_own_so_it_can_say_which_pod_it_meant()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            Empty(Surface(0, 700)),
            Empty(Surface(0, 600)),
            new PodInTheWorld(Surface(-73, 9), players: new[] { 1, 2 }, stowedItems: 0),
            Empty(Surface(-131, 301)),
        };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, Nobody);

        plan.Clear.Should().Equal(1, 3);
        plan.Kept.Should().Be(2);
    }

    [TestMethod]
    public void A_world_with_no_pods_in_it_is_already_clear()
    {
        PodClearingPlan plan = PodClearing.Plan(Circle(700), new List<PodInTheWorld>(), Nobody);

        plan.Clear.Should().BeEmpty();
        plan.Kept.Should().Be(0);
        plan.StowedItems.Should().Be(0);
    }

    [TestMethod]
    public void A_plan_with_no_spots_clears_nothing_rather_than_everything()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };

        PodClearingPlan plan = PodClearing.Plan(new List<WorldPoint>(), pods, Nobody);

        plan.Clear.Should().BeEmpty();
        plan.Kept.Should().Be(1);
        plan.NoLayout.Should().BeTrue();
    }

    [TestMethod]
    public void Clearing_needs_a_layout_a_world_and_a_roster()
    {
        Action noSpots = () => PodClearing.Plan(null!, new List<PodInTheWorld>(), Nobody);
        Action noPods = () => PodClearing.Plan(Circle(700), null!, Nobody);
        Action noPlayers = () => PodClearing.Plan(Circle(700), new List<PodInTheWorld>(), null!);

        noSpots.Should().Throw<ArgumentNullException>();
        noPods.Should().Throw<ArgumentNullException>();
        noPlayers.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void The_sweep_takes_out_the_layouts_own_pods_which_the_derelict_rule_keeps()
    {
        IReadOnlyList<WorldPoint> circle = Circle(600);
        List<PodInTheWorld> pods = new List<PodInTheWorld>();
        foreach (WorldPoint spot in circle)
        {
            pods.Add(new PodInTheWorld(spot, players: new[] { 1 }, stowedItems: 0));
        }

        PodClearing.Plan(circle, pods, Nobody).Clear.Should().BeEmpty();

        PodClearingPlan sweep = PodClearing.PlanAll(pods, Nobody);

        sweep.Clear.Should().Equal(0, 1, 2, 3, 4);
        sweep.Kept.Should().Be(0);
    }

    [TestMethod]
    public void The_sweep_asks_no_layout_so_it_cannot_be_starved_by_one()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            Empty(Surface(0, 600)),
            new PodInTheWorld(Surface(-73, 9), players: new[] { 1, 2 }, stowedItems: 4),
        };

        PodClearingPlan sweep = PodClearing.PlanAll(pods, Nobody);

        sweep.Clear.Should().Equal(0, 1);
        sweep.NoLayout.Should().BeFalse();
        sweep.StowedItems.Should().Be(4);
    }

    [TestMethod]
    public void The_sweep_refuses_outright_while_anybody_is_standing_in_a_pod()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            Empty(Surface(0, 600)),
            Empty(Surface(0, -600)),
        };
        List<WorldPoint> players = new List<WorldPoint> { new WorldPoint(2, 2, 601) };

        PodClearingPlan sweep = PodClearing.PlanAll(pods, players);

        sweep.Clear.Should().BeEmpty();
        sweep.Kept.Should().Be(2);
        sweep.StowedItems.Should().Be(0);
    }

    [TestMethod]
    public void The_sweep_names_who_is_inside_by_pointing_back_at_the_roster()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };
        List<WorldPoint> players = new List<WorldPoint>
        {
            Surface(0, 0),
            new WorldPoint(2, 2, 601),
            Surface(0, 560),
            Surface(1, 599),
        };

        PodClearing.PlanAll(pods, players).Occupants.Should().Equal(1, 3);
    }

    [TestMethod]
    public void The_derelict_rule_names_nobody_because_nobody_stops_it()
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(Surface(0, 600)) };
        List<WorldPoint> players = new List<WorldPoint> { new WorldPoint(2, 2, 601) };

        PodClearingPlan plan = PodClearing.Plan(Circle(700), pods, players);

        plan.SomebodyInside.Should().Be(1);
        plan.Occupants.Should().BeEmpty();
    }

    [TestMethod]
    public void A_world_with_no_pods_is_already_swept()
    {
        PodClearingPlan sweep = PodClearing.PlanAll(new List<PodInTheWorld>(), Nobody);

        sweep.Clear.Should().BeEmpty();
        sweep.Kept.Should().Be(0);
        sweep.Occupants.Should().BeEmpty();
    }

    [TestMethod]
    public void Sweeping_needs_a_world_and_a_roster()
    {
        Action noPods = () => PodClearing.PlanAll(null!, Nobody);
        Action noPlayers = () => PodClearing.PlanAll(new List<PodInTheWorld>(), null!);

        noPods.Should().Throw<ArgumentNullException>();
        noPlayers.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void After_the_seating_the_pod_the_group_came_off_is_an_ordinary_derelict()
    {
        IReadOnlyList<WorldPoint> circle = Circle(600);

        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            Empty(circle[0]),
            Empty(Surface(-320, 118)),
        };

        List<WorldPoint> group = new List<WorldPoint> { circle[0] };

        PodClearingPlan plan = PodClearing.Plan(circle, pods, group);

        plan.Clear.Should().Equal(1);
        plan.Kept.Should().Be(1);
        plan.SomebodyInside.Should().Be(0);
    }

    [TestMethod]
    public void A_contestant_the_seating_did_not_move_keeps_the_pod_they_are_still_in()
    {
        IReadOnlyList<WorldPoint> circle = Circle(600);
        WorldPoint offCircle = Surface(-320, 118);

        List<PodInTheWorld> pods = new List<PodInTheWorld> { Empty(circle[0]), Empty(offCircle) };
        List<WorldPoint> group = new List<WorldPoint> { circle[0], offCircle };

        PodClearingPlan plan = PodClearing.Plan(circle, pods, group);

        plan.Clear.Should().BeEmpty();
        plan.SomebodyInside.Should().Be(1);
    }
}
