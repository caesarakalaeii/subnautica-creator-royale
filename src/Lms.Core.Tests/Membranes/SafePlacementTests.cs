using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>Where a player is put when the ring must relocate them: candidates are computed here and the adapter walks them until one is clear of terrain.</summary>
[TestClass]
public class SafePlacementTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -50, 0);
    private static readonly ZoneSphere Ring = new ZoneSphere(Centre, 10);
    private const double Margin = 1;

    private static WorldPoint East(double metres) => new WorldPoint(metres, -50, 0);

    private static IReadOnlyList<WorldPoint> Candidates(WorldPoint from, ZoneSphere? wall = null, double margin = Margin) =>
        SafePlacement.Inside(wall ?? Ring, from, margin);

    [TestMethod]
    public void A_player_taken_out_of_a_vehicle_lands_in_the_middle_of_the_ring()
    {
        WorldPoint atTheWall = East(9.5);

        SafePlacement.Landing(Ring, atTheWall, Margin, leftAVehicle: true)
            .First().Should().Be(Centre);
    }

    [TestMethod]
    public void A_swimmer_put_back_by_the_backstop_is_moved_as_little_as_possible()
    {
        WorldPoint justOutside = East(12);

        SafePlacement.Landing(Ring, justOutside, Margin, leftAVehicle: false)
            .First().Should().Be(SafePlacement.Inside(Ring, justOutside, Margin).First());
    }

    [TestMethod]
    public void A_landing_from_a_vehicle_still_offers_somewhere_else_when_the_middle_is_rock()
    {
        IReadOnlyList<WorldPoint> candidates = SafePlacement.Landing(Ring, East(9.5), Margin, leftAVehicle: true);

        candidates.Should().HaveCountGreaterThan(1);
        foreach (WorldPoint candidate in candidates)
        {
            Centre.DistanceTo(candidate).Should().BeLessThanOrEqualTo(Ring.Radius - Margin + 1e-9);
        }
    }

    [TestMethod]
    public void Every_candidate_is_inside_the_wall_with_the_margin_to_spare()
    {
        foreach (WorldPoint from in new[] { East(0), East(5), East(40), new WorldPoint(0, 0, 0), Centre })
        {
            foreach (WorldPoint candidate in Candidates(from))
            {
                Centre.DistanceTo(candidate).Should().BeLessThanOrEqualTo(Ring.Radius - Margin + 1e-9,
                    "a player put at {0} for a swim from {1} must not be against the wall", candidate, from);
            }
        }
    }

    [TestMethod]
    public void A_player_who_is_already_safely_inside_is_not_moved_at_all()
    {
        Candidates(East(5)).First().Should().Be(East(5));
    }

    [TestMethod]
    public void A_player_outside_is_brought_to_the_nearest_point_within_the_wall()
    {
        WorldPoint first = Candidates(East(40)).First();

        first.Should().Be(East(9), "straight back along the way they went, and a metre clear of the wall");
    }

    [TestMethod]
    public void Candidates_are_ordered_by_how_little_the_player_is_moved()
    {
        IReadOnlyList<WorldPoint> candidates = Candidates(East(40));

        candidates.Select(c => c.DistanceTo(East(40))).Should().BeInAscendingOrder();
    }

    [TestMethod]
    public void The_water_above_the_impact_is_offered_before_the_rock_below_it()
    {
        IReadOnlyList<WorldPoint> candidates = Candidates(Centre);

        int above = candidates.ToList().FindIndex(c => c.Y > Centre.Y);
        int below = candidates.ToList().FindIndex(c => c.Y < Centre.Y);

        above.Should().BeLessThan(below);
    }

    [TestMethod]
    public void The_same_question_gets_the_same_answer_every_time()
    {
        Candidates(East(40)).Should().Equal(Candidates(East(40)));
    }

    [TestMethod]
    public void Candidates_spread_out_rather_than_repeating_one_point()
    {
        Candidates(East(40)).Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void A_margin_wider_than_the_ring_leaves_nowhere_but_its_centre()
    {
        IReadOnlyList<WorldPoint> candidates = Candidates(East(3), new ZoneSphere(Centre, 1), margin: 1.5);

        candidates.Should().Equal(Centre);
    }

    [TestMethod]
    public void A_field_that_has_gone_has_nowhere_inside_it()
    {
        Candidates(East(3), new ZoneSphere(Centre, 0)).Should().BeEmpty();
    }

    [TestMethod]
    public void The_ring_closing_takes_the_candidates_in_with_it()
    {
        Membrane ring = new Membrane(Centre, 10, TimeSpan.FromSeconds(20));

        WorldPoint late = SafePlacement.Inside(ring.At(TimeSpan.FromSeconds(16)), East(40), Margin).First();

        late.X.Should().BeApproximately(ring.RadiusAt(TimeSpan.FromSeconds(16)) - Margin, 1e-9);
    }

    [TestMethod]
    public void A_margin_has_to_be_a_margin()
    {
        Action negative = () => SafePlacement.Inside(Ring, East(3), -1);

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }
}
