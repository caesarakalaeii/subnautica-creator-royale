using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>
/// Wire-compatibility tripwire for <c>lms-strike/1</c>: every expected string is a literal so a diff here is a deliberate decision.
/// Blades reuse the <c>arm=</c> field with new values rather than a new header, so older builds fail closed only for the weapons they do not know.
/// </summary>
[TestClass]
public class StrikeReportCompatibilityTests
{
    [TestMethod]
    public void The_header_is_the_one_that_shipped()
    {
        StrikeReport.WireHeader.Should().Be("lms-strike/1",
            "a version bump costs a rolling install real swings -- see this file's remarks");
    }

    [TestMethod]
    public void A_claw_is_still_exactly_the_bytes_LMS_76_shipped()
    {
        StrikeReport.Encode(7, StrikeWeapon.Claw).Should().Be("lms-strike/1\tvictim=7\tarm=claw");
        StrikeReport.Encode(7, StrikeWeapon.Drill).Should().Be("lms-strike/1\tvictim=7\tarm=drill");
    }

    [TestMethod]
    public void A_blade_is_a_new_value_in_the_field_that_was_already_there()
    {
        StrikeReport.Encode(7, StrikeWeapon.Knife).Should().Be("lms-strike/1\tvictim=7\tarm=knife");
        StrikeReport.Encode(7, StrikeWeapon.HeatBlade).Should().Be("lms-strike/1\tvictim=7\tarm=heatblade");
    }

    [TestMethod]
    public void Every_weapon_word_is_still_exactly_itself()
    {
        StrikeWeapons.Word(StrikeWeapon.Claw).Should().Be("claw");
        StrikeWeapons.Word(StrikeWeapon.Drill).Should().Be("drill");
        StrikeWeapons.Word(StrikeWeapon.Knife).Should().Be("knife");
        StrikeWeapons.Word(StrikeWeapon.HeatBlade).Should().Be("heatblade");
    }

    [TestMethod]
    public void Every_refusal_word_LMS_76_shipped_is_still_exactly_itself()
    {
        StrikeRefusals.Word(StrikeRefusal.None).Should().Be("none");
        StrikeRefusals.Word(StrikeRefusal.PrawnsAreHarmless).Should().Be("harmless");
        StrikeRefusals.Word(StrikeRefusal.ArmIsDisabled).Should().Be("arm-off");
        StrikeRefusals.Word(StrikeRefusal.MatchIsNotRunning).Should().Be("not-running");
        StrikeRefusals.Word(StrikeRefusal.TheRingHasYou).Should().Be("held");
        StrikeRefusals.Word(StrikeRefusal.NotInAPrawn).Should().Be("no-prawn");
        StrikeRefusals.Word(StrikeRefusal.TheyAreAlreadyOut).Should().Be("already-out");
        StrikeRefusals.Word(StrikeRefusal.OutOfRange).Should().Be("out-of-range");
        StrikeRefusals.Word(StrikeRefusal.TooSoon).Should().Be("too-soon");
    }

    [TestMethod]
    public void The_refusals_LMS_107_added_are_new_words_beside_them()
    {
        StrikeRefusals.Word(StrikeRefusal.BladesAreHarmless).Should().Be("blades-harmless");
        StrikeRefusals.Word(StrikeRefusal.BladeIsDisabled).Should().Be("blade-off");
        StrikeRefusals.Word(StrikeRefusal.NotHoldingThatBlade).Should().Be("no-blade");
        StrikeRefusals.Word(StrikeRefusal.YouAreInAVehicle).Should().Be("in-a-vehicle");
    }

    [TestMethod]
    public void A_version_this_build_does_not_know_is_refused()
    {
        StrikeReport.IsStrike("lms-strike/2\tvictim=7\tarm=knife").Should().BeFalse();
        StrikeReport.TryDecode("lms-strike/2\tvictim=7\tarm=knife", out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_weapon_this_build_does_not_know_is_refused()
    {
        StrikeReport.TryDecode("lms-strike/1\tvictim=7\tarm=torpedo", out _, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_swing_from_a_build_that_has_blades_still_reads_on_one_that_does_not_care()
    {
        StrikeReport
            .TryDecode("lms-strike/1\tvictim=4\tarm=heatblade\tforce=hard", out ushort victim,
                out StrikeWeapon weapon)
            .Should().BeTrue();

        victim.Should().Be(4);
        weapon.Should().Be(StrikeWeapon.HeatBlade);
    }
}
