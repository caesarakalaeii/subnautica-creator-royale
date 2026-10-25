using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Where everybody stood, sampled on a cadence.</summary>
[TestClass]
public class PositionSamplingTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(200, -15, 100), 1400);
    private static readonly Regex Cadence = new Regex(@"positionInterval: *\S+");

    private static MatchHost NewHost(TimeSpan? interval = null)
    {
        string yaml = File.ReadAllText("tournament.test.yaml");

        if (interval.HasValue)
        {
            yaml = Cadence.Replace(yaml, $"positionInterval: {interval.Value.TotalSeconds:0.###}s");
            yaml.Should().Contain($"positionInterval: {interval.Value.TotalSeconds:0.###}s");
        }

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(yaml);
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static IReadOnlyList<PlayerTelemetry> Rows(bool alive = true) =>
        new PlayerRoster().Update(new[]
        {
            new PlayerReading(1, "Vela", new WorldPoint(210, -18, 90), alive, 92, 40, 70, 65)
        }, Wall).Rows;

    private static IReadOnlyList<AuditRecord> Positions(MatchHost host) =>
        host.Audit.Records.Where(record => record.Kind == AuditRecordKind.PlayerPosition).ToList();

    private static void Run(MatchHost host, int seconds, bool alive = true)
    {
        for (int second = 1; second <= seconds; second++)
        {
            TimeSpan now = Noon + TimeSpan.FromSeconds(second);
            host.Advance(now);
            host.Sample(now, Rows(alive));
        }
    }

    [TestMethod]
    public void A_match_nobody_has_started_records_nowhere_anybody_stood()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));

        Run(host, 30);

        Positions(host).Should().BeEmpty();
    }

    [TestMethod]
    public void A_live_match_samples_on_the_cadence_and_not_on_the_tick()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));
        host.Command("start", Noon);

        Run(host, 20);

        Positions(host).Should().HaveCount(4);
    }

    [TestMethod]
    public void A_sample_says_where_somebody_stood_and_what_they_had_left()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));
        host.Command("start", Noon);

        Run(host, 5);

        AuditRecord sample = Positions(host).Should().ContainSingle().Subject;

        sample.Player.Should().Be("Vela");
        sample.Position!.Value.X.Should().Be(210);
        sample.Position!.Value.Y.Should().Be(-18);
        sample.Health.Should().Be(92);
    }

    [TestMethod]
    public void The_first_tick_of_a_live_match_writes_where_everybody_started()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));
        host.Command("start", Noon);

        Run(host, 1);

        Positions(host).Should().ContainSingle().Which.Elapsed.Should().Be(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void Nobody_the_match_is_finished_with_is_sampled()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));
        host.Command("start", Noon);

        Run(host, 20, alive: false);

        Positions(host).Should().BeEmpty();
    }

    [TestMethod]
    public void A_cadence_of_nothing_switches_sampling_off()
    {
        MatchHost host = NewHost(TimeSpan.Zero);
        host.Command("start", Noon);

        Run(host, 60);

        Positions(host).Should().BeEmpty();
    }

    [TestMethod]
    public void A_config_that_says_nothing_still_samples()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Run(host, (int)AuditPolicy.Default.PositionInterval.TotalSeconds);

        Positions(host).Should().ContainSingle();
    }

    [TestMethod]
    public void A_pause_stops_the_cadence_rather_than_storing_it_up()
    {
        MatchHost host = NewHost(TimeSpan.FromSeconds(5));
        host.Command("start", Noon);
        Run(host, 5);
        host.Command("pause", Noon + TimeSpan.FromSeconds(6));

        for (int second = 7; second <= 60; second++)
        {
            TimeSpan now = Noon + TimeSpan.FromSeconds(second);
            host.Advance(now);
            host.Sample(now, Rows());
        }

        Positions(host).Should().ContainSingle();
    }
}
