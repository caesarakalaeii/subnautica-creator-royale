using System;
using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>How often a first aid kit may be used.</summary>
[TestClass]
public class MedKitCooldownTests
{
    private static readonly TimeSpan Half = TimeSpan.FromSeconds(30);

    private static MedKitCooldown Cooling() => new MedKitCooldown(Half);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [TestMethod]
    public void A_kit_can_be_used_before_any_has_been()
    {
        Cooling().IsReady(At(0)).Should().BeTrue();
    }

    [TestMethod]
    public void And_not_again_until_the_cooldown_is_spent()
    {
        MedKitCooldown cooldown = Cooling();
        cooldown.Used(At(10));

        cooldown.IsReady(At(10)).Should().BeFalse();
        cooldown.IsReady(At(39.9)).Should().BeFalse();
        cooldown.IsReady(At(40)).Should().BeTrue("thirty seconds after it was used");
    }

    [TestMethod]
    public void What_is_left_is_what_the_refusal_says()
    {
        MedKitCooldown cooldown = Cooling();
        cooldown.Used(At(10));

        cooldown.Remaining(At(25)).Should().Be(TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public void And_it_is_never_negative()
    {
        MedKitCooldown cooldown = Cooling();
        cooldown.Used(At(10));

        cooldown.Remaining(At(100)).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_cooldown_of_nothing_never_refuses_anything()
    {
        MedKitCooldown off = new MedKitCooldown(TimeSpan.Zero);

        off.IsEnforced.Should().BeFalse();

        off.Used(At(10));
        off.IsReady(At(10)).Should().BeTrue();
        off.Remaining(At(10)).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_cooldown_does_not_run_backwards()
    {
        Action backwards = () => new MedKitCooldown(TimeSpan.FromSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_new_match_clears_it_because_per_player_state_belongs_to_the_match()
    {
        MedKitCooldown cooldown = Cooling();
        cooldown.Used(At(200));

        cooldown.Ready();

        cooldown.IsReady(At(0)).Should().BeTrue("the clock started again at nothing");
    }

    [TestMethod]
    public void Using_one_twice_in_the_same_instant_still_only_starts_one_cooldown()
    {
        MedKitCooldown cooldown = Cooling();
        cooldown.Used(At(10));
        cooldown.Used(At(10));

        cooldown.IsReady(At(40)).Should().BeTrue();
    }
}
