using FluentAssertions;
using Lms.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Settings;

/// <summary>Who may see a beacon; beacons render through terrain at any range, so they are restricted.</summary>
[TestClass]
public class BeaconRuleTests
{
    [TestMethod]
    public void A_contestant_sees_no_beacon_when_the_rule_is_on()
    {
        BeaconRule.Hidden.Draws(isGhost: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_ghost_sees_every_one_of_them()
    {
        BeaconRule.Hidden.Draws(isGhost: true).Should().BeTrue();
    }

    [TestMethod]
    public void And_with_the_rule_off_the_game_is_the_game_again()
    {
        BeaconRule.Shown.Draws(isGhost: false).Should().BeTrue();
        BeaconRule.Shown.Draws(isGhost: true).Should().BeTrue();
    }

    [TestMethod]
    public void The_rule_says_which_one_it_is_so_a_log_line_can_be_read()
    {
        BeaconRule.Hidden.ToString().Should().Contain("hidden");
        BeaconRule.Shown.ToString().Should().Contain("drawn");
    }

    [TestMethod]
    public void A_beacon_a_client_turned_back_on_is_a_refusal_and_a_beacon_it_never_had_is_not()
    {
        BeaconRule.Hidden.Refuses(wasVisible: true, isGhost: false).Should().BeTrue();
        BeaconRule.Hidden.Refuses(wasVisible: false, isGhost: false).Should().BeFalse();
    }

    [TestMethod]
    public void And_a_ghost_is_never_refused_because_nothing_was_taken_from_them()
    {
        BeaconRule.Hidden.Refuses(wasVisible: true, isGhost: true).Should().BeFalse();
        BeaconRule.Shown.Refuses(wasVisible: true, isGhost: false).Should().BeFalse();
    }
}
