using System;
using FluentAssertions;
using Lms.Core.Membranes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Membranes;

/// <summary>The operator-tunable ring settings, which are the game's own values written onto the field.</summary>
[TestClass]
public class RingPolicyTests
{
    [TestMethod]
    public void The_default_ring_is_the_rifle_the_game_ships()
    {
        RingPolicy policy = RingPolicy.Default;

        policy.Limits.MinRadius.Should().Be(1);
        policy.Limits.MaxRadius.Should().Be(10);
        policy.Limits.MinLifetime.Should().Be(TimeSpan.FromSeconds(4));
        policy.Limits.MaxLifetime.Should().Be(TimeSpan.FromSeconds(20));
        policy.PlacementMargin.Should().Be(1);
        policy.EnergyCost.Should().Be(5, "the rifle's own price for a full charge");
    }

    [TestMethod]
    public void A_ring_can_be_made_to_cost_more_than_the_rifle_asks()
    {
        new RingPolicy(StasisFieldLimits.GameDefaults, placementMargin: 1, energyCost: 25)
            .EnergyCost.Should().Be(25);
    }

    [TestMethod]
    public void A_ring_that_costs_nothing_is_refused()
    {
        Action free = () => new RingPolicy(StasisFieldLimits.GameDefaults, placementMargin: 1, energyCost: 0);

        free.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_ring_can_be_made_bigger_and_longer_than_the_rifle_ships_with()
    {
        StasisFieldLimits wide = new StasisFieldLimits(5, 40, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));

        RingPolicy policy = new RingPolicy(wide, placementMargin: 2, energyCost: 12);

        policy.Limits.MaxRadius.Should().Be(40);
        policy.PlacementMargin.Should().Be(2);
        policy.EnergyCost.Should().Be(12);
    }

    [TestMethod]
    public void A_margin_that_fills_the_biggest_ring_is_refused()
    {
        Action tooWide = () => new RingPolicy(StasisFieldLimits.GameDefaults, placementMargin: 10, energyCost: 5);

        tooWide.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*margin*");
    }

    [TestMethod]
    public void A_negative_margin_is_refused()
    {
        Action negative = () => new RingPolicy(StasisFieldLimits.GameDefaults, placementMargin: -1, energyCost: 5);

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_ring_needs_limits()
    {
        Action none = () => new RingPolicy(null!, placementMargin: 1, energyCost: 5);

        none.Should().Throw<ArgumentNullException>();
    }
}
