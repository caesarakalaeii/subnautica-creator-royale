using FluentAssertions;
using Lms.Core.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Combat;

/// <summary>The message telling an attacker a swing was refused; the weapon's own feedback plays regardless, so a refusal looks like a hit.</summary>
[TestClass]
public class StrikeRefusedBroadcastTests
{
    [TestMethod]
    public void A_refusal_is_a_reason()
    {
        new StrikeRefusedBroadcast(StrikeRefusal.OutOfRange).Encode()
            .Should().Be("lms-refused/1\twhy=out-of-range");
    }

    [TestMethod]
    public void A_refusal_survives_the_round_trip()
    {
        StrikeRefusedBroadcast.TryDecode(new StrikeRefusedBroadcast(StrikeRefusal.TheRingHasYou).Encode(),
            out StrikeRefusedBroadcast refusal).Should().BeTrue();

        refusal.Why.Should().Be(StrikeRefusal.TheRingHasYou);
        refusal.Sentence.Should().Be(StrikeRefusals.Sentence(StrikeRefusal.TheRingHasYou));
    }

    [TestMethod]
    public void A_refusal_is_told_apart_from_the_hit_it_is_not()
    {
        StrikeRefusedBroadcast.IsRefusal(new StrikeRefusedBroadcast(StrikeRefusal.TooSoon).Encode())
            .Should().BeTrue();
        StrikeRefusedBroadcast.IsRefusal(new HurtBroadcast(20, "Mara").Encode()).Should().BeFalse();
        StrikeRefusedBroadcast.IsRefusal(null).Should().BeFalse();
    }

    [TestMethod]
    public void A_reason_this_build_does_not_know_is_refused()
    {
        StrikeRefusedBroadcast.TryDecode("lms-refused/1\twhy=out-of-bananas", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_refusal_with_no_reason_is_refused()
    {
        StrikeRefusedBroadcast.TryDecode("lms-refused/1", out _).Should().BeFalse();
        StrikeRefusedBroadcast.TryDecode("lms-refused/1\twhy=none", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_version_this_build_does_not_know_is_refused()
    {
        StrikeRefusedBroadcast.TryDecode("lms-refused/2\twhy=too-soon", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Every_reason_the_rule_can_give_travels()
    {
        foreach (StrikeRefusal why in System.Enum.GetValues<StrikeRefusal>())
        {
            if (why == StrikeRefusal.None)
            {
                continue;
            }

            StrikeRefusedBroadcast.TryDecode(new StrikeRefusedBroadcast(why).Encode(),
                out StrikeRefusedBroadcast read).Should().BeTrue(why.ToString());
            read.Why.Should().Be(why);
        }
    }
}
