using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>The authored squeeze: an opening sphere and phases, each shrinking over its own window and hitting harder than the last.</summary>
[TestClass]
public class ZoneScheduleTests
{
    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);

    private static ZoneSchedule ThreePhases() => new ZoneSchedule(
        new ZoneSphere(Spawn, 1400), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(200, -150, 100), 400), TimeSpan.FromSeconds(90), 8),
            new ZonePhase(3, new ZoneSphere(new WorldPoint(250, -160, 120), 120), TimeSpan.FromSeconds(60), 20),
        });

    [TestMethod]
    public void Before_the_first_phase_the_zone_is_the_opening_sphere()
    {
        ZoneState state = ThreePhases().At(0, TimeSpan.FromMinutes(2));

        state.Sphere.Should().Be(new ZoneSphere(Spawn, 1400));
        state.PhaseNumber.Should().Be(0);
        state.IsShrinking.Should().BeFalse();
    }

    [TestMethod]
    public void The_opening_still_damages_anyone_outside_it()
    {
        ThreePhases().At(0, TimeSpan.Zero).DamagePerTick.Should().Be(1);
    }

    [TestMethod]
    public void A_phase_shrinks_from_where_the_last_one_left_off()
    {
        ZoneState state = ThreePhases().At(1, TimeSpan.FromSeconds(45));

        state.IsShrinking.Should().BeTrue();
        state.Sphere.Radius.Should().BeApproximately(1150, 1e-9);
    }

    [TestMethod]
    public void A_phase_holds_its_sphere_once_the_shrink_is_done()
    {
        ZoneState state = ThreePhases().At(1, TimeSpan.FromSeconds(200));

        state.IsShrinking.Should().BeFalse();
        state.Sphere.Should().Be(new ZoneSphere(Spawn, 900));
    }

    [TestMethod]
    public void The_centre_moves_across_the_shrink_not_at_the_start_of_it()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.At(2, TimeSpan.Zero).Sphere.Centre.Should().Be(Spawn);
        schedule.At(2, TimeSpan.FromSeconds(45)).Sphere.Centre.X.Should().BeApproximately(100, 1e-9);
        schedule.At(2, TimeSpan.FromSeconds(90)).Sphere.Centre.X.Should().BeApproximately(200, 1e-9);
    }

    [TestMethod]
    public void Damage_escalates_with_the_phase()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.At(1, TimeSpan.Zero).DamagePerTick.Should().Be(3);
        schedule.At(2, TimeSpan.Zero).DamagePerTick.Should().Be(8);
        schedule.At(3, TimeSpan.Zero).DamagePerTick.Should().Be(20);
    }

    [TestMethod]
    public void The_state_carries_the_sphere_the_next_phase_is_heading_for()
    {
        ZoneState state = ThreePhases().At(1, TimeSpan.FromSeconds(120));

        state.NextSphere.Should().Be(new ZoneSphere(new WorldPoint(200, -150, 100), 400));
    }

    [TestMethod]
    public void The_last_authored_phase_points_at_the_collapse_and_the_collapse_at_nothing()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.At(3, TimeSpan.FromSeconds(120)).NextSphere!.Value.Radius.Should().Be(0);
        schedule.At(4, TimeSpan.FromSeconds(120)).NextSphere.Should().BeNull();
    }

    [TestMethod]
    public void A_phase_count_beyond_the_schedule_holds_the_closed_zone()
    {
        ZoneState state = ThreePhases().At(9, TimeSpan.FromMinutes(5));

        state.Sphere.Radius.Should().Be(0);
        state.DamagePerTick.Should().Be(20);
    }

    [TestMethod]
    public void A_phase_that_shrinks_for_no_time_snaps_to_its_sphere()
    {
        ZoneSchedule instant = new ZoneSchedule(
            new ZoneSphere(Spawn, 1000), 1,
            new[] { new ZonePhase(1, new ZoneSphere(Spawn, 500), TimeSpan.Zero, 5) });

        instant.At(1, TimeSpan.Zero).Sphere.Radius.Should().Be(500);
    }

    [TestMethod]
    public void A_schedule_needs_at_least_one_phase()
    {
        FluentActions.Invoking(() => new ZoneSchedule(new ZoneSphere(Spawn, 1000), 1, new ZonePhase[0]))
            .Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void The_squeeze_ends_with_a_collapse_the_file_did_not_write()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.AuthoredPhaseCount.Should().Be(3);
        schedule.PhaseCount.Should().Be(4, "the collapse is a phase of the match, just not of the file");
        schedule.Phases[3].IsFinalCollapse.Should().BeTrue();
        schedule.Phases[3].Number.Should().Be(4);
    }

    [TestMethod]
    public void The_collapse_closes_the_last_sphere_onto_its_own_centre()
    {
        ZoneSchedule schedule = ThreePhases();
        WorldPoint lastCentre = new WorldPoint(250, -160, 120);

        ZoneState half = schedule.At(4, TimeSpan.FromSeconds(30));

        half.Sphere.Centre.Should().Be(lastCentre, "the zone closes where it already is, it does not walk somewhere new");
        half.Sphere.Radius.Should().BeApproximately(60, 1e-9);
        half.IsShrinking.Should().BeTrue();
    }

    [TestMethod]
    public void The_collapse_takes_as_long_as_the_phase_before_it()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.Phases[3].ShrinkFor.Should().Be(schedule.Phases[2].ShrinkFor);
        schedule.At(4, TimeSpan.FromSeconds(60)).Sphere.Radius.Should().Be(0);
    }

    [TestMethod]
    public void The_collapse_bites_at_the_hardest_rate_the_file_authored()
    {
        ZoneSchedule schedule = ThreePhases();

        schedule.Phases[3].DamagePerTick.Should().Be(20);
        schedule.At(4, TimeSpan.FromSeconds(90)).DamagePerTick.Should().Be(20);
    }

    [TestMethod]
    public void A_closed_zone_has_nobody_inside_it()
    {
        ZoneState closed = ThreePhases().At(4, TimeSpan.FromMinutes(5));

        closed.Sphere.Contains(new WorldPoint(250, -160, 120)).Should().BeFalse();
    }

    [TestMethod]
    public void Even_a_one_phase_squeeze_collapses()
    {
        ZoneSchedule one = new ZoneSchedule(
            new ZoneSphere(Spawn, 1000), 1,
            new[] { new ZonePhase(1, new ZoneSphere(Spawn, 500), TimeSpan.FromSeconds(30), 5) });

        one.PhaseCount.Should().Be(2);
        one.At(2, TimeSpan.FromSeconds(30)).Sphere.Radius.Should().Be(0);
    }
}
