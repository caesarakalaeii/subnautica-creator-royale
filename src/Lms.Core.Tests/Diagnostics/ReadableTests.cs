using System.Globalization;
using System.Threading;
using FluentAssertions;
using Lms.Core.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Diagnostics;

/// <summary>Human-readable log lines stay readable under a decimal-comma locale.</summary>
[TestClass]
public class ReadableTests
{
    private CultureInfo? restore;

    private static CultureInfo DecimalComma()
    {
        CultureInfo comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        return comma;
    }

    [TestInitialize]
    public void ReadTheLogInAnotherLocale()
    {
        restore = CultureInfo.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = DecimalComma();
    }

    [TestCleanup]
    public void PutTheLocaleBack() => Thread.CurrentThread.CurrentCulture = restore!;

    [TestMethod]
    public void The_locale_this_test_runs_in_really_does_write_a_comma()
    {
        $"{40.2:0.#}".Should().Be("40,2");
    }

    [TestMethod]
    public void A_reading_keeps_its_decimal_point_wherever_it_was_written()
    {
        Readable.Line($"health is still {40.2:0.#}").Should().Be("health is still 40.2");
    }

    [TestMethod]
    public void A_vector_stays_two_numbers_rather_than_becoming_four()
    {
        Readable.Line($"look=({-0.68:0.##},{-0.23:0.##})").Should().Be("look=(-0.68,-0.23)");
    }

    [TestMethod]
    public void A_number_with_no_format_of_its_own_is_invariant_too()
    {
        Readable.Line($"sampling biomes every {0.25}s").Should().Be("sampling biomes every 0.25s");
    }

    [TestMethod]
    public void A_line_assembled_from_fragments_is_invariant_all_the_way_through()
    {
        Readable.Line(
                $"rings are {10.5:0.#}m for ",
                $"{2.25:0.##}s at a tap")
            .Should().Be("rings are 10.5m for 2.25s at a tap");
    }

    [TestMethod]
    public void Nothing_is_inserted_between_fragments_or_around_a_line()
    {
        Readable.Line($"a", $"b", $"c").Should().Be("abc");
        Readable.Line($"plain text").Should().Be("plain text");
    }

    [TestMethod]
    public void A_line_with_no_numbers_in_it_is_left_exactly_as_it_was_written()
    {
        Readable.Line($"the ring is gone").Should().Be("the ring is gone");
    }
}
