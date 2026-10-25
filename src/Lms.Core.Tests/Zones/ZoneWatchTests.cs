using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>Per-tick damage for a player outside the wall, on a 2-second cadence with an escalating bite.</summary>
[TestClass]
public class ZoneWatchTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -100, 0);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(0.25);

    private static readonly WorldPoint WellInside = new WorldPoint(0, -100, 0);
    private static readonly WorldPoint JustOutside = new WorldPoint(0, -100, 501);
    private static readonly WorldPoint FarOutside = new WorldPoint(0, -100, 800);

    private static ZoneWatch NewWatch(double fullExposureAt = 150) =>
        new ZoneWatch(new ZonePolicy(Tick, fullExposureAt));

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Centre, 500), 4,
        new[] { new ZonePhase(1, new ZoneSphere(Centre, 200), TimeSpan.FromSeconds(90), 10) });

    private static ZoneState Zone() => Schedule().At(0, TimeSpan.Zero);

    private static double DamageOver(ZoneWatch watch, WorldPoint position, ZoneState zone, TimeSpan span)
    {
        double total = 0;
        for (TimeSpan spent = TimeSpan.Zero; spent < span; spent += Frame)
        {
            total += watch.Update(position, zone, Frame).Damage;
        }

        return total;
    }

    [TestMethod]
    public void A_player_inside_the_wall_is_safe_and_takes_nothing()
    {
        ZoneAssessment assessment = NewWatch().Update(WellInside, Zone(), Frame);

        assessment.Verdict.Should().Be(ZoneVerdict.Inside);
        assessment.Damage.Should().Be(0);
        assessment.Exposure.Should().Be(0);
        assessment.DistanceOutside.Should().Be(0);
    }

    [TestMethod]
    public void A_player_inside_the_wall_but_outside_the_next_one_is_warned()
    {
        ZoneAssessment assessment = NewWatch().Update(new WorldPoint(0, -100, 300), Zone(), Frame);

        assessment.Verdict.Should().Be(ZoneVerdict.Warning);
        assessment.Damage.Should().Be(0);
    }

    [TestMethod]
    public void A_player_outside_the_wall_takes_nothing_until_the_first_tick_falls_due()
    {
        ZoneWatch watch = NewWatch();

        DamageOver(watch, JustOutside, Zone(), TimeSpan.FromSeconds(1.5)).Should().Be(0);
    }

    [TestMethod]
    public void A_player_outside_the_wall_is_bitten_once_every_interval()
    {
        ZoneWatch watch = NewWatch();

        DamageOver(watch, JustOutside, Zone(), TimeSpan.FromSeconds(10)).Should().BeApproximately(20, 1e-9);
    }

    [TestMethod]
    public void The_interval_is_the_one_the_config_asked_for()
    {
        ZoneWatch watch = new ZoneWatch(new ZonePolicy(TimeSpan.FromSeconds(5), 150));

        DamageOver(watch, JustOutside, Zone(), TimeSpan.FromSeconds(10)).Should().BeApproximately(8, 1e-9);
    }

    [TestMethod]
    public void Damage_is_what_the_phase_says_it_is()
    {
        ZoneWatch watch = NewWatch();
        ZoneState phaseOne = Schedule().At(1, TimeSpan.FromSeconds(200));

        DamageOver(watch, FarOutside, phaseOne, TimeSpan.FromSeconds(4)).Should().BeApproximately(20, 1e-9);
    }

    [TestMethod]
    public void A_frame_that_covers_more_than_one_interval_pays_for_all_of_them()
    {
        ZoneWatch watch = NewWatch();

        watch.Update(JustOutside, Zone(), TimeSpan.FromSeconds(5)).Damage.Should().BeApproximately(8, 1e-9);
    }

    [TestMethod]
    public void A_frame_that_covers_more_than_one_interval_says_how_many_it_was()
    {
        ZoneWatch watch = NewWatch();

        ZoneAssessment assessment = watch.Update(JustOutside, Zone(), TimeSpan.FromSeconds(5));

        assessment.Ticks.Should().Be(2);
        assessment.DamagePerTick.Should().BeApproximately(4, 1e-9);
    }

    [TestMethod]
    public void A_frame_with_no_tick_in_it_bites_nobody()
    {
        ZoneAssessment assessment = NewWatch().Update(JustOutside, Zone(), Frame);

        assessment.Ticks.Should().Be(0);
        assessment.Damage.Should().Be(0);
    }

    [TestMethod]
    public void Nothing_inside_the_wall_has_a_tick_or_a_price()
    {
        ZoneAssessment assessment = NewWatch().Update(WellInside, Zone(), Frame);

        assessment.Ticks.Should().Be(0);
        assessment.DamagePerTick.Should().Be(0);
    }

    [TestMethod]
    public void Getting_back_inside_hands_back_a_full_interval()
    {
        ZoneWatch watch = NewWatch();

        DamageOver(watch, JustOutside, Zone(), TimeSpan.FromSeconds(1.5));
        watch.Update(WellInside, Zone(), Frame);

        DamageOver(watch, JustOutside, Zone(), TimeSpan.FromSeconds(1.5)).Should().Be(0);
    }

    [TestMethod]
    public void Exposure_rises_with_how_far_outside_the_player_is()
    {
        ZoneWatch watch = NewWatch(fullExposureAt: 150);

        watch.Update(new WorldPoint(0, -100, 575), Zone(), Frame).Exposure.Should().BeApproximately(0.5, 1e-9);
        watch.Update(new WorldPoint(0, -100, 650), Zone(), Frame).Exposure.Should().Be(1);
    }

    [TestMethod]
    public void Exposure_never_exceeds_one_however_far_out_the_player_swims()
    {
        NewWatch().Update(new WorldPoint(0, -100, 1900), Zone(), Frame).Exposure.Should().Be(1);
    }

    [TestMethod]
    public void A_player_one_metre_outside_is_already_showing_exposure()
    {
        NewWatch().Update(JustOutside, Zone(), Frame).Exposure.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public void The_assessment_says_how_far_out_and_how_long_until_the_next_bite()
    {
        ZoneWatch watch = NewWatch();

        ZoneAssessment assessment = watch.Update(FarOutside, Zone(), TimeSpan.FromSeconds(0.5));

        assessment.Verdict.Should().Be(ZoneVerdict.Outside);
        assessment.DistanceOutside.Should().BeApproximately(300, 1e-9);
        assessment.UntilNextTick.Should().Be(TimeSpan.FromSeconds(1.5));
    }

    [TestMethod]
    public void Depth_alone_puts_a_player_outside()
    {
        ZoneAssessment assessment = NewWatch().Update(new WorldPoint(0, -700, 0), Zone(), Frame);

        assessment.Verdict.Should().Be(ZoneVerdict.Outside);
        assessment.DistanceOutside.Should().BeApproximately(100, 1e-9);
    }

    [TestMethod]
    public void Match_time_does_not_run_backwards()
    {
        FluentActions.Invoking(() => NewWatch().Update(WellInside, Zone(), TimeSpan.FromSeconds(-1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Nobody_is_warned_about_a_next_sphere_that_has_no_inside()
    {
        ZoneSchedule schedule = new ZoneSchedule(
            new ZoneSphere(Centre, 1000), 1,
            new[] { new ZonePhase(1, new ZoneSphere(Centre, 500), TimeSpan.FromSeconds(30), 5) });

        ZoneState lastAuthoredPhase = schedule.At(1, TimeSpan.FromMinutes(1));
        lastAuthoredPhase.NextSphere!.Value.Radius.Should().Be(0, "the collapse is what comes next");

        ZoneAssessment assessment = NewWatch().Update(Centre, lastAuthoredPhase, Frame);

        assessment.Verdict.Should().Be(ZoneVerdict.Inside);
    }

    [TestMethod]
    public void A_closed_zone_bites_the_player_standing_on_its_centre()
    {
        ZoneSchedule schedule = new ZoneSchedule(
            new ZoneSphere(Centre, 1000), 1,
            new[] { new ZonePhase(1, new ZoneSphere(Centre, 500), TimeSpan.FromSeconds(30), 5) });

        ZoneState closed = schedule.At(2, TimeSpan.FromMinutes(5));

        ZoneAssessment assessment = NewWatch().Update(Centre, closed, Tick);

        assessment.Verdict.Should().Be(ZoneVerdict.Outside, "a zone with no inside shelters nobody");
        assessment.Damage.Should().Be(5);
    }
}
