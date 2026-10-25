using System;
using FluentAssertions;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>Whether a movement segment crosses the ring wall, and where; segments rather than endpoints so fast movement cannot tunnel through.</summary>
[TestClass]
public class MembraneCrossingTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -50, 0);
    private static readonly ZoneSphere Ring = new ZoneSphere(Centre, 10);

    private static WorldPoint East(double metres) => new WorldPoint(metres, -50, 0);

    [TestMethod]
    public void A_frame_spent_inside_the_ring_crosses_nothing()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(-3), East(4));

        crossing.Kind.Should().Be(MembraneCrossingKind.Inside);
        crossing.StartedInside.Should().BeTrue();
        crossing.EndedInside.Should().BeTrue();
        crossing.Entry.Should().BeNull();
        crossing.Exit.Should().BeNull();
    }

    [TestMethod]
    public void A_frame_spent_clear_of_the_ring_crosses_nothing()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(20), East(30));

        crossing.Kind.Should().Be(MembraneCrossingKind.Clear);
        crossing.StartedInside.Should().BeFalse();
        crossing.EndedInside.Should().BeFalse();
        crossing.Entry.Should().BeNull();
    }

    [TestMethod]
    public void A_swim_into_the_ring_reports_where_the_wall_was_crossed()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(15), East(2));

        crossing.Kind.Should().Be(MembraneCrossingKind.Entered);
        crossing.EndedInside.Should().BeTrue();
        crossing.Entry.Should().NotBeNull();
        crossing.Entry!.Value.X.Should().BeApproximately(10, 1e-9);
        crossing.Exit.Should().BeNull("nobody leaves once they are in");
    }

    [TestMethod]
    public void A_swim_out_of_the_ring_reports_where_the_wall_was_crossed()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(2), East(15));

        crossing.Kind.Should().Be(MembraneCrossingKind.Exited);
        crossing.StartedInside.Should().BeTrue();
        crossing.Exit.Should().NotBeNull();
        crossing.Exit!.Value.X.Should().BeApproximately(10, 1e-9);
        crossing.Entry.Should().BeNull();
    }

    [TestMethod]
    public void A_seaglide_that_clears_the_whole_ring_in_one_frame_is_still_caught()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(-40), East(40));

        crossing.Kind.Should().Be(MembraneCrossingKind.PassedThrough);
        crossing.StartedInside.Should().BeFalse();
        crossing.EndedInside.Should().BeFalse();
        crossing.Entry!.Value.X.Should().BeApproximately(-10, 1e-9);
        crossing.Exit!.Value.X.Should().BeApproximately(10, 1e-9);
    }

    [TestMethod]
    public void The_entry_comes_before_the_exit_whichever_way_the_player_was_going()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(40), East(-40));

        crossing.Kind.Should().Be(MembraneCrossingKind.PassedThrough);
        crossing.Entry!.Value.X.Should().BeApproximately(10, 1e-9);
        crossing.Exit!.Value.X.Should().BeApproximately(-10, 1e-9);
    }

    [TestMethod]
    public void A_frame_that_passes_close_by_without_touching_is_clear()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(
            Ring, new WorldPoint(-40, -50, 10.5), new WorldPoint(40, -50, 10.5));

        crossing.Kind.Should().Be(MembraneCrossingKind.Clear);
    }

    [TestMethod]
    public void A_frame_that_grazes_the_surface_counts_as_having_been_in_the_ring()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(
            Ring, new WorldPoint(-40, -50, 10), new WorldPoint(40, -50, 10));

        crossing.Kind.Should().Be(MembraneCrossingKind.PassedThrough);
        crossing.Entry.Should().NotBeNull();
    }

    [TestMethod]
    public void A_player_who_did_not_move_is_judged_where_they_stand()
    {
        MembraneCrossing.Through(Ring, East(3), East(3)).Kind.Should().Be(MembraneCrossingKind.Inside);
        MembraneCrossing.Through(Ring, East(30), East(30)).Kind.Should().Be(MembraneCrossingKind.Clear);
    }

    [TestMethod]
    public void A_frame_that_starts_on_the_wall_and_swims_out_has_left()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(Ring, East(10), East(15));

        crossing.Kind.Should().Be(MembraneCrossingKind.Exited);
        crossing.StartedInside.Should().BeTrue();
    }

    [TestMethod]
    public void A_ring_that_has_gone_cannot_be_crossed()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(new ZoneSphere(Centre, 0), East(-40), East(40));

        crossing.Kind.Should().Be(MembraneCrossingKind.Clear);
        crossing.Entry.Should().BeNull();
    }

    [TestMethod]
    public void The_crossing_point_lies_on_the_wall_in_three_dimensions()
    {
        MembraneCrossing crossing = MembraneCrossing.Through(
            Ring, new WorldPoint(0, -20, 0), new WorldPoint(0, -48, 0));

        crossing.Kind.Should().Be(MembraneCrossingKind.Entered);
        crossing.Entry!.Value.Y.Should().BeApproximately(-40, 1e-9);
        Centre.DistanceTo(crossing.Entry.Value).Should().BeApproximately(10, 1e-9);
    }

    [TestMethod]
    public void A_membrane_is_crossed_against_the_wall_as_it_stands_at_that_moment()
    {
        Membrane field = new Membrane(Centre, 10, TimeSpan.FromSeconds(20));

        field.Crossing(East(15), East(9.5), TimeSpan.Zero).Kind.Should().Be(MembraneCrossingKind.Entered);

        field.Crossing(East(15), East(9.5), TimeSpan.FromSeconds(16)).Kind.Should().Be(MembraneCrossingKind.Clear);
    }

    [TestMethod]
    public void Going_out_through_the_wall_is_told_apart_from_the_wall_closing_past_you()
    {
        MembraneCrossing.Through(Ring, East(2), East(15)).WentThroughTheWall
            .Should().BeTrue("they swam out of it");
        MembraneCrossing.Through(Ring, East(-40), East(40)).WentThroughTheWall
            .Should().BeTrue("a frame fast enough to cross the whole field crossed the wall twice");
        MembraneCrossing.Through(Ring, East(20), East(30)).WentThroughTheWall
            .Should().BeFalse("outside at both ends and never near it: the wall moved, not the player");
        MembraneCrossing.Through(Ring, East(15), East(2)).WentThroughTheWall
            .Should().BeFalse("a swim in is not a way out");
        MembraneCrossing.Through(Ring, East(-3), East(4)).WentThroughTheWall
            .Should().BeFalse("a frame spent inside crossed nothing");
    }
}
