using System;
using FluentAssertions;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>The one-way rule: anyone may swim in, nobody swims out until the field is gone.</summary>
[TestClass]
public class ContainmentWatchTests
{
    private static readonly WorldPoint Impact = new WorldPoint(0, -50, 0);
    private static readonly ZoneSphere Wall = new ZoneSphere(Impact, 10);
    private static readonly ZoneSphere Gone = new ZoneSphere(Impact, 0);

    private static WorldPoint East(double metres) => new WorldPoint(metres, -50, 0);

    [TestMethod]
    public void A_player_who_never_reaches_the_field_is_left_alone()
    {
        ContainmentWatch watch = new ContainmentWatch(East(40));

        ContainmentAssessment assessment = watch.Update(East(30), Wall);

        assessment.Verdict.Should().Be(ContainmentVerdict.Clear);
        assessment.IsContained.Should().BeFalse();
        watch.IsContained.Should().BeFalse();
    }

    [TestMethod]
    public void A_player_who_swims_in_is_caught_on_the_frame_they_cross()
    {
        ContainmentWatch watch = new ContainmentWatch(East(12));

        ContainmentAssessment assessment = watch.Update(East(8), Wall);

        assessment.Verdict.Should().Be(ContainmentVerdict.Caught);
        assessment.IsContained.Should().BeTrue();
        assessment.Crossing.Entry.Should().NotBeNull("the adapter dismounts and switches the collider on here");
    }

    [TestMethod]
    public void The_shooter_standing_in_their_own_field_is_caught_by_it_too()
    {
        ContainmentWatch watch = new ContainmentWatch(Impact);

        watch.Update(Impact, Wall).Verdict.Should().Be(ContainmentVerdict.Caught);
    }

    [TestMethod]
    public void A_caught_player_who_stays_inside_is_simply_held()
    {
        ContainmentWatch watch = new ContainmentWatch(East(12));
        watch.Update(East(8), Wall);

        ContainmentAssessment assessment = watch.Update(East(6), Wall);

        assessment.Verdict.Should().Be(ContainmentVerdict.Held);
        assessment.IsContained.Should().BeTrue();
    }

    [TestMethod]
    public void A_seaglide_through_the_whole_field_is_caught_on_that_frame_and_out_of_bounds_on_the_next()
    {
        ContainmentWatch watch = new ContainmentWatch(East(-40));

        ContainmentAssessment through = watch.Update(East(40), Wall);
        through.Verdict.Should().Be(ContainmentVerdict.Caught);
        through.Crossing.Kind.Should().Be(MembraneCrossingKind.PassedThrough);

        ContainmentAssessment after = watch.Update(East(45), Wall);
        after.Verdict.Should().Be(ContainmentVerdict.Escaped);
        after.IsContained.Should().BeTrue("the ring does not let go of somebody who was in it");
    }

    [TestMethod]
    public void A_contained_player_who_gets_out_anyway_is_reported_for_recovery()
    {
        ContainmentWatch watch = new ContainmentWatch(East(2));
        watch.Update(East(2), Wall);

        ContainmentAssessment assessment = watch.Update(East(30), Wall);

        assessment.Verdict.Should().Be(ContainmentVerdict.Escaped);
        assessment.IsContained.Should().BeTrue();
        assessment.Crossing.Exit.Should().NotBeNull("that is where they went through the wall");
        assessment.Crossing.WentThroughTheWall.Should().BeTrue("this is the half of Escaped worth a warning");
    }

    [TestMethod]
    public void A_player_the_closing_wall_leaves_behind_is_still_contained()
    {
        Membrane ring = new Membrane(Impact, 10, TimeSpan.FromSeconds(20));
        ContainmentWatch watch = new ContainmentWatch(East(9));

        watch.Update(East(9), ring.At(TimeSpan.Zero)).Verdict.Should().Be(ContainmentVerdict.Caught);

        ContainmentAssessment assessment = watch.Update(East(9), ring.At(TimeSpan.FromSeconds(16)));

        assessment.Verdict.Should().Be(ContainmentVerdict.Escaped);
        assessment.IsContained.Should().BeTrue();
        assessment.Crossing.WentThroughTheWall.Should()
            .BeFalse("nobody got out: this is the ring closing past them, and it is not a defect");
    }

    [TestMethod]
    public void Containment_ends_when_the_field_does()
    {
        ContainmentWatch watch = new ContainmentWatch(Impact);
        watch.Update(Impact, Wall);

        ContainmentAssessment released = watch.Update(Impact, Gone);

        released.Verdict.Should().Be(ContainmentVerdict.Released);
        released.IsContained.Should().BeFalse();
        watch.IsContained.Should().BeFalse();
        watch.IsFinished.Should().BeTrue();
    }

    [TestMethod]
    public void A_released_player_is_not_released_twice()
    {
        ContainmentWatch watch = new ContainmentWatch(Impact);
        watch.Update(Impact, Wall);
        watch.Update(Impact, Gone);

        watch.Update(Impact, Gone).Verdict.Should().Be(ContainmentVerdict.Clear);
    }

    [TestMethod]
    public void A_player_the_field_never_caught_has_nothing_to_be_released_from()
    {
        ContainmentWatch watch = new ContainmentWatch(East(40));

        ContainmentAssessment assessment = watch.Update(East(40), Gone);

        assessment.Verdict.Should().Be(ContainmentVerdict.Clear);
        watch.IsFinished.Should().BeTrue("the watch can be dropped either way");
    }

    [TestMethod]
    public void A_field_that_has_gone_catches_nobody_who_swims_through_its_ghost()
    {
        ContainmentWatch watch = new ContainmentWatch(East(40));
        watch.Update(East(40), Gone);

        watch.Update(Impact, Gone).Verdict.Should().Be(ContainmentVerdict.Clear);
        watch.IsContained.Should().BeFalse();
    }

    [TestMethod]
    public void Each_player_is_watched_against_each_field_separately()
    {
        ZoneSphere elsewhere = new ZoneSphere(East(30), 10);

        ContainmentWatch caught = new ContainmentWatch(Impact);
        ContainmentWatch missed = new ContainmentWatch(Impact);

        caught.Update(Impact, Wall).IsContained.Should().BeTrue();
        missed.Update(Impact, elsewhere).IsContained.Should().BeFalse();
    }

    [TestMethod]
    public void The_wall_the_adapter_hands_in_is_the_one_that_is_enforced()
    {
        ContainmentWatch watch = new ContainmentWatch(East(15));

        watch.Update(East(12), Wall).Verdict.Should().Be(ContainmentVerdict.Clear);
        watch.Update(East(12), new ZoneSphere(Impact, 14)).Verdict.Should().Be(ContainmentVerdict.Caught);
    }
}
