using System;
using FluentAssertions;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The setting that decides whether the booth is pushed to at all, and its validation.</summary>
[TestClass]
public class TelemetryPolicyTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [TestMethod]
    public void A_tournament_that_names_no_booth_pushes_nowhere()
    {
        TelemetryPolicy.Off.Enabled.Should().BeFalse();
        TelemetryPolicy.Off.Url.Should().BeNull();
    }

    [TestMethod]
    public void Naming_a_booth_is_all_it_takes_to_start_pushing()
    {
        new TelemetryPolicy("http://booth:8080/ingest", Second, Second).Enabled.Should().BeTrue();
    }

    [TestMethod]
    public void A_url_that_is_not_one_is_refused_rather_than_pushed_at()
    {
        Action written = () => new TelemetryPolicy("booth:8080/ingest", Second, Second);

        written.Should().Throw<ArgumentException>().And.ParamName.Should().Be("url");
    }

    [TestMethod]
    public void A_url_that_is_not_http_is_refused()
    {
        Action written = () => new TelemetryPolicy("ws://booth:8080/ingest", Second, Second);

        written.Should().Throw<ArgumentException>().And.ParamName.Should().Be("url");
    }

    [TestMethod]
    public void An_interval_of_nothing_is_not_a_cadence()
    {
        Action written = () => new TelemetryPolicy("http://booth/ingest", TimeSpan.Zero, Second);

        written.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("interval");
    }

    [TestMethod]
    public void A_timeout_of_nothing_would_refuse_every_push_before_it_left()
    {
        Action written = () => new TelemetryPolicy("http://booth/ingest", Second, TimeSpan.Zero);

        written.Should().Throw<ArgumentOutOfRangeException>().And.ParamName.Should().Be("timeout");
    }

    [TestMethod]
    public void The_default_cadence_is_the_one_the_contract_is_costed_against()
    {
        TelemetryPolicy.Off.Interval.Should().Be(Second);
    }
}
