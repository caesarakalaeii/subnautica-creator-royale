using System;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

[TestClass]
public class MatchClockSettingsTests
{
    [TestMethod]
    [DataRow(0, DisplayName = "zero")]
    [DataRow(-1, DisplayName = "negative")]
    public void A_cycle_has_to_last_some_time(int minutes)
    {
        Action construct = () => new MatchClockSettings(TimeSpan.FromMinutes(minutes), TimeSpan.Zero);

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("cycleLength");
    }

    [TestMethod]
    public void A_warning_cannot_start_before_the_previous_cull_has_fired()
    {
        Action construct = () => new MatchClockSettings(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("warningLead");
    }

    [TestMethod]
    public void A_negative_warning_lead_is_rejected()
    {
        Action construct = () => new MatchClockSettings(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(-1));

        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("warningLead");
    }

    [TestMethod]
    public void A_zero_warning_lead_is_allowed()
    {
        MatchClockSettings settings = new MatchClockSettings(TimeSpan.FromMinutes(5), TimeSpan.Zero);

        settings.WarningLead.Should().Be(TimeSpan.Zero);
    }
}
