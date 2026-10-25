using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>The message telling a victim what it lost; the server supplies the number, the victim's client applies it.</summary>
[TestClass]
public class HurtBroadcastTests
{
    [TestMethod]
    public void A_hit_is_a_number_and_a_name()
    {
        new HurtBroadcast(20, "Mara").Encode().Should().Be("lms-hurt/1\tdamage=20\tby=Mara");
    }

    [TestMethod]
    public void A_hit_survives_the_round_trip()
    {
        HurtBroadcast.TryDecode(new HurtBroadcast(12.5, "Mara").Encode(), out HurtBroadcast hurt)
            .Should().BeTrue();

        hurt.Damage.Should().Be(12.5);
        hurt.By.Should().Be("Mara");
    }

    [TestMethod]
    public void A_name_from_somebody_else_s_keyboard_survives_the_round_trip()
    {
        HurtBroadcast.TryDecode(new HurtBroadcast(20, "Ma\tra\\x").Encode(), out HurtBroadcast hurt)
            .Should().BeTrue();

        hurt.By.Should().Be("Ma\tra\\x");
    }

    [TestMethod]
    public void A_hit_is_told_apart_from_the_zone_s_own_messages()
    {
        HurtBroadcast.IsHurt(new HurtBroadcast(20, "Mara").Encode()).Should().BeTrue();
        HurtBroadcast.IsHurt("lms-arrears/1\tgroup=1").Should().BeFalse();
        HurtBroadcast.IsHurt(null).Should().BeFalse();
    }

    [TestMethod]
    public void A_field_this_build_does_not_know_is_ignored()
    {
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=20\tby=Mara\tarm=claw", out HurtBroadcast hurt)
            .Should().BeTrue();

        hurt.Damage.Should().Be(20);
    }

    [TestMethod]
    public void A_version_this_build_does_not_know_is_refused()
    {
        HurtBroadcast.TryDecode("lms-hurt/2\tdamage=20\tby=Mara", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_hit_with_nobody_s_name_on_it_is_refused()
    {
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=20", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_hit_for_nothing_is_refused()
    {
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=0\tby=Mara", out _).Should().BeFalse();
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=-5\tby=Mara", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_damage_that_is_not_a_number_is_refused()
    {
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=lots\tby=Mara", out _).Should().BeFalse();
        HurtBroadcast.TryDecode("lms-hurt/1\tdamage=NaN\tby=Mara", out _).Should().BeFalse();
    }
}
