using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Combat;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>Whether a reported swing landed and for how much; the order refusals are tried in is the rule, shared by both weapon families.</summary>
[TestClass]
public class StrikeRuleTests
{
    private static readonly PrawnPolicy On = new PrawnPolicy(true, clawDamage: 20, drillDamage: 4,
        reach: 9, minInterval: TimeSpan.FromMilliseconds(800));

    private static readonly BladePolicy Sharp = new BladePolicy(true, knifeDamage: 12,
        heatBladeDamage: 20, reach: 6, minInterval: TimeSpan.FromMilliseconds(400));

    private static readonly WorldPoint Attacker = new WorldPoint(0, -50, 0);

    private static readonly TimeSpan Ages = TimeSpan.MaxValue;

    private static StrikeVerdict Judge(PrawnPolicy? policy = null, StrikeWeapon arm = StrikeWeapon.Claw,
        WorldPoint? victim = null, bool attackerIsHeld = false, bool attackerIsInAPrawn = true,
        bool matchIsLive = true, bool victimIsInTheMatch = true, TimeSpan? sinceLastStrike = null,
        bool attackerIsAGhost = false, IReadOnlyList<EquipmentLine>? victimEquipment = null) =>
        StrikeRule.Judge(policy ?? On, arm, Attacker, victim ?? new WorldPoint(0, -50, 3),
            attackerIsHeld, attackerIsInAVehicle: true, attackerIsInAPrawn, matchIsLive,
            victimIsInTheMatch, sinceLastStrike ?? Ages, attackerIsAGhost, victimEquipment);

    private static StrikeVerdict Stab(BladePolicy? policy = null, StrikeWeapon blade = StrikeWeapon.Knife,
        WorldPoint? victim = null, bool attackerIsHeld = false, bool attackerIsInAVehicle = false,
        bool attackerIsHoldingIt = true, bool matchIsLive = true, bool victimIsInTheMatch = true,
        TimeSpan? sinceLastStrike = null, bool attackerIsAGhost = false,
        IReadOnlyList<EquipmentLine>? victimEquipment = null) =>
        StrikeRule.Judge(policy ?? Sharp, blade, Attacker, victim ?? new WorldPoint(0, -50, 3),
            attackerIsHeld, attackerIsInAVehicle, attackerIsHoldingIt, matchIsLive,
            victimIsInTheMatch, sinceLastStrike ?? Ages, attackerIsAGhost, victimEquipment);

    [TestMethod]
    public void A_claw_at_arm_s_length_lands_for_the_configured_damage()
    {
        StrikeVerdict verdict = Judge();

        verdict.Landed.Should().BeTrue();
        verdict.Damage.Should().Be(20, "the number is the server's, out of the config");
        verdict.Refusal.Should().Be(StrikeRefusal.None);
    }

    [TestMethod]
    public void The_arm_decides_which_number_is_charged()
    {
        Judge(arm: StrikeWeapon.Drill).Damage.Should().Be(4);
    }

    [TestMethod]
    public void A_prawn_that_does_not_hurt_players_refuses_everything()
    {
        StrikeVerdict verdict = Judge(policy: PrawnPolicy.Default);

        verdict.Landed.Should().BeFalse();
        verdict.Damage.Should().Be(0);
        verdict.Refusal.Should().Be(StrikeRefusal.PrawnsAreHarmless);
    }

    [TestMethod]
    public void An_arm_with_no_damage_is_refused_rather_than_landing_for_nothing()
    {
        PrawnPolicy noDrill = new PrawnPolicy(true, clawDamage: 20, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.FromMilliseconds(800));

        StrikeVerdict verdict = Judge(policy: noDrill, arm: StrikeWeapon.Drill);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.ArmIsDisabled);
    }

    [TestMethod]
    public void The_claw_still_works_when_only_the_drill_is_switched_off()
    {
        PrawnPolicy noDrill = new PrawnPolicy(true, clawDamage: 20, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.FromMilliseconds(800));

        Judge(policy: noDrill, arm: StrikeWeapon.Claw).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_swing_just_inside_the_reach_lands()
    {
        Judge(victim: new WorldPoint(0, -50, 8.999)).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_swing_at_exactly_the_reach_lands()
    {
        Judge(victim: new WorldPoint(0, -50, 9)).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_swing_past_the_reach_is_refused()
    {
        StrikeVerdict verdict = Judge(victim: new WorldPoint(0, -50, 9.001));

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void A_victim_across_the_map_is_refused()
    {
        Judge(victim: new WorldPoint(600, -50, -900)).Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void The_distance_is_measured_in_three_dimensions()
    {
        Judge(victim: new WorldPoint(0, -70, 0)).Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void A_position_that_is_not_a_number_is_refused_rather_than_believed()
    {
        Judge(victim: new WorldPoint(double.NaN, -50, 0)).Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void A_second_swing_too_soon_after_the_first_is_refused()
    {
        StrikeVerdict verdict = Judge(sinceLastStrike: TimeSpan.FromMilliseconds(799));

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.TooSoon);
    }

    [TestMethod]
    public void A_second_swing_exactly_one_interval_later_lands()
    {
        Judge(sinceLastStrike: TimeSpan.FromMilliseconds(800)).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_clock_that_has_gone_backwards_is_refused_rather_than_believed()
    {
        Judge(sinceLastStrike: TimeSpan.FromSeconds(-5)).Refusal.Should().Be(StrikeRefusal.TooSoon);
    }

    [TestMethod]
    public void Nobody_is_hurt_while_the_match_is_not_running()
    {
        StrikeVerdict verdict = Judge(matchIsLive: false);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.MatchIsNotRunning);
    }

    [TestMethod]
    public void A_player_the_match_is_finished_with_is_not_hurt_again()
    {
        StrikeVerdict verdict = Judge(victimIsInTheMatch: false);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.TheyAreAlreadyOut);
    }

    [TestMethod]
    public void A_held_attacker_cannot_swing()
    {
        StrikeVerdict verdict = Judge(attackerIsHeld: true);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.TheRingHasYou);
    }

    [TestMethod]
    public void Somebody_who_is_not_in_a_prawn_cannot_swing_a_prawn_s_arm()
    {
        StrikeVerdict verdict = Judge(attackerIsInAPrawn: false);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.NotInAPrawn);
    }

    [TestMethod]
    public void The_ring_beats_the_missing_prawn_it_caused()
    {
        Judge(attackerIsHeld: true, attackerIsInAPrawn: false)
            .Refusal.Should().Be(StrikeRefusal.TheRingHasYou);
    }

    [TestMethod]
    public void The_ring_beats_the_reach()
    {
        Judge(victim: new WorldPoint(600, -50, -900), attackerIsHeld: true)
            .Refusal.Should().Be(StrikeRefusal.TheRingHasYou);
    }

    [TestMethod]
    public void A_switched_off_feature_beats_every_other_reason()
    {
        StrikeVerdict verdict = StrikeRule.Judge(PrawnPolicy.Default, StrikeWeapon.Claw,
            Attacker, new WorldPoint(600, -50, -900), attackerIsHeld: true, attackerIsInAVehicle: true,
            attackerIsArmed: false, matchIsLive: false, victimIsInTheMatch: false,
            sinceLastStrike: TimeSpan.Zero);

        verdict.Refusal.Should().Be(StrikeRefusal.PrawnsAreHarmless);
    }

    [TestMethod]
    public void A_disabled_arm_beats_the_situation_too()
    {
        PrawnPolicy noDrill = new PrawnPolicy(true, clawDamage: 20, drillDamage: 0, reach: 9,
            minInterval: TimeSpan.FromMilliseconds(800));

        Judge(policy: noDrill, arm: StrikeWeapon.Drill, sinceLastStrike: TimeSpan.Zero)
            .Refusal.Should().Be(StrikeRefusal.ArmIsDisabled);
    }

    [TestMethod]
    public void A_knife_at_arm_s_length_lands_for_the_configured_damage()
    {
        StrikeVerdict verdict = Stab();

        verdict.Landed.Should().BeTrue();
        verdict.Damage.Should().Be(12, "the number is the server's, out of the config");
        verdict.Refusal.Should().Be(StrikeRefusal.None);
    }

    [TestMethod]
    public void The_blade_decides_which_number_is_charged()
    {
        Stab(blade: StrikeWeapon.HeatBlade).Damage.Should().Be(20);
    }

    [TestMethod]
    public void Blades_that_do_not_hurt_players_refuse_everything()
    {
        StrikeVerdict verdict = Stab(policy: BladePolicy.Default);

        verdict.Landed.Should().BeFalse();
        verdict.Damage.Should().Be(0);
        verdict.Refusal.Should().Be(StrikeRefusal.BladesAreHarmless);
    }

    [TestMethod]
    public void A_blade_with_no_damage_is_refused_rather_than_landing_for_nothing()
    {
        BladePolicy noKnife = new BladePolicy(true, knifeDamage: 0, heatBladeDamage: 20, reach: 6,
            minInterval: TimeSpan.FromMilliseconds(400));

        StrikeVerdict verdict = Stab(policy: noKnife);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.BladeIsDisabled);
    }

    [TestMethod]
    public void The_heat_blade_still_works_when_only_the_knife_is_switched_off()
    {
        BladePolicy noKnife = new BladePolicy(true, knifeDamage: 0, heatBladeDamage: 20, reach: 6,
            minInterval: TimeSpan.FromMilliseconds(400));

        Stab(policy: noKnife, blade: StrikeWeapon.HeatBlade).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_blade_swung_from_inside_a_vehicle_is_refused()
    {
        StrikeVerdict verdict = Stab(attackerIsInAVehicle: true);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.YouAreInAVehicle);
    }

    [TestMethod]
    public void Somebody_holding_nothing_cannot_stab()
    {
        StrikeVerdict verdict = Stab(attackerIsHoldingIt: false);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.NotHoldingThatBlade);
    }

    [TestMethod]
    public void A_stab_at_exactly_the_reach_lands()
    {
        Stab(victim: new WorldPoint(0, -50, 6)).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void A_stab_past_the_reach_is_refused()
    {
        StrikeVerdict verdict = Stab(victim: new WorldPoint(0, -50, 6.001));

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void A_victim_across_the_map_is_not_stabbed_either()
    {
        Stab(victim: new WorldPoint(600, -50, -900)).Refusal.Should().Be(StrikeRefusal.OutOfRange);
    }

    [TestMethod]
    public void A_second_stab_too_soon_after_the_first_is_refused()
    {
        StrikeVerdict verdict = Stab(sinceLastStrike: TimeSpan.FromMilliseconds(399));

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.TooSoon);
    }

    [TestMethod]
    public void A_second_stab_exactly_one_interval_later_lands()
    {
        Stab(sinceLastStrike: TimeSpan.FromMilliseconds(400)).Landed.Should().BeTrue();
    }

    [TestMethod]
    public void Nobody_is_stabbed_while_the_match_is_not_running()
    {
        Stab(matchIsLive: false).Refusal.Should().Be(StrikeRefusal.MatchIsNotRunning);
    }

    [TestMethod]
    public void A_player_the_match_is_finished_with_is_not_stabbed_again()
    {
        Stab(victimIsInTheMatch: false).Refusal.Should().Be(StrikeRefusal.TheyAreAlreadyOut);
    }

    [TestMethod]
    public void A_held_attacker_cannot_stab_either()
    {
        Stab(attackerIsHeld: true).Refusal.Should().Be(StrikeRefusal.TheRingHasYou);
    }

    [TestMethod]
    public void The_vehicle_beats_the_empty_hand_it_caused()
    {
        Stab(attackerIsInAVehicle: true, attackerIsHoldingIt: false)
            .Refusal.Should().Be(StrikeRefusal.YouAreInAVehicle);
    }

    [TestMethod]
    public void The_ring_beats_the_vehicle()
    {
        Stab(attackerIsHeld: true, attackerIsInAVehicle: true, attackerIsHoldingIt: false)
            .Refusal.Should().Be(StrikeRefusal.TheRingHasYou);
    }

    [TestMethod]
    public void The_order_a_blade_s_refusals_are_tried_in_is_the_rule()
    {
        bool bladesHurtPlayers = false;
        double knifeDamage = 0;
        bool matchIsLive = false;
        bool attackerIsHeld = true;
        bool attackerIsInAVehicle = true;
        bool attackerIsHoldingIt = false;
        bool victimIsInTheMatch = false;
        WorldPoint victim = new WorldPoint(600, -50, -900);
        TimeSpan sinceLastStrike = TimeSpan.Zero;

        StrikeVerdict Next() => StrikeRule.Judge(
            new BladePolicy(bladesHurtPlayers, knifeDamage, heatBladeDamage: 20, reach: 6,
                minInterval: TimeSpan.FromMilliseconds(400)),
            StrikeWeapon.Knife, Attacker, victim, attackerIsHeld, attackerIsInAVehicle,
            attackerIsHoldingIt, matchIsLive, victimIsInTheMatch, sinceLastStrike);

        Next().Refusal.Should().Be(StrikeRefusal.BladesAreHarmless,
            "somebody debugging a tournament where nothing hurts is told the feature is off");
        bladesHurtPlayers = true;

        Next().Refusal.Should().Be(StrikeRefusal.BladeIsDisabled,
            "a fact about the whole match beats every fact about this second");
        knifeDamage = 12;

        Next().Refusal.Should().Be(StrikeRefusal.MatchIsNotRunning,
            "in a lobby nobody is being hurt, whatever else is also true");
        matchIsLive = true;

        Next().Refusal.Should().Be(StrikeRefusal.TheRingHasYou,
            "the ring beats every weapon");
        attackerIsHeld = false;

        Next().Refusal.Should().Be(StrikeRefusal.YouAreInAVehicle,
            "the vehicle is the cause and the empty hand is the consequence");
        attackerIsInAVehicle = false;

        Next().Refusal.Should().Be(StrikeRefusal.NotHoldingThatBlade,
            "and only once nothing else explains the empty hand is the empty hand the answer");
        attackerIsHoldingIt = true;

        Next().Refusal.Should().Be(StrikeRefusal.TheyAreAlreadyOut,
            "who the victim is beats where they are: a corpse is not billed at any distance");
        victimIsInTheMatch = true;

        Next().Refusal.Should().Be(StrikeRefusal.OutOfRange);
        victim = new WorldPoint(0, -50, 3);

        Next().Refusal.Should().Be(StrikeRefusal.TooSoon,
            "the last thing tried, because it is the only one that fixes itself in 400 ms");
        sinceLastStrike = TimeSpan.FromSeconds(5);

        Next().Landed.Should().BeTrue("every fact is now right, so the stab lands");
    }

    [TestMethod]
    public void No_prawn_refusal_can_reach_a_blade_attacker_and_no_blade_refusal_a_prawn_one()
    {
        StrikeRefusal[] prawnOnly =
        {
            StrikeRefusal.PrawnsAreHarmless, StrikeRefusal.ArmIsDisabled, StrikeRefusal.NotInAPrawn,
        };

        StrikeRefusal[] bladeOnly =
        {
            StrikeRefusal.BladesAreHarmless, StrikeRefusal.BladeIsDisabled,
            StrikeRefusal.NotHoldingThatBlade, StrikeRefusal.YouAreInAVehicle,
        };

        foreach (StrikeWeapon weapon in Enum.GetValues<StrikeWeapon>())
        {
            WeaponFamily family = StrikeWeapons.Family(weapon);
            HashSet<StrikeRefusal> seen = new HashSet<StrikeRefusal>();

            foreach (bool weaponIsOn in new[] { true, false })
            {
                for (int facts = 0; facts < 64; facts++)
                {
                    StrikeVerdict verdict = StrikeRule.Judge(
                        PolicyFor(weapon, hurtsPlayers: (facts & 1) != 0, weaponIsOn: weaponIsOn),
                        weapon, Attacker, new WorldPoint(0, -50, 3),
                        attackerIsHeld: (facts & 2) != 0,
                        attackerIsInAVehicle: (facts & 4) != 0,
                        attackerIsArmed: (facts & 8) != 0,
                        matchIsLive: (facts & 16) != 0,
                        victimIsInTheMatch: (facts & 32) != 0,
                        sinceLastStrike: Ages);

                    string swing = weapon + " with facts " + facts + (weaponIsOn ? " on" : " off");
                    seen.Add(verdict.Refusal);

                    if (family == WeaponFamily.PrawnArm)
                    {
                        bladeOnly.Should().NotContain(verdict.Refusal, "{0} is a prawn's arm", swing);
                    }
                    else
                    {
                        prawnOnly.Should().NotContain(verdict.Refusal, "{0} is a blade", swing);
                    }
                }
            }

            seen.Should().Contain(family == WeaponFamily.PrawnArm ? prawnOnly : bladeOnly,
                "every refusal {0} owns has to be reachable", family);
        }
    }

    private static StrikePolicy PolicyFor(StrikeWeapon weapon, bool hurtsPlayers, bool weaponIsOn)
    {
        bool off(StrikeWeapon candidate) => weapon == candidate && !weaponIsOn;

        return StrikeWeapons.Family(weapon) == WeaponFamily.PrawnArm
            ? new PrawnPolicy(hurtsPlayers,
                clawDamage: off(StrikeWeapon.Claw) ? 0 : 20,
                drillDamage: off(StrikeWeapon.Drill) ? 0 : 4,
                reach: 9, minInterval: TimeSpan.FromMilliseconds(800))
            : (StrikePolicy)new BladePolicy(hurtsPlayers,
                knifeDamage: off(StrikeWeapon.Knife) ? 0 : 12,
                heatBladeDamage: off(StrikeWeapon.HeatBlade) ? 0 : 20,
                reach: 6, minInterval: TimeSpan.FromMilliseconds(400));
    }

    [TestMethod]
    public void A_ghost_who_stabs_is_told_they_are_a_ghost_rather_than_told_their_hand_is_empty()
    {
        StrikeVerdict verdict = Stab(attackerIsAGhost: true, attackerIsHoldingIt: false);

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.YouAreAGhost);
    }

    [TestMethod]
    public void Being_a_ghost_beats_every_other_reason_the_swing_would_have_failed()
    {
        Stab(attackerIsAGhost: true, attackerIsHeld: true).Refusal
            .Should().Be(StrikeRefusal.YouAreAGhost);
        Stab(attackerIsAGhost: true, attackerIsInAVehicle: true).Refusal
            .Should().Be(StrikeRefusal.YouAreAGhost);
        Stab(attackerIsAGhost: true, victimIsInTheMatch: false).Refusal
            .Should().Be(StrikeRefusal.YouAreAGhost);
    }

    [TestMethod]
    public void A_claw_from_a_ghost_is_refused_the_same_way()
    {
        Judge(attackerIsAGhost: true).Refusal.Should().Be(StrikeRefusal.YouAreAGhost);
    }

    [TestMethod]
    public void A_match_that_is_not_running_is_still_said_first()
    {
        Stab(attackerIsAGhost: true, matchIsLive: false).Refusal
            .Should().Be(StrikeRefusal.MatchIsNotRunning);
    }

    [TestMethod]
    public void A_knife_against_a_reinforced_suit_lands_for_half()
    {
        StrikeVerdict verdict = Stab(victimEquipment: new[]
        {
            new EquipmentLine("Body", "ReinforcedDiveSuit"),
        });

        verdict.Landed.Should().BeTrue();
        verdict.Damage.Should().Be(6);
    }

    [TestMethod]
    public void A_knife_against_a_victim_in_gear_the_config_does_not_name_lands_in_full()
    {
        StrikeVerdict verdict = Stab(victimEquipment: new[]
        {
            new EquipmentLine("Feet", "Fins"),
        });

        verdict.Damage.Should().Be(12);
    }

    [TestMethod]
    public void Equipment_the_server_was_never_told_about_changes_nothing()
    {
        Stab().Damage.Should().Be(12);
    }

    [TestMethod]
    public void Equipment_cannot_turn_a_refusal_into_a_hit()
    {
        StrikeVerdict verdict = Stab(policy: BladePolicy.Default, victimEquipment: new[]
        {
            new EquipmentLine("Body", "ReinforcedDiveSuit"),
        });

        verdict.Landed.Should().BeFalse();
        verdict.Refusal.Should().Be(StrikeRefusal.BladesAreHarmless);
    }

    [TestMethod]
    public void A_prawn_claw_takes_no_reading_from_the_victims_equipment()
    {
        StrikeVerdict verdict = Judge(victimEquipment: new[]
        {
            new EquipmentLine("Body", "ReinforcedDiveSuit"),
        });

        verdict.Damage.Should().Be(20);
    }
}
