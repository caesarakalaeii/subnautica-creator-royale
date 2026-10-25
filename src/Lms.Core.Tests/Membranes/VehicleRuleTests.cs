using FluentAssertions;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>The vehicle rule: a caught pilot is turned back into a player and a caught player cannot enter a vehicle.</summary>
[TestClass]
public class VehicleRuleTests
{
    [TestMethod]
    public void A_contained_player_at_a_helm_is_taken_out_of_the_seat()
    {
        VehicleOrder order = VehicleRule.For(isContained: true, PlayerRide.Piloting);

        order.AnythingToDo.Should().BeTrue();
        order.Steps.Should().Equal(VehicleStep.LeaveThePilotSeat);
    }

    [TestMethod]
    public void A_contained_passenger_is_detached_from_the_sub_they_are_riding()
    {
        VehicleOrder order = VehicleRule.For(isContained: true, PlayerRide.AboardASub);

        order.Steps.Should().Equal(VehicleStep.DetachFromTheSub);
    }

    [TestMethod]
    public void A_contained_pilot_of_a_sub_leaves_the_seat_before_being_detached()
    {
        VehicleOrder order = VehicleRule.For(isContained: true, PlayerRide.PilotingASub);

        order.Steps.Should().Equal(VehicleStep.LeaveThePilotSeat, VehicleStep.DetachFromTheSub);
    }

    [TestMethod]
    public void Nobody_is_exempt_by_virtue_of_what_they_were_sitting_in()
    {
        VehicleRule.For(isContained: true, PlayerRide.Of(isPiloting: true, isAboardASub: true))
            .Steps.Should().Equal(VehicleStep.LeaveThePilotSeat, VehicleStep.DetachFromTheSub);
    }

    [TestMethod]
    public void A_contained_player_on_foot_has_nothing_done_to_them()
    {
        VehicleOrder order = VehicleRule.For(isContained: true, PlayerRide.OnFoot);

        order.AnythingToDo.Should().BeFalse();
        order.Steps.Should().BeEmpty();
    }

    [TestMethod]
    public void A_player_the_ring_never_caught_keeps_their_vehicle()
    {
        VehicleRule.For(isContained: false, PlayerRide.PilotingASub).AnythingToDo.Should().BeFalse();
    }

    [TestMethod]
    public void A_contained_player_may_not_board_anything()
    {
        VehicleRule.MayBoard(isContained: true).Should().BeFalse();
    }

    [TestMethod]
    public void A_player_outside_the_ring_may_board_as_usual()
    {
        VehicleRule.MayBoard(isContained: false).Should().BeTrue();
    }

    [TestMethod]
    public void The_vehicle_rule_holds_on_every_frame_of_the_hold_not_just_the_catch()
    {
        ZoneSphere wall = new ZoneSphere(new WorldPoint(0, -20, 0), 10);
        ContainmentWatch watch = new ContainmentWatch(new WorldPoint(0, -20, 0));

        ContainmentAssessment caught = watch.Update(new WorldPoint(0, -20, 0), wall);
        ContainmentAssessment held = watch.Update(new WorldPoint(1, -20, 0), wall);

        caught.Verdict.Should().Be(ContainmentVerdict.Caught);
        held.Verdict.Should().Be(ContainmentVerdict.Held);

        VehicleRule.For(held.IsContained, PlayerRide.AboardASub)
            .Steps.Should().Equal(VehicleStep.DetachFromTheSub);
    }

    [TestMethod]
    public void And_stops_the_moment_the_field_ends()
    {
        ZoneSphere wall = new ZoneSphere(new WorldPoint(0, -20, 0), 10);
        ContainmentWatch watch = new ContainmentWatch(new WorldPoint(0, -20, 0));
        watch.Update(new WorldPoint(0, -20, 0), wall);

        ContainmentAssessment released = watch.Update(new WorldPoint(1, -20, 0), Gone(wall));

        released.Verdict.Should().Be(ContainmentVerdict.Released);
        VehicleRule.For(released.IsContained, PlayerRide.PilotingASub).AnythingToDo.Should().BeFalse();
    }

    private static ZoneSphere Gone(ZoneSphere wall) => new ZoneSphere(wall.Centre, 0);

    [TestMethod]
    public void What_a_player_is_riding_is_two_independent_facts()
    {
        PlayerRide.OnFoot.IsPiloting.Should().BeFalse();
        PlayerRide.OnFoot.IsAboardASub.Should().BeFalse();
        PlayerRide.Piloting.IsPiloting.Should().BeTrue();
        PlayerRide.Piloting.IsAboardASub.Should().BeFalse();
        PlayerRide.AboardASub.IsPiloting.Should().BeFalse();
        PlayerRide.AboardASub.IsAboardASub.Should().BeTrue();
        PlayerRide.PilotingASub.IsPiloting.Should().BeTrue();
        PlayerRide.PilotingASub.IsAboardASub.Should().BeTrue();
    }
}
