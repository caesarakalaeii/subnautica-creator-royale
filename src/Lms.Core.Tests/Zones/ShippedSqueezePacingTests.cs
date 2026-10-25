using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>Golden pacing figures for the shipped squeeze configs, as printed by <c>./scripts/check-zone</c>, so the Python transcription of <see cref="ZoneSqueeze.Toward"/> cannot drift unnoticed.</summary>
[TestClass]
public class ShippedSqueezePacingTests
{
    private static double FeltSpeed(ZoneSphere from, ZonePhase phase) =>
        (from.Radius - phase.Target.Radius + from.Centre.DistanceTo(phase.Target.Centre))
        / phase.ShrinkFor.TotalSeconds;

    private static ZoneSqueeze Squeeze(string file)
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText(file));
        result.Problems.Should().BeEmpty($"{file} is a file this repository ships");

        return result.Config!.Squeeze!;
    }

    private static List<double> Speeds(ZoneSqueeze squeeze, ZoneDestination destination)
    {
        ZoneSchedule schedule = squeeze.Toward(destination);
        List<double> speeds = new();
        ZoneSphere from = schedule.Opening;

        foreach (ZonePhase phase in schedule.Phases)
        {
            speeds.Add(FeltSpeed(from, phase));
            from = phase.Target;
        }

        return speeds;
    }

    private static double Fastest(ZoneSqueeze squeeze) =>
        squeeze.Destinations.SelectMany(destination => Speeds(squeeze, destination)).Max();

    [TestMethod]
    public void The_tournaments_own_squeeze_is_one_a_player_can_swim_out_of()
    {
        Fastest(Squeeze("tournament.playtest.yaml")).Should().BeApproximately(5.69, 0.01);
    }

    [TestMethod]
    public void The_documented_example_squeeze_is_one_a_player_can_swim_out_of()
    {
        Fastest(Squeeze("tournament.example.yaml")).Should().BeApproximately(3.89, 0.01);
    }

    [TestMethod]
    public void The_python_and_the_csharp_still_agree_about_where_the_wall_goes()
    {
        ZoneSqueeze squeeze = Squeeze("tournament.playtest.yaml");
        ZoneDestination ghostTree = squeeze.Destinations.Single(d => d.Name == "The Ghost Tree");

        Speeds(squeeze, ghostTree).Should().HaveCount(4);
        Speeds(squeeze, ghostTree).Zip(new[] { 4.65, 5.69, 4.55, 0.40 })
            .Should().AllSatisfy(pair => pair.First.Should().BeApproximately(pair.Second, 0.01));
    }

    [TestMethod]
    public void No_phase_of_the_tournament_puts_the_sea_surface_outside_the_wall_before_it_has_to()
    {
        ZoneSqueeze squeeze = Squeeze("tournament.playtest.yaml");

        foreach (ZoneDestination destination in squeeze.Destinations)
        {
            ZonePhase first = squeeze.Toward(destination).Phases[0];

            (first.Target.Centre.Y + first.Target.Radius).Should().BePositive(
                $"phase 1 toward {destination.Name} has to leave somewhere to breathe");
        }
    }

    [TestMethod]
    public void A_round_of_the_tournament_is_shut_by_the_end_of_its_slot()
    {
        TournamentConfig config = TournamentConfigLoader.Load(File.ReadAllText("tournament.playtest.yaml")).Config!;
        IReadOnlyList<ZonePhase> phases = config.SqueezeFor(1).Phases;

        TimeSpan shut = config.Clock.OpeningLength
                        + TimeSpan.FromTicks(config.Clock.CycleLength.Ticks * (phases.Count - 1))
                        + phases[phases.Count - 1].ShrinkFor;

        shut.Should().BeLessOrEqualTo(TimeSpan.FromMinutes(35));
    }

    [TestMethod]
    public void The_tournament_opens_for_ten_minutes_and_then_rests_between_five_minute_walls()
    {
        TournamentConfig config = TournamentConfigLoader.Load(File.ReadAllText("tournament.playtest.yaml")).Config!;

        config.Clock.OpeningLength.Should().Be(TimeSpan.FromMinutes(10));
        config.Clock.CycleLength.Should().Be(new TimeSpan(0, 6, 40));
        config.SqueezeFor(1).Phases.Should().AllSatisfy(phase => phase.ShrinkFor.Should().Be(TimeSpan.FromMinutes(5)));
    }
}
