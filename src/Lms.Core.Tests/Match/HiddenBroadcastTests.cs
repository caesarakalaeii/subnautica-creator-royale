using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Who no client may draw.</summary>
[TestClass]
public class HiddenBroadcastTests
{
    private static HiddenBroadcast Read(string line)
    {
        HiddenBroadcast.TryDecode(line, out HiddenBroadcast hidden).Should().BeTrue();

        return hidden;
    }

    [TestMethod]
    public void A_list_of_names_survives_the_round_trip()
    {
        HiddenBroadcast sent = new HiddenBroadcast(new[] { "Caster", "Producer" });

        Read(sent.Encode()).Names.Should().Equal("Caster", "Producer");
    }

    [TestMethod]
    public void Nobody_hidden_is_a_real_message_rather_than_a_refusal()
    {
        Read(new HiddenBroadcast(null).Encode()).Names.Should().BeEmpty();
        Read(HiddenBroadcast.WireHeader).Names.Should().BeEmpty();
    }

    [TestMethod]
    public void The_whole_list_travels_rather_than_a_change_to_it()
    {
        new HiddenBroadcast(new[] { "Caster" }).Encode()
            .Should().Be("lms-hidden/1\tname=Caster");
    }

    [TestMethod]
    public void One_field_per_name_rather_than_one_field_with_commas_in_it()
    {
        new HiddenBroadcast(new[] { "Ada", "Bea" }).Encode()
            .Should().Be("lms-hidden/1\tname=Ada\tname=Bea");
    }

    [TestMethod]
    public void It_is_told_apart_from_the_other_messages_by_its_header_alone()
    {
        HiddenBroadcast.IsHidden(new HiddenBroadcast(new[] { "Caster" }).Encode()).Should().BeTrue();

        HiddenBroadcast.IsHidden(new GhostBroadcast(true).Encode()).Should().BeFalse();
        GhostBroadcast.IsGhostFlag(new HiddenBroadcast(new[] { "Caster" }).Encode()).Should().BeFalse();

        HiddenBroadcast.IsHidden(MatchBroadcast.WireHeader + "\tx=1").Should().BeFalse();
        HiddenBroadcast.IsHidden(null).Should().BeFalse();
    }

    [TestMethod]
    public void A_header_this_build_does_not_know_is_declined()
    {
        HiddenBroadcast.TryDecode("lms-hidden/2\tname=Caster", out HiddenBroadcast _).Should().BeFalse();
        HiddenBroadcast.TryDecode(null, out HiddenBroadcast _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_because_two_builds_at_once_is_ordinary()
    {
        Read("lms-hidden/1\tsomethingNew=7\tname=Caster").Names.Should().Equal("Caster");
    }

    [TestMethod]
    public void A_blank_name_is_dropped_because_it_would_match_nobody_anyway()
    {
        new HiddenBroadcast(new[] { "Caster", "  ", string.Empty }).Names.Should().Equal("Caster");
        Read("lms-hidden/1\tname=Caster\tname=").Names.Should().Equal("Caster");
    }

    [TestMethod]
    public void Two_of_the_same_list_are_equal_so_a_sender_can_tell_a_change_from_a_resend()
    {
        new HiddenBroadcast(new[] { "Ada", "Bea" })
            .Should().Be(new HiddenBroadcast(new[] { "Ada", "Bea" }));

        new HiddenBroadcast(new[] { "Ada" })
            .Should().NotBe(new HiddenBroadcast(new[] { "Ada", "Bea" }));

        new HiddenBroadcast(new[] { "Ada", "Bea" })
            .Should().NotBe(new HiddenBroadcast(new[] { "Bea", "Ada" }));
    }
}
