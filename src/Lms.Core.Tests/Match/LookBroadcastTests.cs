using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Where a caster's camera is being sent.</summary>
[TestClass]
public class LookBroadcastTests
{
    private static LookBroadcast Read(string line)
    {
        LookBroadcast.TryDecode(line, out LookBroadcast look).Should().BeTrue();

        return look;
    }

    [TestMethod]
    public void Who_and_where_survive_the_wire()
    {
        LookBroadcast sent = new LookBroadcast("Vela", new WorldPoint(123.456, -42.1, 88.9));

        LookBroadcast read = Read(sent.Encode());

        read.Name.Should().Be("Vela");
        read.Where.X.Should().BeApproximately(123.456, 0.0005);
        read.Where.Y.Should().BeApproximately(-42.1, 0.0005);
        read.Where.Z.Should().BeApproximately(88.9, 0.0005);
    }

    [TestMethod]
    public void A_position_is_written_in_no_particular_locale()
    {
        new LookBroadcast("Vela", new WorldPoint(0.5, -1.25, 2))
            .Encode().Should().Contain("x=0.5").And.Contain("y=-1.25");
    }

    [TestMethod]
    public void The_origin_is_a_real_place_and_survives_as_one()
    {
        LookBroadcast read = Read(new LookBroadcast("Vela", new WorldPoint(0, 0, 0)).Encode());

        read.Where.Should().Be(new WorldPoint(0, 0, 0));
    }

    [TestMethod]
    public void A_name_with_a_tab_in_it_cannot_eat_the_coordinates()
    {
        LookBroadcast read = Read(new LookBroadcast("Ve\tla", new WorldPoint(1, 2, 3)).Encode());

        read.Name.Should().Be("Ve la");
        read.Where.Should().Be(new WorldPoint(1, 2, 3));
    }

    [TestMethod]
    public void A_line_that_is_not_a_look_is_declined_rather_than_half_read()
    {
        LookBroadcast.TryDecode("lms-ghost/1\tghost=1", out _).Should().BeFalse();
        LookBroadcast.TryDecode("", out _).Should().BeFalse();
        LookBroadcast.TryDecode(null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_look_missing_an_axis_is_declined_rather_than_placed_at_a_guess()
    {
        LookBroadcast.TryDecode("lms-look/1\tname=Vela\tx=1\ty=2", out _).Should().BeFalse();
        LookBroadcast.TryDecode("lms-look/1\tx=1\ty=2\tz=3", out _).Should().BeFalse();
    }

    [TestMethod]
    public void It_is_told_apart_from_the_other_nine_without_being_read()
    {
        LookBroadcast.IsLook(new LookBroadcast("Vela", new WorldPoint(1, 2, 3)).Encode())
            .Should().BeTrue();
        LookBroadcast.IsLook("lms-ghost/1\tghost=1").Should().BeFalse();
    }
}
