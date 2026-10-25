using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>One phase's geometry without a clock: start, destination, duration and cost.</summary>
[TestClass]
public class ZoneShrinkTests
{
    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);
    private static readonly WorldPoint Second = new WorldPoint(200, -150, 100);
    private static readonly WorldPoint Third = new WorldPoint(250, -160, 120);

    private static ZoneSchedule ThreePhases() => new ZoneSchedule(
        new ZoneSphere(Spawn, 1400), 1,
        new[]
        {
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(Second, 400), TimeSpan.FromSeconds(90), 8),
            new ZonePhase(3, new ZoneSphere(Third, 120), TimeSpan.FromSeconds(60), 20),
        });

    [TestMethod]
    public void Before_the_first_phase_the_wall_is_the_opening_sphere_going_nowhere()
    {
        ZoneShrink shrink = ThreePhases().ShrinkAt(0);

        shrink.PhaseNumber.Should().Be(0);
        shrink.From.Should().Be(new ZoneSphere(Spawn, 1400));
        shrink.Target.Should().Be(new ZoneSphere(Spawn, 1400));
        shrink.ShrinkFor.Should().Be(TimeSpan.Zero);
        shrink.DamagePerTick.Should().Be(1, "outside the opening sphere still costs something");
        shrink.NextTarget.Should().Be(new ZoneSphere(Spawn, 900));
    }

    [TestMethod]
    public void A_phase_says_where_the_wall_comes_from_and_where_it_is_going()
    {
        ZoneShrink shrink = ThreePhases().ShrinkAt(2);

        shrink.PhaseNumber.Should().Be(2);
        shrink.From.Should().Be(new ZoneSphere(Spawn, 900), "phase one's target is phase two's start");
        shrink.Target.Should().Be(new ZoneSphere(Second, 400));
        shrink.ShrinkFor.Should().Be(TimeSpan.FromSeconds(90));
        shrink.DamagePerTick.Should().Be(8);
        shrink.NextTarget.Should().Be(new ZoneSphere(Third, 120), "the ghost sphere the HUD draws");
    }

    [TestMethod]
    public void The_last_phase_has_nowhere_after_it()
    {
        ZoneShrink collapse = ThreePhases().ShrinkAt(4);

        collapse.Target.Should().Be(new ZoneSphere(Third, 0));
        collapse.NextTarget.Should().BeNull();
    }

    [TestMethod]
    public void Asking_past_the_end_of_the_schedule_holds_the_closed_sphere()
    {
        ThreePhases().ShrinkAt(9).Should().Be(ThreePhases().ShrinkAt(4));
    }

    [TestMethod]
    public void A_shrink_asked_where_the_wall_is_agrees_with_the_schedule()
    {
        ZoneSchedule schedule = ThreePhases();

        foreach (int phase in new[] { 0, 1, 2, 3, 4, 7 })
        {
            foreach (double seconds in new[] { 0d, 1, 45, 89.9, 90, 300 })
            {
                TimeSpan since = TimeSpan.FromSeconds(seconds);

                schedule.ShrinkAt(phase).At(since).Should().BeEquivalentTo(schedule.At(phase, since),
                    $"phase {phase} at {seconds}s");
            }
        }
    }

    [TestMethod]
    public void A_shrink_does_not_run_backwards()
    {
        Action asking = () => ThreePhases().ShrinkAt(1).At(TimeSpan.FromSeconds(-1));

        asking.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_match_cannot_have_un_fired_phases()
    {
        Action asking = () => ThreePhases().ShrinkAt(-1);

        asking.Should().Throw<ArgumentOutOfRangeException>();
    }
}
