using System;
using FluentAssertions;
using Lms.Core.Membranes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>Stasis rifle cooldown: <c>StasisRifle.sphere</c> is static, so a second shot moves the first ring; the cooldown lasts the life of the field.</summary>
[TestClass]
public class RifleCooldownTests
{
    private static readonly StasisFieldLimits Limits = StasisFieldLimits.GameDefaults;

    private static RifleCooldown Cooldown() => new RifleCooldown(Limits);

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [TestMethod]
    public void A_rifle_nobody_has_fired_is_ready()
    {
        Cooldown().IsReady(At(0)).Should().BeTrue();
    }

    [TestMethod]
    public void A_full_charge_locks_the_rifle_for_the_life_of_the_ring_it_leaves()
    {
        RifleCooldown cooldown = Cooldown();
        cooldown.Fired(At(100), charge: 1);

        cooldown.IsReady(At(119)).Should().BeFalse();
        cooldown.IsReady(At(120)).Should().BeTrue();
    }

    [TestMethod]
    public void A_tap_locks_it_only_as_long_as_the_ring_a_tap_makes()
    {
        RifleCooldown cooldown = Cooldown();
        cooldown.Fired(At(100), charge: 0);

        cooldown.IsReady(At(103)).Should().BeFalse();
        cooldown.IsReady(At(104)).Should().BeTrue();
    }

    [TestMethod]
    public void What_is_left_of_the_lock_counts_down_and_stops_at_nothing()
    {
        RifleCooldown cooldown = Cooldown();
        cooldown.Fired(At(100), charge: 1);

        cooldown.Remaining(At(110)).Should().Be(At(10));
        cooldown.Remaining(At(200)).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_reset_gives_the_rifle_back()
    {
        RifleCooldown cooldown = Cooldown();
        cooldown.Fired(At(100), charge: 1);

        cooldown.Ready();

        cooldown.IsReady(At(101)).Should().BeTrue();
    }
}
