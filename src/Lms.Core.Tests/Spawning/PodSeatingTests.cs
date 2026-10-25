using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Spawning;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>Who goes in which pod, and which spots need a pod made on them first.</summary>
[TestClass]
public class PodSeatingTests
{
    private static readonly List<WorldPoint> Nothing = new List<WorldPoint>();

    private static readonly List<PodInTheWorld> NoPods = new List<PodInTheWorld>();

    private static PodInTheWorld Pod(WorldPoint where, params int[] owners) =>
        new PodInTheWorld(where, owners, stowedItems: 0);

    private static List<PodInTheWorld> Unowned(IEnumerable<WorldPoint> spots)
    {
        List<PodInTheWorld> pods = new List<PodInTheWorld>();
        foreach (WorldPoint spot in spots)
        {
            pods.Add(Pod(spot));
        }

        return pods;
    }

    private static WorldPoint Surface(double x, double z) => new WorldPoint(x, 0, z);

    private static IReadOnlyList<WorldPoint> Circle(int pods) =>
        SpawnCircle.Plan(600, Nothing, pods, minSeparation: 400).Pods;

    [TestMethod]
    public void Everybody_present_gets_a_spot_from_the_plan()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats.Should().HaveCount(3);
        seating.Unseated.Should().Be(0);
    }

    [TestMethod]
    public void The_spots_are_handed_out_in_the_plans_order()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);

        PodSeatingPlan seating = PodSeating.Plan(circle, NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats[0].Spot.Should().Be(circle[0]);
        seating.Seats[1].Spot.Should().Be(circle[1]);
        seating.Seats[2].Spot.Should().Be(circle[2]);
    }

    [TestMethod]
    public void A_group_bigger_than_the_plan_seats_who_fits_and_says_how_many_did_not()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy"), (4, "Dot"), (5, "Eve"), (6, "Fay"), (7, "Gil")));

        seating.Seats.Should().HaveCount(5);
        seating.Unseated.Should().Be(2);
    }

    [TestMethod]
    public void Nobody_present_is_nothing_to_do()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), NoPods, new PlayerReading[0]);

        seating.Seats.Should().BeEmpty();
        seating.Unseated.Should().Be(0);
        seating.PodsToMake.Should().Be(0);
    }

    [TestMethod]
    public void A_plan_with_no_spots_seats_nobody_rather_than_inventing_somewhere()
    {
        PodSeatingPlan seating = PodSeating.Plan(new List<WorldPoint>(), NoPods,
            Group((1, "Ada"), (2, "Bea")));

        seating.Seats.Should().BeEmpty();
        seating.Unseated.Should().Be(2);
    }

    [TestMethod]
    public void A_spot_with_no_pod_on_it_needs_one_made()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats.Should().OnlyContain(seat => seat.NeedsAPod);
        seating.PodsToMake.Should().Be(3);
    }

    [TestMethod]
    public void A_spot_that_already_has_a_pod_on_it_reuses_it()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);

        PodSeatingPlan seating = PodSeating.Plan(circle, Unowned(circle),
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats.Should().OnlyContain(seat => !seat.NeedsAPod);
        seating.PodsToMake.Should().Be(0);
    }

    [TestMethod]
    public void A_pod_the_save_rounded_is_still_on_its_spot()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);
        List<WorldPoint> rounded = new List<WorldPoint>();
        foreach (WorldPoint pod in circle)
        {
            rounded.Add(new WorldPoint((float)pod.X, (float)pod.Y, (float)pod.Z));
        }

        PodSeating.Plan(circle, Unowned(rounded), Group((1, "Ada"), (2, "Bea"), (3, "Cy")))
            .PodsToMake.Should().Be(0);
    }

    [TestMethod]
    public void A_pod_somewhere_else_entirely_is_not_one_of_the_circles()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), new List<PodInTheWorld> { Pod(Surface(-73, 9)) },
            Group((1, "Ada"), (2, "Bea")));

        seating.PodsToMake.Should().Be(2);
    }

    [TestMethod]
    public void A_contestant_whose_own_pod_is_on_the_layout_is_seated_in_it_whatever_their_name()
    {
        WorldPoint north = Surface(0, 300), west = Surface(-300, 0), east = Surface(300, 0), south = Surface(0, -300);
        const int ember = 1, lark = 2, moss = 3, nell = 4;
        List<PodInTheWorld> pods = new List<PodInTheWorld>
        {
            Pod(north, nell), Pod(west, moss), Pod(east, ember), Pod(south, lark), Pod(Surface(74, -93), 99)
        };

        PodSeatingPlan seating = PodSeating.Plan(new[] { north, west, east, south }, pods,
            Group((ember, "Ember"), (lark, "Lark"), (moss, "Moss"), (nell, "Nell")));

        seating.Seats.Should().Equal(
            new PodSeat(ember, east, 2),
            new PodSeat(lark, south, 3),
            new PodSeat(moss, west, 1),
            new PodSeat(nell, north, 0));
        seating.PodsToMake.Should().Be(0);
    }

    [TestMethod]
    public void No_two_seats_are_given_the_same_pod_even_when_pods_crowd_one_spot()
    {
        IReadOnlyList<WorldPoint> circle = Circle(3);
        WorldPoint beside = new WorldPoint(circle[0].X + 0.5, 0, circle[0].Z);
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Pod(beside), Pod(circle[0]), Pod(circle[0], 1, 2) };

        PodSeatingPlan seating = PodSeating.Plan(circle, pods, Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        List<int> reused = new List<int>();
        foreach (PodSeat seat in seating.Seats)
        {
            if (seat.Pod.HasValue)
            {
                reused.Add(seat.Pod.Value);
            }
        }

        reused.Should().OnlyHaveUniqueItems();
        seating.Seats.Should().HaveCount(3);
    }

    [TestMethod]
    public void A_pod_whose_owner_is_left_where_they_are_is_not_given_to_somebody_else()
    {
        IReadOnlyList<WorldPoint> circle = Circle(2);
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Pod(circle[1], 3) };

        PodSeatingPlan seating = PodSeating.Plan(circle, pods, Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats.Should().OnlyContain(seat => seat.NeedsAPod);
        seating.Unseated.Should().Be(1);
    }

    [TestMethod]
    public void A_pod_on_the_layout_owned_by_nobody_present_is_reused()
    {
        IReadOnlyList<WorldPoint> circle = Circle(2);
        List<PodInTheWorld> pods = new List<PodInTheWorld> { Pod(circle[0], 99) };

        PodSeatingPlan seating = PodSeating.Plan(circle, pods, Group((1, "Ada")));

        seating.Seats.Should().Equal(new PodSeat(1, circle[0], 0));
    }

    [TestMethod]
    public void A_seating_needs_somewhere_to_seat_people_and_somebody_to_seat()
    {
        Action noSpots = () => PodSeating.Plan((IReadOnlyList<WorldPoint>)null!, NoPods, Group((1, "Ada")));
        Action noPods = () => PodSeating.Plan(Circle(5), (IReadOnlyList<PodInTheWorld>)null!, Group((1, "Ada")));
        Action noPlayers = () => PodSeating.Plan(Circle(5), NoPods, (IReadOnlyList<PlayerReading>)null!);

        noSpots.Should().Throw<ArgumentNullException>();
        noPods.Should().Throw<ArgumentNullException>();
        noPlayers.Should().Throw<ArgumentNullException>();
    }

    private static PlayerReading Reading(ushort id, string name) =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 100, 45, 100, 100);

    private static IReadOnlyList<PlayerReading> Group(params (ushort id, string name)[] players)
    {
        List<PlayerReading> readings = new List<PlayerReading>(players.Length);
        foreach ((ushort id, string name) in players)
        {
            readings.Add(Reading(id, name));
        }

        return readings;
    }

    [TestMethod]
    public void A_contestants_seat_follows_their_name_whatever_the_join_order_did()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);

        PodSeatingPlan first = PodSeating.Plan(circle, NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));
        PodSeatingPlan rejoined = PodSeating.Plan(circle, NoPods,
            Group((9, "Cy"), (2, "Bea"), (11, "Ada")));

        rejoined.Seats[0].Spot.Should().Be(first.Seats[0].Spot, "Ada's seat follows her name");
        rejoined.Seats[1].Spot.Should().Be(first.Seats[1].Spot, "Bea's seat follows her name");
        rejoined.Seats[2].Spot.Should().Be(first.Seats[2].Spot, "Cy's seat follows his name");
    }

    [TestMethod]
    public void Ghosts_passed_in_are_seated_like_anybody_else_so_the_filter_is_the_callers()
    {
        PodSeatingPlan seating = PodSeating.Plan(Circle(5), NoPods,
            Group((1, "Ada"), (2, "Bea"), (3, "Cy")));

        seating.Seats.Should().HaveCount(3);
        seating.Unseated.Should().Be(0);
    }

    [TestMethod]
    public void Contestants_are_seated_in_name_order_ordinally()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);

        PodSeatingPlan seating = PodSeating.Plan(circle, NoPods,
            Group((7, "Zed"), (8, "ada"), (9, "Bea")));

        seating.Seats[0].Player.Should().Be(9);
        seating.Seats[1].Player.Should().Be(7);
        seating.Seats[2].Player.Should().Be(8);
    }

    [TestMethod]
    public void The_same_name_twice_is_broken_by_id_rather_than_thrown_away()
    {
        IReadOnlyList<WorldPoint> circle = Circle(5);

        PodSeatingPlan seating = PodSeating.Plan(circle, NoPods,
            Group((4, "Bea"), (2, "Bea"), (1, "Ada")));

        seating.Seats[0].Player.Should().Be(1);
        seating.Seats[1].Player.Should().Be(2);
        seating.Seats[2].Player.Should().Be(4);
    }

    [TestMethod]
    public void The_outcome_carries_the_names_of_who_it_seated_and_who_it_left_alone()
    {
        PodCommandOutcome outcome = new PodCommandOutcome(2, "done", "detail",
            new[] { "Ada", "Bea" }, new[] { "Zed" });

        outcome.Seated.Should().Equal("Ada", "Bea");
        outcome.Unseated.Should().Equal("Zed");
    }

    [TestMethod]
    public void An_outcome_that_names_nobody_is_one_that_seated_nobody()
    {
        PodCommandOutcome outcome = new PodCommandOutcome(2, "done");

        outcome.Seated.Should().BeEmpty();
        outcome.Unseated.Should().BeEmpty();
    }

    [TestMethod]
    public void Nothing_is_missing_when_the_seating_accounted_for_every_contestant()
    {
        PodCommandOutcome outcome = new PodCommandOutcome(3, "done", null,
            new[] { "Ada", "Bea" }, new[] { "Cy" });

        outcome.Missing(new[] { "Ada", "Bea", "Cy" }).Should().BeEmpty();
    }

    [TestMethod]
    public void A_contestant_neither_seated_nor_left_alone_is_missing()
    {
        PodCommandOutcome outcome = new PodCommandOutcome(2, "done", null,
            new[] { "Ada", "Bea" }, new[] { "Zed" });

        outcome.Missing(new[] { "Ada", "Bea", "Cy" }).Should().Equal("Cy");
    }

    [TestMethod]
    public void A_contestant_left_where_they_were_is_not_missing()
    {
        PodCommandOutcome outcome = new PodCommandOutcome(2, "done", null,
            new[] { "Ada", "Bea" }, new[] { "Cy" });

        outcome.Missing(new[] { "Cy" }).Should().BeEmpty();
    }
}
