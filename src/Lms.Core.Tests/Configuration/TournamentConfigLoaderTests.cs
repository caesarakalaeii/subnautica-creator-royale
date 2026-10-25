using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Configuration;

/// <summary>The tournament config loader: invalid files are reported naming the field.</summary>
[TestClass]
public class TournamentConfigLoaderTests
{
    private const string ValidConfig = @"
match:
  cycleLength: 5m
  warningLead: 1m
zone:
  damageInterval: 2s
  opening:
    centre: [0, -100, 0]
    radius: 1400
    damagePerTick: 1
  phases:
    - centre: [0, -100, 0]
      radius: 900
      shrinkFor: 90s
      damagePerTick: 3
    - centre: [200, -150, 100]
      radius: 400
      shrinkFor: 90s
      damagePerTick: 8
flags:
  enforceDamage: true
";

    private static TournamentConfigLoadResult Load(string yaml) => TournamentConfigLoader.Load(yaml);

    [TestMethod]
    public void The_file_can_put_a_cooldown_on_first_aid_kits()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
medkit:
  cooldown: 45s
");

        result.Problems.Should().BeEmpty();
        result.Config!.MedKit.Cooldown.Should().Be(TimeSpan.FromSeconds(45));
        result.Config!.MedKit.IsEnforced.Should().BeTrue();
    }

    [TestMethod]
    public void A_file_with_no_medkit_section_leaves_the_games_own_behaviour_alone()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.MedKit.IsEnforced.Should().BeFalse();
    }

    [TestMethod]
    public void A_medkit_cooldown_that_runs_backwards_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
medkit:
  cooldown: -5s
");

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path.StartsWith("medkit."));
    }

    [TestMethod]
    public void The_file_can_say_how_a_round_begins()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
start:
  hold: 45s
  fieldRadius: 20
  kit:
    - Knife
    - Flashlight
  lockedRecipes:
    - Cyclops
");

        result.Problems.Should().BeEmpty();

        StartPolicy start = result.Config!.Start;
        start.Hold.Should().Be(TimeSpan.FromSeconds(45));
        start.FieldRadius.Should().Be(20);
        start.Kit.Should().Equal("Knife", "Flashlight");
        start.LockedRecipes.Should().Equal("Cyclops");

        start.GrantsEverything.Should().BeTrue();
    }

    [TestMethod]
    public void A_file_with_no_start_section_gets_the_conservative_defaults()
    {
        StartPolicy start = Load(ValidConfig).Config!.Start;

        start.Holds.Should().BeTrue();
        start.Kit.Should().BeEmpty("a fresh Subnautica player starts empty-handed");
        start.LockedRecipes.Should().Contain("Cyclops").And.Contain("Exosuit").And.Contain("Seamoth");
        start.Recipes.Should().BeEmpty("the vanilla baseline is the floor nobody has to write down");
        start.GrantsEverything.Should().BeFalse();
    }

    [TestMethod]
    public void The_destination_is_withheld_unless_the_file_says_to_show_it()
    {
        Load(ValidConfig).Config!.Flags.ShowDestination.Should().BeFalse();

        Load(ValidConfig.Replace("  enforceDamage: true", "  enforceDamage: true\n  showDestination: true"))
            .Config!.Flags.ShowDestination.Should().BeTrue();
    }

    [TestMethod]
    public void The_floor_a_round_grants_is_read_off_the_file()
    {
        StartPolicy start = Load(ValidConfig + @"
start:
  recipes:
    - Knife
    - Seaglide
").Config!.Start;

        start.Recipes.Should().Equal("Knife", "Seaglide");
        start.GrantsEverything.Should().BeFalse();
    }

    [TestMethod]
    public void A_floor_of_all_is_how_a_config_still_asks_for_every_blueprint()
    {
        StartPolicy start = Load(ValidConfig + @"
start:
  recipes:
    - all
").Config!.Start;

        start.GrantsEverything.Should().BeTrue();
    }

    [TestMethod]
    public void Asking_for_everything_and_then_narrowing_it_is_refused_rather_than_guessed_at()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
start:
  recipes:
    - all
    - Knife
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle()
            .Which.Path.Should().Be("start.recipes");
    }

    [TestMethod]
    public void An_absent_floor_and_an_empty_one_are_the_same_answer()
    {
        Load(ValidConfig + @"
start:
  recipes: []
").Config!.Start.Recipes.Should().BeEmpty();

        Load(ValidConfig).Config!.Start.Recipes.Should().BeEmpty();
    }

    [TestMethod]
    public void Locks_with_no_floor_grant_everything_but_the_locks()
    {
        StartPolicy start = Load(WithStart("  lockedRecipes:\n    - Seamoth\n")).Config!.Start;

        start.GrantsEverything.Should().BeTrue();
        start.Recipes.Should().Equal("all");
        start.LockedRecipes.Should().Equal("Seamoth");
    }

    [TestMethod]
    public void An_empty_locked_list_and_no_floor_is_the_full_grant()
    {
        StartPolicy start = Load(WithStart("  lockedRecipes: []\n")).Config!.Start;

        start.GrantsEverything.Should().BeTrue();
        start.LockedRecipes.Should().BeEmpty();
    }

    [TestMethod]
    public void Locks_beside_a_written_floor_subtract_from_it_as_before()
    {
        StartPolicy start = Load(WithStart(
            "  lockedRecipes: [Seaglide]\n  recipes:\n    - Knife\n")).Config!.Start;

        start.Recipes.Should().Equal("Knife");
        start.GrantsEverything.Should().BeFalse();
        start.LockedRecipes.Should().Equal("Seaglide");
    }

    [TestMethod]
    public void Locks_beside_an_explicit_vanilla_floor_are_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(
            WithStart("  lockedRecipes: [Seamoth]\n  recipes: []\n"));

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle()
            .Which.Path.Should().Be("start.lockedRecipes");
    }

    [TestMethod]
    public void An_empty_locked_list_grants_everything_and_is_not_the_same_as_an_absent_one()
    {
        Load(ValidConfig + @"
start:
  lockedRecipes: []
").Config!.Start.LockedRecipes.Should().BeEmpty();

        Load(ValidConfig).Config!.Start.LockedRecipes.Should().NotBeEmpty();
    }

    [TestMethod]
    public void A_hold_of_nothing_starts_the_match_as_the_round_is_prepared()
    {
        StartPolicy start = Load(ValidConfig + @"
start:
  hold: 0s
").Config!.Start;

        start.Holds.Should().BeFalse();
    }

    [TestMethod]
    public void A_field_with_no_radius_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
start:
  fieldRadius: 0
");

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path.StartsWith("start.fieldRadius"));
    }

    [TestMethod]
    public void A_kit_that_is_not_a_list_is_refused_against_its_own_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
start:
  kit:
    Knife: 1
");

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "start.kit");
    }

    [TestMethod]
    public void The_file_can_name_the_people_the_match_cannot_see()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ghosts:
  - Caster
  - Producer
");

        result.Problems.Should().BeEmpty();
        result.Config!.Ghosts.Should().Equal("Caster", "Producer");
    }

    [TestMethod]
    public void A_file_with_no_ghosts_section_has_no_ghosts_in_it()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Ghosts.Should().BeEmpty();
    }

    [TestMethod]
    public void An_empty_ghosts_list_is_allowed_because_it_is_how_the_list_is_switched_off()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ghosts: []
");

        result.Problems.Should().BeEmpty();
        result.Config!.Ghosts.Should().BeEmpty();
    }

    [TestMethod]
    public void Ghosts_has_to_be_a_list_of_names()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ghosts:
  Caster: true
");

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "ghosts");
    }

    [TestMethod]
    public void A_ghost_with_no_name_is_refused_rather_than_skipped()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ghosts:
  - Caster
  - ''
");

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "ghosts[1]");
    }

    [TestMethod]
    public void The_documented_example_config_still_loads()
    {
        string example = System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "tournament.example.yaml"));

        TournamentConfigLoadResult result = Load(example);

        result.Problems.Should().BeEmpty();
        result.Config!.Flags.EnforceDamage.Should().BeFalse();

        result.Config.Blade.HurtsPlayers.Should().BeFalse(
            "the file an operator copies must not arrive armed");

        result.Config.Start.GrantsEverything.Should().BeTrue(
            "the reference file still hands out everything, and flipping that is a decision");
    }

    [TestMethod]
    public void The_config_that_ships_to_testers_still_loads()
    {
        string shipped = System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "tournament.playtest.yaml"));

        TournamentConfigLoadResult result = Load(shipped);

        result.Problems.Should().BeEmpty();
        result.Config!.Flags.EnforceDamage.Should().BeTrue("the point of that file is to feel the bite");

        result.Config.Spawn.OwnPod.Should().BeTrue("the test config gives everyone a pod");
        result.Config.Start.GrantsEverything.Should().BeTrue(
            "the shipped file still grants everything; `recipes: []` is the switch and it is a decision");
        result.Config.Spectator.Spectate.Should().BeTrue("the test config lets the dead fly");
        result.Config.Prawn.HurtsPlayers.Should().BeTrue("the test config arms the claw");

        result.Config.Blade.HurtsPlayers.Should().BeTrue("the test config arms the knife");

        result.Config.Squeeze.Should().NotBeNull("the test config draws the endgame from a pool");
        result.Config.Squeeze!.IsListed.Should().BeFalse("the event draws every game's place at random");
        result.Config.DestinationSeed.Should().NotBe(0, "a seed is changed per event and written down");
        Enumerable.Range(1, 6).Select(group => result.Config.DestinationFor(group)!.Name).Should().OnlyHaveUniqueItems(
            "no place comes round twice until the pool is exhausted");

        result.Config.Spawn.GroupSize.Should().Be(4, "the event's groups are four");
        result.Config.Ghosts.Should().BeEquivalentTo(new[] { "CasterOne", "CasterTwo" },
            "the crew known before the day is ghosted by the file, not by hand");

        result.Config.Squeeze.Destinations.Should().NotContain(
            place => place.Name.Contains("Emperor"),
            "it is out of the pool, and a config that quietly kept it would send one group in six " +
            "somewhere the round was decided to be unplayable");

        result.Config.DestinationFor(1)!.Name.Should().NotBe(result.Config.DestinationFor(2)!.Name);
    }

    [TestMethod]
    public void The_hand_authored_fixture_still_loads()
    {
        string playtest = System.IO.File.ReadAllText(
            System.IO.Path.Combine(AppContext.BaseDirectory, "tournament.test.yaml"));

        TournamentConfigLoadResult result = Load(playtest);

        result.Problems.Should().BeEmpty();
        result.Config!.Flags.EnforceDamage.Should().BeTrue("the point of that file is to feel the bite");

        result.Config.Spawn.OwnPod.Should().BeTrue("the test config gives everyone a pod");
        result.Config.Spectator.Spectate.Should().BeTrue("the test config lets the dead fly");
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_the_ring_gets_the_rifle_the_game_ships()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Ring.Limits.MaxRadius.Should().Be(10);
        result.Config.Ring.Limits.MaxLifetime.Should().Be(TimeSpan.FromSeconds(20));
        result.Config.Ring.PlacementMargin.Should().Be(1);
        result.Config.Ring.EnergyCost.Should().Be(5);
    }

    [TestMethod]
    public void A_duration_can_be_written_in_milliseconds()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  minInterval: 250ms
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.MinInterval.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [TestMethod]
    public void Minutes_still_mean_minutes_now_that_milliseconds_exist()
    {
        Load(ValidConfig).Config!.Clock.CycleLength.Should().Be(TimeSpan.FromMinutes(5));
        Load(ValidConfig + @"
prawn:
  minInterval: 2s
").Config!.Prawn.MinInterval.Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_the_prawn_leaves_the_arms_harmless()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.HurtsPlayers.Should().BeFalse();
    }

    [TestMethod]
    public void A_prawn_section_that_forgets_the_switch_is_still_off()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  clawDamage: 35
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.HurtsPlayers.Should().BeFalse();
        result.Config.Prawn.ClawDamage.Should().Be(35, "the number is still read, ready for the day it is on");
    }

    [TestMethod]
    public void The_prawn_s_damage_can_be_retuned_without_a_rebuild()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  clawDamage: 15
  drillDamage: 2
  reach: 12
  minInterval: 1200ms
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.HurtsPlayers.Should().BeTrue();
        result.Config.Prawn.ClawDamage.Should().Be(15);
        result.Config.Prawn.DrillDamage.Should().Be(2);
        result.Config.Prawn.Reach.Should().Be(12);
        result.Config.Prawn.MinInterval.Should().Be(TimeSpan.FromMilliseconds(1200));
    }

    [TestMethod]
    public void Naming_one_prawn_field_does_not_silently_reset_the_others()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  clawDamage: 15
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.ClawDamage.Should().Be(15);
        result.Config.Prawn.DrillDamage.Should().Be(0, "the drill still ships switched off");
        result.Config.Prawn.Reach.Should().Be(9);
        result.Config.Prawn.MinInterval.Should().Be(TimeSpan.FromMilliseconds(800));
    }

    [TestMethod]
    public void An_arm_can_be_switched_off_on_its_own()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  drillDamage: 0
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.DrillDamage.Should().Be(0);
    }

    [TestMethod]
    public void A_reach_shorter_than_the_game_s_own_claw_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  reach: 3
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("prawn.reach");
    }

    [TestMethod]
    public void An_arm_that_heals_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  clawDamage: -5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("prawn.clawDamage");
    }

    [TestMethod]
    public void A_prawn_with_no_interval_between_swings_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  minInterval: 0s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("prawn.minInterval");
    }

    [TestMethod]
    public void A_mistyped_prawn_field_refuses_the_file_rather_than_shipping_a_zero()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  clawDamge: 20
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("prawn.clawDamge");
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_blades_leaves_them_harmless()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.Should().BeSameAs(BladePolicy.Default);
        result.Config.Blade.HurtsPlayers.Should().BeFalse();
    }

    [TestMethod]
    public void A_blade_section_that_forgets_the_switch_is_still_off()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  knifeDamage: 35
");

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.HurtsPlayers.Should().BeFalse();
        result.Config.Blade.KnifeDamage.Should().Be(35, "the number is still read, ready for the day it is on");
    }

    [TestMethod]
    public void The_blades_can_be_retuned_without_a_rebuild()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  knifeDamage: 8
  heatBladeDamage: 25
  reach: 7
  minInterval: 600ms
");

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.HurtsPlayers.Should().BeTrue();
        result.Config.Blade.KnifeDamage.Should().Be(8);
        result.Config.Blade.HeatBladeDamage.Should().Be(25);
        result.Config.Blade.Reach.Should().Be(7);
        result.Config.Blade.MinInterval.Should().Be(TimeSpan.FromMilliseconds(600));
    }

    [TestMethod]
    public void Naming_one_blade_field_does_not_silently_reset_the_others()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  knifeDamage: 8
");

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.KnifeDamage.Should().Be(8);
        result.Config.Blade.HeatBladeDamage.Should().Be(20);
        result.Config.Blade.Reach.Should().Be(6);
        result.Config.Blade.MinInterval.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [TestMethod]
    public void A_blade_can_be_switched_off_on_its_own()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  knifeDamage: 0
");

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.KnifeDamage.Should().Be(0);
        result.Config.Blade.HeatBladeDamage.Should().Be(20, "switching one off leaves the other alone");
    }

    [TestMethod]
    public void A_blade_reach_shorter_than_the_geometry_of_a_hit_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  reach: 3
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.reach");
    }

    [TestMethod]
    public void A_blade_that_heals_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  heatBladeDamage: -5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.heatBladeDamage");
    }

    [TestMethod]
    public void A_blade_with_no_interval_between_stabs_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  minInterval: 0s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.minInterval");
    }

    [TestMethod]
    public void A_mistyped_blade_field_refuses_the_file_rather_than_shipping_a_zero()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  knifeDamge: 8
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.knifeDamge");
    }

    [TestMethod]
    public void The_prawn_and_the_blade_sections_are_read_independently()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
prawn:
  hurtsPlayers: true
  clawDamage: 15
");

        result.Problems.Should().BeEmpty();
        result.Config!.Prawn.HurtsPlayers.Should().BeTrue();
        result.Config.Blade.HurtsPlayers.Should().BeFalse("no blade section means no blades");
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_resistances_leaves_the_reinforced_suit_at_half()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.Resistances.Should().ContainKey("ReinforcedDiveSuit")
            .WhoseValue.Should().Be(0.5);
    }

    [TestMethod]
    public void A_resistances_map_is_read_as_written()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances:
    ReinforcedDiveSuit: 0.25
    StillsuitBody: 1
");

        result.Problems.Should().BeEmpty();
        result.Config!.Blade.Resistances["ReinforcedDiveSuit"].Should().Be(0.25);
        result.Config.Blade.Resistances["StillsuitBody"].Should().Be(1);
    }

    [TestMethod]
    public void A_resistance_that_amplifies_the_blade_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances:
    ReinforcedDiveSuit: 1.5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistances.ReinforcedDiveSuit");
    }

    [TestMethod]
    public void A_resistance_that_shields_completely_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances:
    ReinforcedDiveSuit: 0
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistances.ReinforcedDiveSuit");
    }

    [TestMethod]
    public void A_resistance_that_is_not_a_number_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances:
    ReinforcedDiveSuit: half
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistances.ReinforcedDiveSuit");
    }

    [TestMethod]
    public void A_resistances_field_that_is_not_a_map_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances: 3
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistances");
    }

    [TestMethod]
    public void A_resistance_for_an_unnamed_piece_of_gear_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistances:
    """": 0.5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistances.");
    }

    [TestMethod]
    public void A_misspelled_resistances_sibling_is_still_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
blade:
  hurtsPlayers: true
  resistance:
    ReinforcedDiveSuit: 0.5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("blade.resistance");
    }

    [TestMethod]
    public void A_ring_can_be_retuned_without_a_rebuild()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  minRadius: 3
  maxRadius: 25
  minLifetime: 8s
  maxLifetime: 45s
  placementMargin: 2
  energyCost: 25
");

        result.Problems.Should().BeEmpty();
        result.Config!.Ring.Limits.MinRadius.Should().Be(3);
        result.Config.Ring.Limits.MaxRadius.Should().Be(25);
        result.Config.Ring.Limits.MinLifetime.Should().Be(TimeSpan.FromSeconds(8));
        result.Config.Ring.Limits.MaxLifetime.Should().Be(TimeSpan.FromSeconds(45));
        result.Config.Ring.PlacementMargin.Should().Be(2);
        result.Config.Ring.EnergyCost.Should().Be(25);
    }

    [TestMethod]
    public void A_ring_that_costs_nothing_to_fire_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  energyCost: 0
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("ring.energyCost");
    }

    [TestMethod]
    public void Naming_one_ring_field_does_not_silently_reset_the_others()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  maxRadius: 25
");

        result.Problems.Should().BeEmpty();
        result.Config!.Ring.Limits.MaxRadius.Should().Be(25);
        result.Config.Ring.Limits.MinRadius.Should().Be(1, "the rest of the ring is still the rifle's");
        result.Config.Ring.Limits.MaxLifetime.Should().Be(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public void A_ring_that_shrinks_as_it_is_charged_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  minRadius: 20
  maxRadius: 5
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("ring.maxRadius");
    }

    [TestMethod]
    public void A_ring_that_lasts_less_at_a_full_charge_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  minLifetime: 30s
  maxLifetime: 10s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("ring.maxLifetime");
    }

    [TestMethod]
    public void A_margin_that_fills_the_biggest_ring_is_refused_by_name()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  placementMargin: 20
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("ring.placementMargin");
    }

    [TestMethod]
    public void A_mistyped_ring_field_is_an_error_rather_than_a_default()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
ring:
  maxRadiusMetres: 25
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("ring.maxRadiusMetres");
    }

    [TestMethod]
    public void The_ring_holds_people_unless_the_file_says_otherwise()
    {
        Load(ValidConfig).Config!.Flags.EnforceContainment.Should().BeTrue();

        Load(ValidConfig + @"
  enforceContainment: false
").Config!.Flags.EnforceContainment.Should().BeFalse();
    }

    [TestMethod]
    public void Beacons_are_hidden_unless_the_file_says_otherwise()
    {
        Load(ValidConfig).Config!.Flags.HideBeacons.Should().BeTrue();

        Load(ValidConfig + @"
  hideBeacons: false
").Config!.Flags.HideBeacons.Should().BeFalse();
    }

    [TestMethod]
    public void A_valid_file_loads_the_cadence()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Clock.CycleLength.Should().Be(TimeSpan.FromMinutes(5));
        result.Config.Clock.WarningLead.Should().Be(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void A_valid_file_loads_the_opening_sphere_and_every_phase_after_it()
    {
        TournamentConfig config = Load(ValidConfig).Config!;

        config.Zone.Opening.Radius.Should().Be(1400);
        config.Zone.Opening.Centre.Depth.Should().Be(100);
        config.Zone.AuthoredPhaseCount.Should().Be(2);
        config.Zone.PhaseCount.Should().Be(3, "the match also runs the collapse the file does not write");
        config.Zone.Phases[1].Target.Centre.X.Should().Be(200);
        config.Zone.Phases[1].DamagePerTick.Should().Be(8);
    }

    [TestMethod]
    public void Phases_are_numbered_in_the_order_they_are_written()
    {
        ZoneSchedule zone = Load(ValidConfig).Config!.Zone;

        zone.Phases.Select(p => p.Number).Should().Equal(1, 2, 3);
        zone.Phases.Select(p => p.IsFinalCollapse).Should().Equal(false, false, true);
    }

    [TestMethod]
    public void The_damage_tick_defaults_to_the_two_seconds_the_client_chose()
    {
        string silent = ValidConfig.Replace("  damageInterval: 2s\n", string.Empty);

        Load(silent).Config!.ZonePolicy.DamageInterval.Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void The_damage_tick_can_be_changed_between_matches()
    {
        Load(ValidConfig.Replace("damageInterval: 2s", "damageInterval: 5s"))
            .Config!.ZonePolicy.DamageInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void A_damage_tick_of_no_time_names_the_field_rather_than_throwing()
    {
        Load(ValidConfig.Replace("damageInterval: 2s", "damageInterval: 0s"))
            .Problems.Should().Contain(p => p.Path == "zone.damageInterval");
    }

    [TestMethod]
    public void Flags_default_to_the_safe_setting_when_absent()
    {
        string withoutFlags = ValidConfig.Replace("flags:\n  enforceDamage: true\n", string.Empty);

        Load(withoutFlags).Config!.Flags.EnforceDamage
            .Should().BeFalse("a config that forgets to say must not start hurting people");
    }

    [TestMethod]
    public void A_client_cannot_be_told_to_start_a_tournament_it_does_not_own()
    {
        string stale = ValidConfig.Replace("enforceDamage: true", "enforceDamage: true\n  autoStart: false");

        TournamentConfigLoadResult result = Load(stale);

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(problem => problem.ToString().Contains("autoStart"));
    }

    [TestMethod]
    [DataRow("90s", 90, DisplayName = "seconds")]
    [DataRow("5m", 300, DisplayName = "minutes")]
    [DataRow("1h", 3600, DisplayName = "hours")]
    [DataRow("00:04:30", 270, DisplayName = "clock form")]
    public void Durations_can_be_written_the_way_a_person_would(string written, int expectedSeconds)
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("cycleLength: 5m", "cycleLength: " + written));

        result.Config!.Clock.CycleLength.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [TestMethod]
    public void A_duration_that_makes_no_sense_names_the_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("cycleLength: 5m", "cycleLength: soonish"));

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Path == "match.cycleLength");
    }

    [TestMethod]
    public void A_missing_field_names_the_field_that_is_missing()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("  warningLead: 1m\n", string.Empty));

        result.Problems.Should().Contain(p => p.Path == "match.warningLead");
    }

    [TestMethod]
    public void An_unknown_field_is_an_error_rather_than_something_ignored()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("damageInterval: 2s", "damageIntervall: 2s"));

        result.Problems.Should().Contain(p => p.Path == "zone.damageIntervall");
    }

    [TestMethod]
    public void An_unknown_field_inside_a_phase_names_the_phase_it_is_in()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("      shrinkFor: 90s\n      damagePerTick: 8",
                                                                     "      shrinkFor: 90s\n      damgePerTick: 8"));

        result.Problems.Should().Contain(p => p.Path == "zone.phases[1].damgePerTick");
    }

    [TestMethod]
    public void A_centre_that_is_not_three_numbers_names_the_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("    centre: [0, -100, 0]\n    radius: 1400",
                                                                     "    centre: [0, -100]\n    radius: 1400"));

        result.Problems.Should().Contain(p => p.Path == "zone.opening.centre");
    }

    [TestMethod]
    public void A_centre_with_something_that_is_not_a_number_in_it_names_the_position()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("    centre: [0, -100, 0]\n    radius: 1400",
                                                                     "    centre: [0, deep, 0]\n    radius: 1400"));

        result.Problems.Should().Contain(p => p.Path == "zone.opening.centre[1]");
    }

    [TestMethod]
    public void A_radius_of_zero_names_the_field_rather_than_throwing()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("radius: 1400", "radius: 0"));

        result.Problems.Should().Contain(p => p.Path == "zone.opening.radius");
    }

    [TestMethod]
    public void A_phase_that_escapes_the_one_before_it_is_a_config_error()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("[200, -150, 100]", "[800, -150, 100]"));

        result.Problems.Should().Contain(p => p.Path == "zone.phases[1]" && p.Message.Contains("sticks"));
    }

    [TestMethod]
    public void A_squeeze_that_never_shrinks_is_a_config_error()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("      radius: 400", "      radius: 1000"));

        result.Problems.Should().Contain(p => p.Path == "zone.phases[1]");
    }

    [TestMethod]
    public void A_squeeze_with_no_phases_at_all_is_a_config_error()
    {
        string flat = ValidConfig[..ValidConfig.IndexOf("  phases:", StringComparison.Ordinal)]
                      + "  phases: []\nflags:\n  enforceDamage: true\n";

        Load(flat).Problems.Should().Contain(p => p.Path == "zone.phases");
    }

    [TestMethod]
    public void Everything_wrong_is_reported_in_one_pass()
    {
        TournamentConfigLoadResult result = Load(ValidConfig
            .Replace("cycleLength: 5m", "cycleLength: soonish")
            .Replace("radius: 1400", "radius: nope"));

        result.Problems.Select(p => p.Path).Should().Contain(new[] { "match.cycleLength", "zone.opening.radius" });
    }

    [TestMethod]
    public void A_file_with_any_problem_at_all_yields_no_config()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("      radius: 400", "      radius: 1000"));

        result.Problems.Should().NotBeEmpty();
        result.Config.Should().BeNull();
        result.IsValid.Should().BeFalse();
    }

    [TestMethod]
    public void An_empty_file_is_a_problem_rather_than_an_empty_tournament()
    {
        TournamentConfigLoadResult result = Load("   ");

        result.IsValid.Should().BeFalse();
        result.Config.Should().BeNull();
    }

    [TestMethod]
    public void Something_that_is_not_yaml_at_all_is_reported_and_not_thrown()
    {
        TournamentConfigLoadResult result = Load("match: [unclosed\n  : :");

        result.IsValid.Should().BeFalse();
        result.Problems.Should().NotBeEmpty();
    }

    [TestMethod]
    public void A_missing_zone_section_names_the_section()
    {
        string noZone = ValidConfig[..ValidConfig.IndexOf("zone:", StringComparison.Ordinal)]
                        + "flags:\n  enforceDamage: true\n";

        Load(noZone).Problems.Should().Contain(p => p.Path == "zone");
    }

    [TestMethod]
    public void A_flag_that_is_not_a_yes_or_no_names_the_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("enforceDamage: true", "enforceDamage: maybe"));

        result.Problems.Should().Contain(p => p.Path == "flags.enforceDamage");
    }

    [TestMethod]
    public void The_wall_colours_default_to_the_pair_the_minimap_already_drew()
    {
        TournamentConfig config = Load(ValidConfig).Config!;

        config.Colours.Now.Should().Be(ZoneColours.Default.Now);
        config.Colours.Next.Should().Be(ZoneColours.Default.Next);
    }

    [TestMethod]
    public void The_wall_colours_can_be_authored()
    {
        TournamentConfig config = Load(WithColours(@"
    now: ""#112233""
    next: ""#44556677""")).Config!;

        config.Colours.Now.Should().Be(new ZoneColour(0x11, 0x22, 0x33));
        config.Colours.Next.Should().Be(new ZoneColour(0x44, 0x55, 0x66, 0x77));
    }

    [TestMethod]
    public void Naming_one_colour_leaves_the_other_alone()
    {
        TournamentConfig config = Load(WithColours(@"
    now: ""#112233""")).Config!;

        config.Colours.Now.Should().Be(new ZoneColour(0x11, 0x22, 0x33));
        config.Colours.Next.Should().Be(ZoneColours.Default.Next);
    }

    [TestMethod]
    public void A_colour_that_is_not_a_colour_names_the_field()
    {
        TournamentConfigLoadResult result = Load(WithColours(@"
    now: gold"));

        result.Problems.Should().Contain(p => p.Path == "zone.colours.now");
        result.Config.Should().BeNull();
    }

    [TestMethod]
    public void An_unquoted_colour_is_told_to_quote_itself()
    {
        TournamentConfigLoadResult result = Load(WithColours(@"
    now: #FFD65C"));

        result.Problems.Should().Contain(p => p.Path == "zone.colours.now" && p.Message.Contains("quote"));
    }

    [TestMethod]
    public void An_unknown_field_in_the_colours_section_is_refused_like_any_other()
    {
        TournamentConfigLoadResult result = Load(WithColours(@"
    wall: ""#112233"""));

        result.Problems.Should().Contain(p => p.Path == "zone.colours.wall");
    }

    [TestMethod]
    public void Spectating_is_the_games_own_five_seconds_and_free_flight_when_the_file_says_nothing()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Spectator.DeathCam.Should().Be(TimeSpan.FromSeconds(5));
        result.Config.Spectator.Spectate.Should().BeTrue();
    }

    [TestMethod]
    public void The_spectator_fallback_is_a_flag_rather_than_a_rebuild()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spectator:
  spectate: false
");

        result.Problems.Should().BeEmpty();
        result.Config!.Spectator.Spectate.Should().BeFalse();
    }

    [TestMethod]
    public void The_death_cam_is_retunable_between_matches()
    {
        Load(ValidConfig + @"
spectator:
  deathCam: 8s
").Config!.Spectator.DeathCam.Should().Be(TimeSpan.FromSeconds(8));
    }

    [TestMethod]
    public void A_death_cam_that_runs_before_the_death_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spectator:
  deathCam: -2s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spectator.deathCam");
    }

    [TestMethod]
    public void How_fast_a_spectator_flies_is_retunable_between_matches()
    {
        Load(ValidConfig + @"
spectator:
  flySpeed: 12
").Config!.Spectator.FlySpeed.Should().Be(12);
    }

    [TestMethod]
    public void A_spectator_who_cannot_move_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spectator:
  flySpeed: 0
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spectator.flySpeed");
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_the_fly_speed_does_not_get_the_games_crawl()
    {
        Load(ValidConfig).Config!.Spectator.FlySpeed.Should().BeGreaterThan(1);
    }

    [TestMethod]
    public void A_mistyped_spectator_field_is_an_error_rather_than_a_default()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spectator:
  spectating: false
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spectator.spectating");
    }

    [TestMethod]
    public void Everybody_shares_one_escape_pod_unless_the_file_asks_otherwise()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.OwnPod.Should().BeFalse();
    }

    [TestMethod]
    public void A_pod_each_is_asked_for_by_name_and_carries_how_far_apart()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  minSeparation: 600
");

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.OwnPod.Should().BeTrue();
        result.Config.Spawn.MinSeparation.Should().Be(600);
    }

    [TestMethod]
    public void A_separation_that_is_not_a_distance_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  minSeparation: -20
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spawn.minSeparation");
    }

    [TestMethod]
    public void The_pods_are_not_on_a_circle_unless_the_file_asks_for_one()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  minSeparation: 600
");

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.CircleRadius.Should().Be(0);
        result.Config.Spawn.OnACircle.Should().BeFalse();
    }

    [TestMethod]
    public void A_circle_of_pods_is_asked_for_by_its_radius()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  minSeparation: 600
  circleRadius: 900
");

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.CircleRadius.Should().Be(900);
        result.Config.Spawn.OnACircle.Should().BeTrue();
    }

    [TestMethod]
    public void A_radius_that_is_not_a_distance_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  circleRadius: -900
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spawn.circleRadius");
    }

    [TestMethod]
    public void A_mistyped_spawn_field_is_an_error_rather_than_a_default()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPods: true
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spawn.ownPods");
    }

    [TestMethod]
    public void A_file_written_before_the_victory_window_existed_still_loads()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Clock.VictoryWindow.Should().Be(MatchClockSettings.DefaultVictoryWindow);
    }

    [TestMethod]
    public void The_winners_moment_is_a_number_an_operator_can_change()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace(
            "  warningLead: 1m", "  warningLead: 1m\n  victoryWindow: 25s"));

        result.Problems.Should().BeEmpty();
        result.Config!.Clock.VictoryWindow.Should().Be(TimeSpan.FromSeconds(25));
    }

    [TestMethod]
    public void A_file_that_says_nothing_about_an_opening_opens_on_the_cycle()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Clock.OpeningLength.Should().Be(result.Config.Clock.CycleLength);
    }

    [TestMethod]
    public void An_opening_longer_than_the_cycle_is_the_pacing_the_event_is_authored_against()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace(
            "  warningLead: 1m", "  warningLead: 1m\n  openingLength: 10m"));

        result.Problems.Should().BeEmpty();
        result.Config!.Clock.OpeningLength.Should().Be(TimeSpan.FromMinutes(10));
        result.Config.Clock.CycleLength.Should().Be(TimeSpan.FromMinutes(5),
            "an opening is the first window, not a new cadence");
    }

    [TestMethod]
    public void An_opening_that_is_not_a_length_of_time_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace(
            "  warningLead: 1m", "  warningLead: 1m\n  openingLength: soon"));

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("match.openingLength");
    }

    [TestMethod]
    public void A_warning_that_would_be_due_before_the_match_began_is_refused()
    {
        TournamentConfigLoadResult result = Load(ValidConfig
            .Replace("  warningLead: 1m", "  warningLead: 2m\n  openingLength: 90s"));

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("match.warningLead");
    }

    [TestMethod]
    public void A_victory_window_that_is_not_a_length_of_time_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace(
            "  warningLead: 1m", "  warningLead: 1m\n  victoryWindow: a moment"));

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("match.victoryWindow");
    }

    [TestMethod]
    public void A_tournament_pushes_nowhere_unless_the_file_names_a_booth()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Telemetry.Enabled.Should().BeFalse();
    }

    [TestMethod]
    public void Naming_the_booth_is_the_whole_switch()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
  interval: 2s
  timeout: 4s
");

        result.Problems.Should().BeEmpty();
        result.Config!.Telemetry.Enabled.Should().BeTrue();
        result.Config.Telemetry.Url.Should().Be("http://booth.local:8080/ingest");
        result.Config.Telemetry.Interval.Should().Be(TimeSpan.FromSeconds(2));
        result.Config.Telemetry.Timeout.Should().Be(TimeSpan.FromSeconds(4));
    }

    [TestMethod]
    public void A_booth_named_with_a_url_that_is_not_one_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: booth.local:8080/ingest
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("telemetry.url");
    }

    [TestMethod]
    public void A_cadence_that_is_not_a_length_of_time_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
  interval: often
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("telemetry.interval");
    }

    [TestMethod]
    public void A_named_booth_may_run_this_tournaments_verbs_without_a_second_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
");

        result.Problems.Should().BeEmpty();
        result.Config!.Telemetry.AcceptsCommands.Should().BeTrue();
    }

    [TestMethod]
    public void A_server_can_feed_a_booth_and_refuse_to_be_driven_by_it()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
  acceptsCommands: false
");

        result.Problems.Should().BeEmpty();
        result.Config!.Telemetry.Enabled.Should().BeTrue();
        result.Config.Telemetry.AcceptsCommands.Should().BeFalse();
    }

    [TestMethod]
    public void The_console_channel_sits_beside_the_ingest_endpoint()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
");

        result.Config!.Telemetry.ConsoleUrl.Should().Be("http://booth.local:8080/console");
    }

    [TestMethod]
    public void A_booth_mounted_under_a_prefix_keeps_it()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: https://sn.caes.ar/lms/ingest
");

        result.Config!.Telemetry.ConsoleUrl.Should().Be("https://sn.caes.ar/lms/console");
    }

    [TestMethod]
    public void A_tournament_that_pushes_nowhere_has_no_console_channel_either()
    {
        Load(ValidConfig).Config!.Telemetry.ConsoleUrl.Should().BeNull();
    }

    [TestMethod]
    public void The_config_file_has_nowhere_to_write_the_push_token()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
telemetry:
  url: http://booth.local:8080/ingest
  token: hunter2
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("telemetry.token");
    }

    [TestMethod]
    public void A_group_the_arena_is_planned_for_defaults_to_the_size_of_a_match()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.GroupSize.Should().Be(5);
    }

    [TestMethod]
    public void A_bigger_group_can_be_planned_for_than_a_match_holds()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  ownPod: true
  minSeparation: 400
  groupSize: 8
");

        result.Problems.Should().BeEmpty();
        result.Config!.Spawn.GroupSize.Should().Be(8);
    }

    [TestMethod]
    public void A_group_of_nobody_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
spawn:
  groupSize: 0
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("spawn.groupSize");
    }

    [TestMethod]
    public void A_config_that_says_nothing_about_the_audit_still_samples_positions()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Audit.SamplesPositions.Should().BeTrue();
        result.Config.Audit.PositionInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void A_position_cadence_of_nothing_switches_sampling_off()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
audit:
  positionInterval: 0s
");

        result.Problems.Should().BeEmpty();
        result.Config!.Audit.SamplesPositions.Should().BeFalse();
    }

    [TestMethod]
    public void A_position_cadence_that_is_not_a_length_of_time_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
audit:
  positionInterval: sometimes
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("audit.positionInterval");
    }

    [TestMethod]
    public void A_position_cadence_that_runs_backwards_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
audit:
  positionInterval: -5s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("audit.positionInterval");
    }

    [TestMethod]
    public void A_misspelt_audit_field_is_named_rather_than_ignored()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
audit:
  positionsInterval: 5s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("audit.positionsInterval");
    }

    [TestMethod]
    public void A_config_with_no_disconnect_section_still_closes_the_free_escape()
    {
        TournamentConfigLoadResult result = Load(ValidConfig);

        result.Problems.Should().BeEmpty();
        result.Config!.Disconnect.Grace.Should().Be(TimeSpan.FromSeconds(90));
    }

    [TestMethod]
    public void A_grace_period_is_read_from_the_file()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
disconnect:
  grace: 45s
");

        result.Problems.Should().BeEmpty();
        result.Config!.Disconnect.Grace.Should().Be(TimeSpan.FromSeconds(45));
    }

    [TestMethod]
    public void A_grace_of_nothing_forgives_nothing()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
disconnect:
  grace: 0s
");

        result.Problems.Should().BeEmpty();
        result.Config!.Disconnect.Grace.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_grace_period_that_runs_backwards_is_reported_by_field_path()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
disconnect:
  grace: -30s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("disconnect.grace");
    }

    [TestMethod]
    public void A_misspelt_disconnect_field_is_named_rather_than_ignored()
    {
        TournamentConfigLoadResult result = Load(ValidConfig + @"
disconnect:
  gracePeriod: 30s
");

        result.Config.Should().BeNull();
        result.Problems.Should().ContainSingle().Which.Path.Should().Be("disconnect.gracePeriod");
    }


    [TestMethod]
    public void A_phase_that_closes_backwards_names_the_field_rather_than_throwing()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("shrinkFor: 90s", "shrinkFor: -90s"));

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Path == "zone.phases[0].shrinkFor");
    }

    [TestMethod]
    public void A_duration_that_is_not_a_number_at_all_names_the_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("cycleLength: 5m", "cycleLength: NaNs"));

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Path == "match.cycleLength");
    }

    [TestMethod]
    public void A_duration_too_long_to_hold_names_the_field()
    {
        TournamentConfigLoadResult result = Load(ValidConfig.Replace("cycleLength: 5m", "cycleLength: 1e30s"));

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Path == "match.cycleLength");
    }

    [TestMethod]
    public void A_valid_file_still_loads_every_duration_it_had()
    {
        Load(ValidConfig).Problems.Should().BeEmpty();

        foreach (string written in new[] { "90s", "5m", "1h", "00:04:30" })
        {
            Load(ValidConfig.Replace("cycleLength: 5m", "cycleLength: " + written))
                .Problems.Should().BeEmpty($"'{written}' is a length of time somebody would write");
        }

        foreach (string written in new[] { "800ms", "0.8s" })
        {
            Load(ValidConfig.Replace("damageInterval: 2s", "damageInterval: " + written))
                .Problems.Should().BeEmpty($"'{written}' is a length of time somebody would write");
        }
    }

    private static string WithColours(string body) =>
        ValidConfig.Replace("  damageInterval: 2s", "  damageInterval: 2s\n  colours:" + body);

    private static string WithStart(string body) =>
        ValidConfig.Replace("flags:", "start:\n" + body + "flags:");

    [TestMethod]
    public void A_start_section_that_says_nothing_about_vitals_gets_a_hundred_across_the_board()
    {
        Load(ValidConfig + "\nstart:\n  hold: 30s\n").Config!.Start.Vitals
            .Should().Be(new RoundVitals(100, 100, 100));
    }

    [TestMethod]
    public void Vitals_written_under_start_are_read_as_whole_percentages()
    {
        StartPolicy start = Load(WithStart("  vitals:\n    health: 85\n    food: 60\n    water: 40\n"))
            .Config!.Start;

        start.Vitals.Health.Should().Be(85);
        start.Vitals.Food.Should().Be(60);
        start.Vitals.Water.Should().Be(40);
    }

    [TestMethod]
    public void A_vital_below_one_is_refused_because_zero_is_dead()
    {
        TournamentConfigLoadResult result = Load(WithStart("  vitals:\n    health: 0\n"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "start.vitals.health");
    }

    [TestMethod]
    public void A_vital_over_a_hundred_is_refused()
    {
        TournamentConfigLoadResult result = Load(WithStart("  vitals:\n    water: 101\n"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "start.vitals.water");
    }

    [TestMethod]
    public void The_bottom_of_the_vitals_range_is_accepted()
    {
        Load(WithStart("  vitals:\n    food: 1\n")).Problems.Should().BeEmpty();
    }

    [TestMethod]
    public void A_vital_that_is_not_a_whole_number_is_refused()
    {
        TournamentConfigLoadResult result = Load(WithStart("  vitals:\n    health: 99.5\n"));

        result.Config.Should().BeNull();
        result.Problems.Should().Contain(problem => problem.Path == "start.vitals.health");
    }
}
