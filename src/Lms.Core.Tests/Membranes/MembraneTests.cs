using System;
using FluentAssertions;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>The ring as geometry: the wall position and its radius over the field's life, matching <c>StasisSphere.fieldRadius</c>.</summary>
[TestClass]
public class MembraneTests
{
    private static readonly WorldPoint Impact = new WorldPoint(120, -60, -40);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(20);

    private static Membrane Field(double radius = 10) => new Membrane(Impact, radius, Lifetime);

    [TestMethod]
    public void A_field_that_has_just_landed_is_all_but_its_full_size()
    {
        Field().RadiusAt(TimeSpan.Zero).Should().BeApproximately(9.9, 1e-9);
    }

    [TestMethod]
    public void A_field_is_at_its_widest_a_fifth_of_the_way_through_its_life()
    {
        Membrane field = Field();

        field.RadiusAt(TimeSpan.FromSeconds(3.8)).Should().BeApproximately(10, 1e-9);
        field.RadiusAt(TimeSpan.FromSeconds(3.8)).Should().BeGreaterThan(field.RadiusAt(TimeSpan.Zero));
    }

    [TestMethod]
    public void A_field_shrinks_towards_its_centre_as_it_dies()
    {
        Membrane field = Field();
        double previous = field.RadiusAt(TimeSpan.FromSeconds(4));

        for (double second = 5; second < 20; second++)
        {
            double radius = field.RadiusAt(TimeSpan.FromSeconds(second));
            radius.Should().BeLessThan(previous, "the ring closes in on whoever is caught in it");
            previous = radius;
        }
    }

    [TestMethod]
    public void A_field_is_down_to_a_fifth_of_its_size_by_the_time_it_pops()
    {
        Field().RadiusAt(Lifetime - TimeSpan.FromTicks(1)).Should().BeApproximately(1.9, 0.01);
    }

    [TestMethod]
    public void A_field_past_its_lifetime_is_gone_and_holds_nobody()
    {
        Membrane field = Field();

        field.HasGoneBy(Lifetime).Should().BeTrue();
        field.RadiusAt(Lifetime).Should().Be(0);
        field.At(Lifetime).Radius.Should().Be(0);
        field.Contains(Impact, Lifetime).Should().BeFalse("nobody is caged by a field that has ended");
    }

    [TestMethod]
    public void A_field_is_not_gone_while_it_still_has_a_wall()
    {
        Field().HasGoneBy(Lifetime - TimeSpan.FromTicks(1)).Should().BeFalse();
    }

    [TestMethod]
    public void A_field_holds_whoever_is_within_its_radius_at_that_moment()
    {
        Membrane field = Field();
        WorldPoint nineMetresOut = new WorldPoint(Impact.X + 9, Impact.Y, Impact.Z);

        field.Contains(nineMetresOut, TimeSpan.Zero).Should().BeTrue();

        field.Contains(nineMetresOut, TimeSpan.FromSeconds(16)).Should().BeFalse();
    }

    [TestMethod]
    public void The_sphere_at_an_age_is_the_zone_geometry_everything_else_already_speaks()
    {
        ZoneSphere sphere = Field().At(TimeSpan.FromSeconds(3.8));

        sphere.Centre.Should().Be(Impact);
        sphere.Radius.Should().BeApproximately(10, 1e-9);
    }

    [TestMethod]
    public void Match_time_does_not_run_backwards()
    {
        Action negative = () => Field().RadiusAt(TimeSpan.FromSeconds(-1));

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_field_with_no_radius_or_no_life_is_not_a_field()
    {
        Action noRadius = () => new Membrane(Impact, 0, Lifetime);
        Action noLife = () => new Membrane(Impact, 10, TimeSpan.Zero);

        noRadius.Should().Throw<ArgumentOutOfRangeException>();
        noLife.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_full_charge_shot_leaves_the_biggest_ring_for_the_longest()
    {
        Membrane field = Membrane.FromShot(Impact, 1, StasisFieldLimits.GameDefaults);

        field.NominalRadius.Should().Be(10);
        field.Lifetime.Should().Be(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public void A_tapped_shot_leaves_the_smallest_ring_for_the_shortest()
    {
        Membrane field = Membrane.FromShot(Impact, 0, StasisFieldLimits.GameDefaults);

        field.NominalRadius.Should().Be(1);
        field.Lifetime.Should().Be(TimeSpan.FromSeconds(4));
    }

    [TestMethod]
    public void A_half_charged_shot_lands_between_the_two()
    {
        Membrane field = Membrane.FromShot(Impact, 0.5, StasisFieldLimits.GameDefaults);

        field.NominalRadius.Should().BeApproximately(5.5, 1e-9);
        field.Lifetime.Should().Be(TimeSpan.FromSeconds(12));
    }

    [TestMethod]
    public void A_charge_outside_nought_to_one_is_clamped_rather_than_believed()
    {
        Membrane.FromShot(Impact, 4, StasisFieldLimits.GameDefaults).NominalRadius.Should().Be(10);
        Membrane.FromShot(Impact, -4, StasisFieldLimits.GameDefaults).NominalRadius.Should().Be(1);
    }

    [TestMethod]
    public void Limits_read_off_a_prefab_beat_the_defaults_compiled_in_here()
    {
        StasisFieldLimits tuned = new StasisFieldLimits(2, 30, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(45));

        Membrane field = Membrane.FromShot(Impact, 1, tuned);

        field.NominalRadius.Should().Be(30);
        field.Lifetime.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestMethod]
    public void Limits_that_run_backwards_are_refused()
    {
        Action radii = () => new StasisFieldLimits(10, 1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20));
        Action times = () => new StasisFieldLimits(1, 10, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(4));

        radii.Should().Throw<ArgumentException>();
        times.Should().Throw<ArgumentException>();
    }
}
