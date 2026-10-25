using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>A squeeze with a pool of destinations, one drawn per match; phase centres are derived so every sphere sits inside the last.</summary>
[TestClass]
public class ZoneSqueezeTests
{
    private static readonly WorldPoint Origin = new WorldPoint(0, -400, 0);

    private static readonly ZoneDestination Skull = new ZoneDestination("Lost River, near the skull", new WorldPoint(-731, -761, -272));
    private static readonly ZoneDestination Jelly = new ZoneDestination("Jellyshroom Cave", new WorldPoint(-632, -262, -93));
    private static readonly ZoneDestination Crash = new ZoneDestination("Crash Zone", new WorldPoint(1092, -91, 296));

    private static IReadOnlyList<ZonePhaseShape> Shapes() => new[]
    {
        new ZonePhaseShape(900, TimeSpan.FromSeconds(90), 3),
        new ZonePhaseShape(400, TimeSpan.FromSeconds(90), 8),
        new ZonePhaseShape(120, TimeSpan.FromSeconds(60), 20),
    };

    private static ZoneSqueeze Squeeze(params ZoneDestination[] destinations) =>
        new ZoneSqueeze(new ZoneSphere(Origin, 1400), 1, Shapes(), destinations);

    [TestMethod]
    public void Every_phase_sits_inside_the_one_before_it_whichever_destination_is_drawn()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        foreach (ZoneDestination destination in squeeze.Destinations)
        {
            ZoneSchedule schedule = squeeze.Toward(destination);

            ZoneSphere previous = schedule.Opening;
            foreach (ZonePhase phase in schedule.Phases)
            {
                previous.Contains(phase.Target)
                    .Should().BeTrue($"{phase.Number} of the squeeze toward {destination.Name} must stay inside {previous}");
                previous = phase.Target;
            }
        }
    }

    [TestMethod]
    public void The_validator_agrees_with_it_for_every_destination()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        foreach (ZoneDestination destination in squeeze.Destinations)
        {
            ZoneScheduleValidation validation =
                ZoneScheduleValidator.Validate(squeeze.Toward(destination), TimeSpan.FromMinutes(2));

            validation.IsValid.Should().BeTrue($"the squeeze toward {destination.Name} should be playable");
        }
    }

    [TestMethod]
    public void The_last_authored_phase_lands_on_the_destination()
    {
        ZoneSchedule schedule = Squeeze(Skull).Toward(Skull);

        schedule.Phases[schedule.AuthoredPhaseCount - 1].Target.Centre.Should().Be(Skull.Centre);
    }

    [TestMethod]
    public void The_collapse_closes_on_the_destination_too()
    {
        ZoneSchedule schedule = Squeeze(Skull).Toward(Skull);

        schedule.FinalCollapse.Target.Centre.Should().Be(Skull.Centre);
        schedule.FinalCollapse.Target.Radius.Should().Be(0);
    }

    [TestMethod]
    public void The_wall_moves_a_little_at_a_time_rather_than_all_at_once()
    {
        ZoneSchedule schedule = Squeeze(Skull).Toward(Skull);

        double[] steps = Steps(schedule).ToArray();

        steps.Should().HaveCount(3);
        steps.Should().AllSatisfy(step => step.Should().BeGreaterThan(0));
        steps[0].Should().BeGreaterThan(steps[2], "the widest phase has the most room to travel");
    }

    [TestMethod]
    public void A_destination_at_the_opening_centre_never_moves_the_wall()
    {
        ZoneDestination middle = new ZoneDestination("dead centre", Origin);

        ZoneSchedule schedule = Squeeze(middle).Toward(middle);

        Steps(schedule).Should().AllSatisfy(step => step.Should().Be(0));
    }

    [TestMethod]
    public void A_destination_the_final_sphere_could_not_reach_is_refused_by_the_pool()
    {
        ZoneDestination tooFar = new ZoneDestination("the void", new WorldPoint(0, -400, 1350));

        Action building = () => Squeeze(tooFar);

        building.Should().Throw<ArgumentException>().WithMessage("*the void*");
    }

    [TestMethod]
    public void The_reach_is_the_opening_less_the_final_sphere()
    {
        Squeeze(Jelly).Reach.Should().Be(1400 - 120);
    }

    [TestMethod]
    public void A_destination_exactly_at_the_limit_is_allowed()
    {
        ZoneDestination onTheEdge = new ZoneDestination("the edge", new WorldPoint(0, -400, 1280));

        Action building = () => Squeeze(onTheEdge);

        building.Should().NotThrow();
    }

    [TestMethod]
    public void Overhang_says_by_how_much_a_destination_misses()
    {
        ZoneSqueeze squeeze = Squeeze(Jelly);

        squeeze.Overhang(new ZoneDestination("100 m out", new WorldPoint(0, -400, 1380)))
            .Should().BeApproximately(100, 1e-9);
    }

    [TestMethod]
    public void A_pool_with_nothing_in_it_is_refused()
    {
        Action building = () => Squeeze();

        building.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Two_destinations_with_the_same_name_are_refused()
    {
        Action building = () => Squeeze(Jelly, new ZoneDestination("Jellyshroom Cave", new WorldPoint(1, -400, 1)));

        building.Should().Throw<ArgumentException>().WithMessage("*Jellyshroom Cave*");
    }

    [TestMethod]
    public void A_tournament_sees_every_destination_before_it_sees_one_twice()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        string[] drawn = Enumerable.Range(0, 3).Select(n => squeeze.Draw(n, seed: 7).Name).ToArray();

        drawn.Should().OnlyHaveUniqueItems();
        drawn.Should().BeEquivalentTo(new[] { Skull.Name, Jelly.Name, Crash.Name });
    }

    [TestMethod]
    public void The_pool_starts_again_once_it_is_exhausted()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        string[] second = Enumerable.Range(3, 3).Select(n => squeeze.Draw(n, seed: 7).Name).ToArray();

        second.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void The_second_pass_is_shuffled_differently_from_the_first()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        string[] first = Enumerable.Range(0, 3).Select(n => squeeze.Draw(n, seed: 7).Name).ToArray();
        string[] second = Enumerable.Range(3, 3).Select(n => squeeze.Draw(n, seed: 7).Name).ToArray();

        second.Should().NotEqual(first);
    }

    [TestMethod]
    public void The_same_seed_draws_the_same_tournament()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        Enumerable.Range(0, 6).Select(n => squeeze.Draw(n, seed: 31).Name)
            .Should().Equal(Enumerable.Range(0, 6).Select(n => squeeze.Draw(n, seed: 31).Name));
    }

    [TestMethod]
    public void A_different_seed_draws_a_different_tournament()
    {
        ZoneSqueeze squeeze = Squeeze(Skull, Jelly, Crash);

        IEnumerable<string> Order(int seed) => Enumerable.Range(0, 3).Select(n => squeeze.Draw(n, seed).Name).ToArray();

        Enumerable.Range(1, 40).Select(Order).Distinct(new SequenceComparer())
            .Should().HaveCountGreaterThan(1);
    }

    [TestMethod]
    public void A_pool_of_one_draws_it_every_time()
    {
        ZoneSqueeze squeeze = Squeeze(Jelly);

        Enumerable.Range(0, 4).Select(n => squeeze.Draw(n, seed: 7).Name)
            .Should().AllBe(Jelly.Name);
    }

    [TestMethod]
    public void A_draw_before_the_first_match_is_refused()
    {
        Action drawing = () => Squeeze(Jelly).Draw(-1, seed: 7);

        drawing.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static IEnumerable<double> Steps(ZoneSchedule schedule)
    {
        ZoneSphere previous = schedule.Opening;
        for (int i = 0; i < schedule.AuthoredPhaseCount; i++)
        {
            ZoneSphere target = schedule.Phases[i].Target;
            yield return previous.Centre.DistanceTo(target.Centre);
            previous = target;
        }
    }

    private sealed class SequenceComparer : IEqualityComparer<IEnumerable<string>>
    {
        public bool Equals(IEnumerable<string>? x, IEnumerable<string>? y) => x != null && y != null && x.SequenceEqual(y);

        public int GetHashCode(IEnumerable<string> obj) => string.Join("|", obj).GetHashCode();
    }
}
