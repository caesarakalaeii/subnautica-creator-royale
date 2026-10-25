using FluentAssertions;
using Lms.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Settings;

/// <summary>Which signal pings are a player's location; Nitrox's marker registers before the story SignalPing is destroyed.</summary>
[TestClass]
public class SignalLocationTests
{
    [TestMethod]
    public void A_remote_player_marker_is_swept_even_while_the_story_component_is_still_attached()
    {
        SignalLocation.IsALocation("signal_Spud", hasSignalPing: true, inAnotherPlayersPod: false)
            .Should().BeTrue();
    }

    [TestMethod]
    public void A_story_signal_is_left_alone()
    {
        SignalLocation.IsALocation("Signal(Clone)", hasSignalPing: true, inAnotherPlayersPod: false)
            .Should().BeFalse();
    }

    [TestMethod]
    public void A_mod_ping_is_left_alone()
    {
        SignalLocation.IsALocation("LmsZoneCentre", hasSignalPing: false, inAnotherPlayersPod: false)
            .Should().BeFalse();
    }

    [TestMethod]
    public void The_players_own_pod_is_left_alone()
    {
        SignalLocation.IsALocation("EscapePod", hasSignalPing: false, inAnotherPlayersPod: false)
            .Should().BeFalse();
    }

    [TestMethod]
    public void Another_players_pod_is_swept()
    {
        SignalLocation.IsALocation("EscapePod", hasSignalPing: false, inAnotherPlayersPod: true)
            .Should().BeTrue();
    }

    [TestMethod]
    public void A_remote_player_marker_is_swept_whatever_the_story_component_decides()
    {
        SignalLocation.IsALocation("signal_Spud", hasSignalPing: false, inAnotherPlayersPod: false)
            .Should().BeTrue();
    }

    [TestMethod]
    public void A_remote_player_marker_is_recognized_by_its_name_alone()
    {
        SignalLocation.IsMarker("signal_Spud").Should().BeTrue();

        SignalLocation.IsMarker("Signal(Clone)").Should().BeFalse();
        SignalLocation.IsMarker("LmsZoneCentre").Should().BeFalse();
        SignalLocation.IsMarker(string.Empty).Should().BeFalse();
    }
}