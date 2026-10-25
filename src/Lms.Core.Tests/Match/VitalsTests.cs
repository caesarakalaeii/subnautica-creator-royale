using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>When a caster's nameplate colours a number.</summary>
[TestClass]
public class VitalsTests
{
    [TestMethod]
    public void Health_is_read_out_of_a_hundred()
    {
        Vitals.Health(100).Should().Be(VitalLevel.Fine);
        Vitals.Health(51).Should().Be(VitalLevel.Fine);
        Vitals.Health(50).Should().Be(VitalLevel.Low);
        Vitals.Health(26).Should().Be(VitalLevel.Low);
        Vitals.Health(25).Should().Be(VitalLevel.Critical);
        Vitals.Health(0).Should().Be(VitalLevel.Critical);
    }

    [TestMethod]
    public void Oxygen_is_read_in_seconds()
    {
        Vitals.Oxygen(45).Should().Be(VitalLevel.Fine);
        Vitals.Oxygen(31).Should().Be(VitalLevel.Fine);
        Vitals.Oxygen(30).Should().Be(VitalLevel.Low);
        Vitals.Oxygen(11).Should().Be(VitalLevel.Low);
        Vitals.Oxygen(10).Should().Be(VitalLevel.Critical);
        Vitals.Oxygen(0).Should().Be(VitalLevel.Critical);
    }

    [TestMethod]
    public void Forty_is_a_warning_about_health_and_nothing_about_air()
    {
        Vitals.Health(40).Should().Be(VitalLevel.Low);
        Vitals.Oxygen(40).Should().Be(VitalLevel.Fine);
    }

    [TestMethod]
    public void Below_nothing_is_still_critical()
    {
        Vitals.Health(-3).Should().Be(VitalLevel.Critical);
        Vitals.Oxygen(-1).Should().Be(VitalLevel.Critical);
    }

    [TestMethod]
    public void The_thresholds_are_the_booths_own()
    {
        Vitals.HealthCritical.Should().Be(25);
        Vitals.HealthLow.Should().Be(50);
        Vitals.OxygenCritical.Should().Be(10);
        Vitals.OxygenLow.Should().Be(30);
    }
}
