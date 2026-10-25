using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>Validation that rejects an unplayable squeeze in the config.</summary>
[TestClass]
public class ZoneScheduleValidatorTests
{
    private static readonly WorldPoint Spawn = new WorldPoint(0, -100, 0);
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(5);

    private static ZoneSchedule With(params ZonePhase[] phases) =>
        new ZoneSchedule(new ZoneSphere(Spawn, 1400), 1, phases);

    [TestMethod]
    public void A_squeeze_that_closes_steadily_inwards_is_valid()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(200, -150, 100), 400), TimeSpan.FromSeconds(90), 8));

        ZoneScheduleValidation result = ZoneScheduleValidator.Validate(schedule, Cycle);

        result.IsValid.Should().BeTrue();
    }

    [TestMethod]
    public void A_phase_whose_sphere_escapes_the_last_one_is_rejected()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(800, -100, 0), 400), TimeSpan.FromSeconds(90), 8));

        ZoneScheduleValidation result = ZoneScheduleValidator.Validate(schedule, Cycle);

        result.Problems.Should().ContainSingle()
            .Which.Kind.Should().Be(ZoneScheduleProblemKind.PhaseEscapesItsPredecessor);
    }

    [TestMethod]
    public void A_phase_that_does_not_shrink_is_rejected()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 8));

        ZoneScheduleValidator.Validate(schedule, Cycle).Problems
            .Should().Contain(p => p.Kind == ZoneScheduleProblemKind.PhaseDoesNotShrink);
    }

    [TestMethod]
    public void A_shrink_longer_than_the_cycle_is_rejected()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromMinutes(6), 3));

        ZoneScheduleValidator.Validate(schedule, Cycle).Problems
            .Should().Contain(p => p.Kind == ZoneScheduleProblemKind.ShrinkOutlastsTheCycle);
    }

    [TestMethod]
    public void Damage_that_does_not_escalate_is_rejected()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 8),
            new ZonePhase(2, new ZoneSphere(Spawn, 400), TimeSpan.FromSeconds(90), 3));

        ZoneScheduleValidator.Validate(schedule, Cycle).Problems
            .Should().Contain(p => p.Kind == ZoneScheduleProblemKind.DamageDoesNotEscalate);
    }

    [TestMethod]
    public void Every_problem_is_reported_in_one_pass()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 1500), TimeSpan.FromMinutes(6), 1),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(800, -100, 0), 900), TimeSpan.FromSeconds(90), 5));

        ZoneScheduleValidation result = ZoneScheduleValidator.Validate(schedule, Cycle);

        result.Problems.Select(p => p.Kind).Should().Contain(new[]
        {
            ZoneScheduleProblemKind.PhaseEscapesItsPredecessor,
            ZoneScheduleProblemKind.PhaseDoesNotShrink,
            ZoneScheduleProblemKind.ShrinkOutlastsTheCycle,
            ZoneScheduleProblemKind.DamageDoesNotEscalate,
        });
    }

    [TestMethod]
    public void A_phase_that_grows_is_only_reported_as_not_shrinking()
    {
        ZoneSchedule schedule = With(new ZonePhase(1, new ZoneSphere(Spawn, 1600), TimeSpan.FromSeconds(90), 3));

        ZoneScheduleValidator.Validate(schedule, Cycle).Problems
            .Should().ContainSingle().Which.Kind.Should().Be(ZoneScheduleProblemKind.PhaseDoesNotShrink);
    }

    [TestMethod]
    public void A_problem_says_which_phase_it_is_about()
    {
        ZoneSchedule schedule = With(
            new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
            new ZonePhase(2, new ZoneSphere(new WorldPoint(800, -100, 0), 400), TimeSpan.FromSeconds(90), 8));

        ZoneScheduleValidator.Validate(schedule, Cycle).Problems.Single()
            .ToString().Should().StartWith("phase 2:");
    }

    [TestMethod]
    public void The_synthesised_collapse_is_not_validated_as_if_somebody_wrote_it()
    {
        ZoneSchedule schedule = new ZoneSchedule(
            new ZoneSphere(Spawn, 1400), 1,
            new[]
            {
                new ZonePhase(1, new ZoneSphere(Spawn, 900), TimeSpan.FromSeconds(90), 3),
                new ZonePhase(2, new ZoneSphere(Spawn, 400), TimeSpan.FromSeconds(90), 8),
            });

        ZoneScheduleValidation result = ZoneScheduleValidator.Validate(schedule, TimeSpan.FromMinutes(5));

        result.Problems.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }
}
