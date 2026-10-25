using System;
using System.Globalization;
using FluentAssertions;
using Lms.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Diagnostics;

/// <summary>The timestamp every log line carries.</summary>
[TestClass]
public class LogStampTests
{
    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 10, 24, 12, 34, 56, 789, TimeSpan.Zero);

    [TestMethod]
    public void It_writes_the_time_in_UTC_because_a_local_one_cannot_be_interleaved()
    {
        LogStamp.Prefix(Noon, null).Should().Be("[LMS 12:34:56.789Z]");
    }

    [TestMethod]
    public void An_offset_is_converted_rather_than_dropped()
    {
        LogStamp.Prefix(Noon.ToOffset(TimeSpan.FromHours(2)), null)
            .Should().Be("[LMS 12:34:56.789Z]");
    }

    [TestMethod]
    public void And_the_world_clock_beside_it_because_that_is_the_one_nobody_can_drift_from()
    {
        LogStamp.Prefix(Noon, TimeSpan.FromSeconds(1234.5))
            .Should().Be("[LMS 12:34:56.789Z w1234.5]");
    }

    [TestMethod]
    public void There_is_no_world_clock_in_the_main_menu_so_the_field_goes_rather_than_reading_zero()
    {
        LogStamp.Prefix(Noon, null).Should().NotContain("w");
    }

    [TestMethod]
    public void A_long_lived_world_still_fits_on_one_line()
    {
        LogStamp.Prefix(Noon, TimeSpan.FromDays(3)).Should().Be("[LMS 12:34:56.789Z w259200.0]");
    }

    [TestMethod]
    public void Every_number_in_it_is_invariant_because_that_is_the_whole_job()
    {
        CultureInfo was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            LogStamp.Prefix(Noon, TimeSpan.FromSeconds(1234.5))
                .Should().Be("[LMS 12:34:56.789Z w1234.5]");
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [TestMethod]
    public void A_world_clock_that_runs_backwards_is_written_rather_than_hidden()
    {
        LogStamp.Prefix(Noon, TimeSpan.FromSeconds(-1)).Should().Be("[LMS 12:34:56.789Z w-1.0]");
    }
}
