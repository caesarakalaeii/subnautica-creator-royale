using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Who every caster's camera is staying on.</summary>
[TestClass]
public class FollowBroadcastTests
{
    private static FollowBroadcast Read(string line)
    {
        FollowBroadcast.TryDecode(line, out FollowBroadcast follow).Should().BeTrue();

        return follow;
    }

    [TestMethod]
    public void Who_to_stay_on_survives_the_wire()
    {
        FollowBroadcast read = Read(new FollowBroadcast("Vela").Encode());

        read.Following.Should().BeTrue();
        read.Name.Should().Be("Vela");
    }

    [TestMethod]
    public void Letting_go_is_a_message_rather_than_the_absence_of_one()
    {
        FollowBroadcast read = Read(FollowBroadcast.LetGo.Encode());

        read.Following.Should().BeFalse();
        read.Name.Should().BeEmpty();
    }

    [TestMethod]
    public void A_blank_name_is_letting_go_rather_than_a_player_called_nothing()
    {
        new FollowBroadcast("   ").Following.Should().BeFalse();
        new FollowBroadcast(null).Following.Should().BeFalse();
    }

    [TestMethod]
    public void Another_builds_message_is_not_read_as_this_one()
    {
        FollowBroadcast.IsFollow(new CasterFeed(null).Encode()).Should().BeFalse();
        FollowBroadcast.IsFollow(FollowBroadcast.LetGo.Encode()).Should().BeTrue();
        FollowBroadcast.TryDecode("lms-follow/3\tname=Vela", out _).Should().BeFalse();
        FollowBroadcast.TryDecode(null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_v2_line_without_a_ghost_is_the_unaddressed_follow_it_always_was()
    {
        FollowBroadcast read = Read("lms-follow/2\tname=Vela");

        read.Following.Should().BeTrue();
        read.Name.Should().Be("Vela");
        read.Ghost.Should().BeEmpty();
    }

    [TestMethod]
    public void A_v1_line_still_decodes_for_a_mixed_fleet()
    {
        FollowBroadcast read = Read("lms-follow/1\tname=Vela");

        read.Following.Should().BeTrue();
        read.Name.Should().Be("Vela");
        read.Ghost.Should().BeEmpty();
    }

    [TestMethod]
    public void A_follow_can_be_addressed_to_one_ghost()
    {
        FollowBroadcast follow = new FollowBroadcast("Caster", "Vela");

        follow.Encode().Should().Be("lms-follow/2\tghost=Caster\tname=Vela");

        FollowBroadcast read = Read(follow.Encode());

        read.Following.Should().BeTrue();
        read.Name.Should().Be("Vela");
        read.Ghost.Should().Be("Caster");
    }

    [TestMethod]
    public void An_unaddressed_follow_encodes_without_a_ghost_field()
    {
        new FollowBroadcast("Vela").Encode().Should().Be("lms-follow/2\tname=Vela");
    }

    [TestMethod]
    public void Letting_go_can_be_addressed_to_one_ghost()
    {
        FollowBroadcast follow = new FollowBroadcast("Caster", null);

        follow.Following.Should().BeFalse();
        follow.Ghost.Should().Be("Caster");

        FollowBroadcast read = Read(follow.Encode());

        read.Following.Should().BeFalse();
        read.Ghost.Should().Be("Caster");
    }

    [TestMethod]
    public void A_blank_ghost_is_everybody_rather_than_a_ghost_called_nothing()
    {
        new FollowBroadcast("  ", "Vela").Ghost.Should().BeEmpty();
        new FollowBroadcast(null, "Vela").Ghost.Should().BeEmpty();
        Read("lms-follow/2\tghost=\tname=Vela").Ghost.Should().BeEmpty();
    }

    [TestMethod]
    public void Two_follows_saying_the_same_thing_are_the_same_follow()
    {
        new FollowBroadcast("Vela").Should().Be(new FollowBroadcast("Vela"));
        new FollowBroadcast("Vela").GetHashCode().Should().Be(new FollowBroadcast("Vela").GetHashCode());
        new FollowBroadcast("Vela").Should().NotBe(FollowBroadcast.LetGo);
    }

    [TestMethod]
    public void Follows_for_different_ghosts_are_different_follows()
    {
        FollowBroadcast forCaster = new FollowBroadcast("Caster", "Vela");

        forCaster.Should().Be(new FollowBroadcast("Caster", "Vela"));
        forCaster.GetHashCode().Should().Be(new FollowBroadcast("caster", "Vela").GetHashCode(),
            "the addressee is matched the way the roster matches: case does not matter");
        forCaster.Should().NotBe(new FollowBroadcast("Producer", "Vela"));
        forCaster.Should().NotBe(new FollowBroadcast("Vela"),
            "addressed to one ghost is not the same instruction as addressed to everybody");
    }

    [TestMethod]
    public void The_sentence_names_who_it_is_for_when_it_is_for_somebody()
    {
        new FollowBroadcast("Caster", "Vela").ToString().Should().Be("for Caster: stay on Vela");
        new FollowBroadcast("Caster", null).ToString().Should().Be("for Caster: let go");
        new FollowBroadcast("Vela").ToString().Should().Be("stay on Vela");
        FollowBroadcast.LetGo.ToString().Should().Be("let go");
    }

    [TestMethod]
    public void A_name_cannot_carry_a_separator()
    {
        Read(new FollowBroadcast("Ve\tla").Encode()).Name.Should().Be("Ve la");
    }

    [TestMethod]
    public void A_field_a_later_build_added_is_ignored()
    {
        Read("lms-follow/1\tname=Vela\tease=0.5").Name.Should().Be("Vela");
    }

}
