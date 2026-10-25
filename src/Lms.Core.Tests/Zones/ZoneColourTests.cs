using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>Hex colour parsing for the wall colour.</summary>
[TestClass]
public class ZoneColourTests
{
    [TestMethod]
    public void Six_digits_are_an_opaque_colour()
    {
        ZoneColour.TryParse("#FFD65C", out ZoneColour colour).Should().BeTrue();

        colour.Should().Be(new ZoneColour(0xFF, 0xD6, 0x5C));
        colour.Alpha.Should().Be(255, "a colour written without an alpha is the solid one");
    }

    [TestMethod]
    public void Eight_digits_carry_the_alpha_as_well()
    {
        ZoneColour.TryParse("#73D9FF47", out ZoneColour colour).Should().BeTrue();

        colour.Should().Be(new ZoneColour(0x73, 0xD9, 0xFF, 0x47));
    }

    [TestMethod]
    public void The_hash_is_optional()
    {
        ZoneColour.TryParse("FFD65C", out ZoneColour colour).Should().BeTrue();

        colour.Should().Be(new ZoneColour(0xFF, 0xD6, 0x5C));
    }

    [TestMethod]
    public void Case_and_surrounding_space_do_not_matter()
    {
        ZoneColour.TryParse("  #ffd65c  ", out ZoneColour colour).Should().BeTrue();

        colour.Should().Be(new ZoneColour(0xFF, 0xD6, 0x5C));
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty, which is what an unquoted #hex leaves behind")]
    [DataRow("   ")]
    [DataRow("gold", DisplayName = "a name, because there is no name table")]
    [DataRow("#FFD", DisplayName = "shorthand, which is not accepted")]
    [DataRow("#FFD65", DisplayName = "five digits")]
    [DataRow("#FFD65C4", DisplayName = "seven digits")]
    [DataRow("#FFD65C447", DisplayName = "nine digits")]
    [DataRow("#GGD65C", DisplayName = "not hex at all")]
    [DataRow("#FFD6 5C", DisplayName = "a space in the middle")]
    public void Anything_that_is_not_a_colour_is_refused(string written)
    {
        ZoneColour.TryParse(written, out ZoneColour colour).Should().BeFalse();

        colour.Should().Be(default(ZoneColour));
    }

    [TestMethod]
    public void It_reads_back_the_way_it_was_written()
    {
        ZoneColour.TryParse("#73D9FF47", out ZoneColour colour);

        colour.ToString().Should().Be("#73D9FF47");
    }

    [TestMethod]
    public void The_defaults_are_a_solid_wall_and_a_ghost_you_can_almost_see_through()
    {
        ZoneColours.Default.Now.Should().Be(new ZoneColour(0xFF, 0xD6, 0x5C));
        ZoneColours.Default.Next.Should().Be(new ZoneColour(0x7C, 0xFF, 0x5C, 0x1A));
        ZoneColours.Default.Next.Alpha.Should().BeLessThan((byte)(ZoneColours.Default.Now.Alpha / 2));
    }
}
