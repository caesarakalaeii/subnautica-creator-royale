using FluentAssertions;
using Lms.Core.Combat;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>The attacker's strike report message, told apart on the report envelope by its header.</summary>
[TestClass]
public class PrawnStrikeReportTests
{
    [TestMethod]
    public void A_swing_is_a_victim_and_an_arm_and_nothing_else()
    {
        StrikeReport.Encode(7, StrikeWeapon.Claw).Should().Be("lms-strike/1\tvictim=7\tarm=claw");
    }

    [TestMethod]
    public void A_swing_survives_the_round_trip()
    {
        string wire = StrikeReport.Encode(19, StrikeWeapon.Drill);

        StrikeReport.TryDecode(wire, out ushort victim, out StrikeWeapon arm).Should().BeTrue();
        victim.Should().Be(19);
        arm.Should().Be(StrikeWeapon.Drill);
    }

    [TestMethod]
    public void A_swing_is_told_apart_from_the_two_messages_already_on_the_envelope()
    {
        StrikeReport.IsStrike(StrikeReport.Encode(1, StrikeWeapon.Claw)).Should().BeTrue();

        StrikeReport.IsStrike(AdminRequest.Encode(AdminCommand.Start)).Should().BeFalse();
        StrikeReport.IsStrike(null).Should().BeFalse();
        StrikeReport.IsStrike("  ").Should().BeFalse();
    }

    [TestMethod]
    public void A_field_this_build_does_not_know_is_ignored()
    {
        StrikeReport
            .TryDecode("lms-strike/1\tvictim=4\tarm=claw\tforce=hard", out ushort victim, out StrikeWeapon arm)
            .Should().BeTrue();

        victim.Should().Be(4);
        arm.Should().Be(StrikeWeapon.Claw);
    }

    [TestMethod]
    public void A_version_this_build_does_not_know_is_refused()
    {
        StrikeReport.IsStrike("lms-strike/2\tvictim=4\tarm=claw").Should().BeFalse();
        StrikeReport.TryDecode("lms-strike/2\tvictim=4\tarm=claw", out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void An_arm_this_build_does_not_know_is_refused()
    {
        StrikeReport.TryDecode("lms-strike/1\tvictim=4\tarm=torpedo", out _, out _)
            .Should().BeFalse();
    }

    [TestMethod]
    public void A_swing_that_names_nobody_is_refused()
    {
        StrikeReport.TryDecode("lms-strike/1\tarm=claw", out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_swing_with_no_arm_on_it_is_refused()
    {
        StrikeReport.TryDecode("lms-strike/1\tvictim=4", out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_victim_that_is_not_a_player_id_is_refused()
    {
        StrikeReport.TryDecode("lms-strike/1\tvictim=nigel\tarm=claw", out _, out _).Should().BeFalse();
        StrikeReport.TryDecode("lms-strike/1\tvictim=-3\tarm=claw", out _, out _).Should().BeFalse();
        StrikeReport.TryDecode("lms-strike/1\tvictim=99999999\tarm=claw", out _, out _).Should().BeFalse();
    }
}
