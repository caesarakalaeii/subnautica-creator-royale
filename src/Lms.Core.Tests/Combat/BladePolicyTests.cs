using System;
using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>The operator-tunable knife and heat blade settings.</summary>
[TestClass]
public class BladePolicyTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [TestMethod]
    public void The_default_blade_hurts_nobody()
    {
        BladePolicy.Default.HurtsPlayers.Should().BeFalse();
    }

    [TestMethod]
    public void The_defaults_are_the_documented_numbers()
    {
        BladePolicy policy = BladePolicy.Default;

        policy.KnifeDamage.Should().Be(12, "what Nitrox's own halving has already been playing at");
        policy.HeatBladeDamage.Should().Be(20, "a claw's number, so a caster can say the comparison");
        policy.Reach.Should().Be(6, "the blade's 2 m plus the geometry of a hit both machines agree about");
        policy.MinInterval.Should().Be(Interval,
            "half the game's own 0.5 s tool gate, so the other half absorbs a retransmit that would " +
            "otherwise shrink the measured gap and refuse a stab that landed");
    }

    [TestMethod]
    public void A_blade_is_switched_off_by_giving_it_no_damage()
    {
        BladePolicy policy = new BladePolicy(true, knifeDamage: 0, heatBladeDamage: 20, reach: 6,
            minInterval: Interval);

        policy.Damage(StrikeWeapon.Knife).Should().Be(0);
        policy.Damage(StrikeWeapon.HeatBlade).Should().Be(20, "switching one off leaves the other alone");
    }

    [TestMethod]
    public void A_negative_damage_is_refused_by_the_name_an_operator_has_to_change()
    {
        Action heals = () => new BladePolicy(true, knifeDamage: 12, heatBladeDamage: -1, reach: 6,
            minInterval: Interval);

        heals.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("heatBladeDamage");
    }

    [TestMethod]
    public void A_damage_that_is_not_a_number_is_refused()
    {
        Action nonsense = () => new BladePolicy(true, knifeDamage: double.NaN, heatBladeDamage: 20,
            reach: 6, minInterval: Interval);

        nonsense.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("knifeDamage");
    }

    [TestMethod]
    public void A_one_shot_blade_is_allowed()
    {
        new BladePolicy(true, knifeDamage: 250, heatBladeDamage: 250, reach: 6, minInterval: Interval)
            .KnifeDamage.Should().Be(250);
    }

    [TestMethod]
    public void A_reach_shorter_than_the_geometry_of_an_agreed_hit_is_refused()
    {
        Action tooTight = () => new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20, reach: 3,
            minInterval: Interval);

        tooTight.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("reach");
    }

    [TestMethod]
    public void A_reach_of_exactly_the_floor_is_refused_too()
    {
        Action exact = () => new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20,
            reach: BladePolicy.GameBladeReach + BladePolicy.BodyAllowance, minInterval: Interval);

        exact.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("reach");
    }

    [TestMethod]
    public void A_reach_just_past_the_floor_is_allowed()
    {
        new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20, reach: 4.81, minInterval: Interval)
            .Reach.Should().Be(4.81);
    }

    [TestMethod]
    public void An_interval_of_nothing_is_refused()
    {
        Action instant = () => new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20, reach: 6,
            minInterval: TimeSpan.Zero);

        instant.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("minInterval");
    }

    [TestMethod]
    public void An_interval_that_runs_backwards_is_refused()
    {
        Action backwards = () => new BladePolicy(true, knifeDamage: 12, heatBladeDamage: 20, reach: 6,
            minInterval: TimeSpan.FromSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("minInterval");
    }

    [TestMethod]
    public void Being_in_anything_takes_a_blade_out_of_your_hands()
    {
        BladePolicy.Default.AVehicleDisarms.Should().BeTrue();
        PrawnPolicy.Default.AVehicleDisarms.Should().BeFalse();
    }

    [TestMethod]
    public void A_blade_policy_asked_for_a_prawn_s_arm_throws()
    {
        Action claw = () => BladePolicy.Default.Damage(StrikeWeapon.Claw);
        Action drill = () => BladePolicy.Default.Damage(StrikeWeapon.Drill);

        claw.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("weapon");
        drill.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("weapon");
    }

    [TestMethod]
    public void A_prawn_policy_asked_for_a_blade_throws_the_same_way()
    {
        Action knife = () => PrawnPolicy.Default.Damage(StrikeWeapon.Knife);
        Action heatBlade = () => PrawnPolicy.Default.Damage(StrikeWeapon.HeatBlade);

        knife.Should().Throw<ArgumentOutOfRangeException>();
        heatBlade.Should().Throw<ArgumentOutOfRangeException>();
    }
}
