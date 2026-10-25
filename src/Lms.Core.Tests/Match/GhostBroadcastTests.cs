using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The one flag that crosses the wire about a ghost.</summary>
[TestClass]
public class GhostBroadcastTests
{
    [TestMethod]
    public void A_ghost_survives_the_round_trip()
    {
        GhostBroadcast.TryDecode(new GhostBroadcast(true).Encode(), out GhostBroadcast read)
            .Should().BeTrue();

        read.IsGhost.Should().BeTrue();
    }

    [TestMethod]
    public void And_so_does_a_contestant_because_unghost_has_to_reach_the_client()
    {
        GhostBroadcast.TryDecode(new GhostBroadcast(false).Encode(), out GhostBroadcast read)
            .Should().BeTrue();

        read.IsGhost.Should().BeFalse();
    }

    [TestMethod]
    public void The_field_is_written_either_way_rather_than_only_when_it_is_true()
    {
        new GhostBroadcast(false).Encode().Should().Contain("ghost=0");
        new GhostBroadcast(true).Encode().Should().Contain("ghost=1");
    }

    [TestMethod]
    public void It_is_told_apart_from_the_other_two_messages_by_its_header_alone()
    {
        GhostBroadcast.IsGhostFlag(new GhostBroadcast(true).Encode()).Should().BeTrue();
        GhostBroadcast.IsGhostFlag(MatchBroadcast.WireHeader + "\tsomething=1").Should().BeFalse();
        GhostBroadcast.IsGhostFlag(ArrearsBroadcast.WireHeader + "\towed=8").Should().BeFalse();
        GhostBroadcast.IsGhostFlag(null).Should().BeFalse();
    }

    [TestMethod]
    public void It_names_no_player_because_the_addressee_is_the_channel()
    {
        new GhostBroadcast(true).Encode().Should().Be("lms-ghost/1\tghost=1");
    }

    [TestMethod]
    public void A_line_this_build_cannot_read_is_declined_rather_than_read_as_a_ghost()
    {
        GhostBroadcast.TryDecode("lms-ghost/1\tghost=maybe", out GhostBroadcast _).Should().BeFalse();
        GhostBroadcast.TryDecode("lms-ghost/1", out GhostBroadcast _).Should().BeFalse();
        GhostBroadcast.TryDecode("lms-ghost/2\tghost=1", out GhostBroadcast _).Should().BeFalse();
        GhostBroadcast.TryDecode(null, out GhostBroadcast _).Should().BeFalse();
        GhostBroadcast.TryDecode(string.Empty, out GhostBroadcast _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_because_two_builds_at_once_is_ordinary()
    {
        GhostBroadcast.TryDecode("lms-ghost/1\tsomethingNew=7\tghost=1", out GhostBroadcast read)
            .Should().BeTrue();

        read.IsGhost.Should().BeTrue();
    }

    [TestMethod]
    public void Two_of_the_same_flag_are_equal_so_a_sender_can_tell_a_change_from_a_resend()
    {
        new GhostBroadcast(true).Should().Be(new GhostBroadcast(true));
        new GhostBroadcast(true).Should().NotBe(new GhostBroadcast(false));
        new GhostBroadcast(true).GetHashCode().Should().NotBe(new GhostBroadcast(false).GetHashCode());
    }
}
