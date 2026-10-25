using System;
using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>The operator-tunable prawn suit arm settings.</summary>
[TestClass]
public class PrawnPolicyTests
{
    [TestMethod]
    public void The_default_prawn_hurts_nobody()
    {
        PrawnPolicy.Default.HurtsPlayers.Should().BeFalse();
    }

    [TestMethod]
    public void The_defaults_are_the_documented_numbers()
    {
        PrawnPolicy policy = PrawnPolicy.Default;

        policy.ClawDamage.Should().Be(20, "well under the game's own 50, which two-shots a player");
        policy.DrillDamage.Should().Be(0, "its tick rate has never been measured");
        policy.Reach.Should().Be(9, "the claw's 6.5 m plus an allowance for two machines disagreeing");
        policy.MinInterval.Should().Be(TimeSpan.FromMilliseconds(800));
    }

    [TestMethod]
    public void An_arm_is_switched_off_by_giving_it_no_damage()
    {
        PrawnPolicy policy = new PrawnPolicy(true, clawDamage: 20, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.FromMilliseconds(800));

        policy.Damage(StrikeWeapon.Claw).Should().Be(20);
        policy.Damage(StrikeWeapon.Drill).Should().Be(0);
    }

    [TestMethod]
    public void A_negative_damage_is_refused()
    {
        Action heals = () => new PrawnPolicy(true, clawDamage: -1, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.FromMilliseconds(800));

        heals.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("clawDamage");
    }

    [TestMethod]
    public void A_one_shot_claw_is_allowed()
    {
        new PrawnPolicy(true, clawDamage: 250, drillDamage: 0, reach: 9,
                minInterval: TimeSpan.FromMilliseconds(800))
            .ClawDamage.Should().Be(250);
    }

    [TestMethod]
    public void A_reach_shorter_than_the_game_s_own_arm_is_refused()
    {
        Action tooTight = () => new PrawnPolicy(true, clawDamage: 20, drillDamage: 0,
            reach: 3, minInterval: TimeSpan.FromMilliseconds(800));

        tooTight.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("reach");
    }

    [TestMethod]
    public void A_reach_of_exactly_the_game_s_own_arm_is_refused_too()
    {
        Action exact = () => new PrawnPolicy(true, clawDamage: 20, drillDamage: 0,
            reach: PrawnPolicy.GameClawReach, minInterval: TimeSpan.FromMilliseconds(800));

        exact.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void An_interval_of_nothing_is_refused()
    {
        Action instant = () => new PrawnPolicy(true, clawDamage: 20, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.Zero);

        instant.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("minInterval");
    }

    [TestMethod]
    public void A_damage_that_is_not_a_number_is_refused()
    {
        Action nonsense = () => new PrawnPolicy(true, clawDamage: double.NaN, drillDamage: 0,
            reach: 9, minInterval: TimeSpan.FromMilliseconds(800));

        nonsense.Should().Throw<ArgumentOutOfRangeException>();
    }
}
