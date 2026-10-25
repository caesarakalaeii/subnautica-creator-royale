using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What the server tells a returning client about its own standing.</summary>
[TestClass]
public class EliminatedBroadcastTests
{
    [TestMethod]
    public void The_verdict_survives_the_round_trip()
    {
        EliminatedBroadcast.TryDecode(new EliminatedBroadcast(2, 3).Encode(),
            out EliminatedBroadcast read).Should().BeTrue();

        read.Group.Should().Be(2);
        read.Generation.Should().Be(3);
    }

    [TestMethod]
    public void It_is_told_apart_from_every_other_message_on_the_channel_by_its_header_alone()
    {
        EliminatedBroadcast.IsEliminated(new EliminatedBroadcast(1, 1).Encode()).Should().BeTrue();
        EliminatedBroadcast.IsEliminated(MatchBroadcast.WireHeader + "\tsomething=1").Should().BeFalse();
        EliminatedBroadcast.IsEliminated(ArrearsBroadcast.WireHeader + "\towed=8").Should().BeFalse();
        EliminatedBroadcast.IsEliminated(GhostBroadcast.WireHeader + "\tghost=1").Should().BeFalse();
        EliminatedBroadcast.IsEliminated(null).Should().BeFalse();
    }

    [TestMethod]
    public void And_no_other_message_on_the_channel_answers_to_it()
    {
        string verdict = new EliminatedBroadcast(1, 1).Encode();

        GhostBroadcast.IsGhostFlag(verdict).Should().BeFalse();
        HiddenBroadcast.IsHidden(verdict).Should().BeFalse();
        RulesBroadcast.IsRules(verdict).Should().BeFalse();
        RoundBroadcast.IsRound(verdict).Should().BeFalse();
        ArrearsBroadcast.IsArrears(verdict).Should().BeFalse();
        MatchBroadcast.TryDecode(verdict, out MatchBroadcast _).Should().BeFalse();
    }

    [TestMethod]
    public void It_names_no_player_because_the_addressee_is_the_channel()
    {
        new EliminatedBroadcast(2, 3).Encode().Should().Be("lms-out/1\tgroup=2\tgen=3");
    }

    [TestMethod]
    public void The_fence_is_the_group_and_the_generation_because_a_reset_puts_everybody_back()
    {
        new EliminatedBroadcast(2, 3).Should().NotBe(new EliminatedBroadcast(3, 3));
        new EliminatedBroadcast(2, 3).Should().NotBe(new EliminatedBroadcast(2, 4));
        new EliminatedBroadcast(2, 3).Should().Be(new EliminatedBroadcast(2, 3));
        new EliminatedBroadcast(2, 3).GetHashCode().Should().NotBe(new EliminatedBroadcast(3, 3).GetHashCode());
    }

    [TestMethod]
    public void A_line_this_build_cannot_read_is_declined_rather_than_read_as_an_elimination()
    {
        EliminatedBroadcast.TryDecode("lms-out/1\tgroup=2", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode("lms-out/1\tgen=3", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode("lms-out/1\tgroup=nine\tgen=3", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode("lms-out/1\tgroup=0\tgen=3", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode("lms-out/1\tgroup=2\tgen=0", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode("lms-out/2\tgroup=2\tgen=3", out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode(null, out EliminatedBroadcast _).Should().BeFalse();
        EliminatedBroadcast.TryDecode(string.Empty, out EliminatedBroadcast _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_because_two_builds_at_once_is_ordinary()
    {
        EliminatedBroadcast.TryDecode("lms-out/1\tgroup=2\tgen=3\tcause=zone",
            out EliminatedBroadcast read).Should().BeTrue();

        read.Group.Should().Be(2);
    }
}
