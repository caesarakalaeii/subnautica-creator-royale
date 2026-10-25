using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What a caster is shown about every contestant.</summary>
[TestClass]
public class CasterFeedTests
{
    private static CasterFeed Read(string line)
    {
        CasterFeed.TryDecode(line, out CasterFeed feed).Should().BeTrue();

        return feed;
    }

    [TestMethod]
    public void A_contestant_survives_the_wire_whole()
    {
        CasterFeed sent = new CasterFeed(new[]
        {
            new CasterSighting("Vela", new WorldPoint(-844.25, 0.6, 1021.5), 80.5, 42),
        });

        CasterSighting read = Read(sent.Encode()).Seen.Single();

        read.Name.Should().Be("Vela");
        read.Where.X.Should().BeApproximately(-844.25, 0.0005);
        read.Where.Y.Should().BeApproximately(0.6, 0.0005);
        read.Where.Z.Should().BeApproximately(1021.5, 0.0005);
        read.Health.Should().BeApproximately(80.5, 0.0005);
        read.Oxygen.Should().BeApproximately(42, 0.0005);
    }

    [TestMethod]
    public void A_row_without_the_alive_field_reads_as_living()
    {
        CasterSighting read = Read("lms-caster/1\tp=Vela:1:2:3:100:45").Seen.Single();

        read.Alive.Should().BeTrue();
    }

    [TestMethod]
    public void A_dead_row_travels_the_wire_whole()
    {
        CasterFeed sent = new CasterFeed(new[]
        {
            new CasterSighting("Vela", new WorldPoint(1, 2, 3), 0, 0, alive: false),
        });

        CasterSighting read = Read(sent.Encode()).Seen.Single();

        read.Alive.Should().BeFalse();
        read.Health.Should().BeApproximately(0, 0.0005);
        read.Where.X.Should().BeApproximately(1, 0.0005);
    }

    [TestMethod]
    public void A_living_row_writes_the_alive_field()
    {
        string line = new CasterFeed(new[]
        {
            new CasterSighting("Vela", new WorldPoint(1, 2, 3), 100, 45),
        }).Encode();

        line.Should().Be("lms-caster/1\tp=Vela:1:2:3:100:45:1");
    }

    [TestMethod]
    public void Everybody_travels_and_the_order_is_kept()
    {
        CasterFeed sent = new CasterFeed(new[]
        {
            new CasterSighting("Vela", new WorldPoint(1, 2, 3), 100, 45),
            new CasterSighting("Kai", new WorldPoint(4, 5, 6), 12, 3),
            new CasterSighting("Rook", new WorldPoint(7, 8, 9), 55, 20),
        });

        CasterFeed read = Read(sent.Encode());

        read.Seen.Select(one => one.Name).Should().ContainInOrder("Vela", "Kai", "Rook");
    }

    [TestMethod]
    public void Nobody_in_the_match_is_a_real_message()
    {
        CasterFeed read = Read(new CasterFeed(null).Encode());

        read.Seen.Should().BeEmpty();
    }

    [TestMethod]
    public void Another_builds_message_is_not_read_as_this_one()
    {
        CasterFeed.IsFeed(new HiddenBroadcast(new[] { "Vela" }).Encode()).Should().BeFalse();
        CasterFeed.TryDecode("lms-caster/2\tp=Vela:1:2:3:4:5", out _).Should().BeFalse();
        CasterFeed.TryDecode(null, out _).Should().BeFalse();
        CasterFeed.TryDecode("   ", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_row_that_does_not_parse_costs_one_plate_rather_than_all_of_them()
    {
        CasterFeed read = Read("lms-caster/1\tp=Vela:1:2:3:100:45\tp=Kai:1:nonsense:3:100:45\tp=Rook:7:8:9:55:20");

        read.Seen.Select(one => one.Name).Should().ContainInOrder("Vela", "Rook");
    }

    [TestMethod]
    public void A_short_row_is_dropped_rather_than_half_read()
    {
        CasterFeed read = Read("lms-caster/1\tp=Vela:1:2:3\tp=Rook:7:8:9:55:20");

        read.Seen.Select(one => one.Name).Should().ContainSingle().Which.Should().Be("Rook");
    }

    [TestMethod]
    public void A_field_a_later_build_added_is_ignored()
    {
        CasterFeed read = Read("lms-caster/1\tp=Vela:1:2:3:100:45\tvehicle=Seamoth");

        read.Seen.Should().ContainSingle().Which.Name.Should().Be("Vela");
    }

    [TestMethod]
    public void A_name_cannot_carry_a_separator()
    {
        CasterFeed sent = new CasterFeed(new[]
        {
            new CasterSighting("Ve\tla:99", new WorldPoint(1, 2, 3), 100, 45),
        });

        CasterSighting read = Read(sent.Encode()).Seen.Single();

        read.Name.Should().Be("Ve la 99");
        read.Where.X.Should().BeApproximately(1, 0.0005);
    }

    [TestMethod]
    public void A_blank_name_is_nobody_and_is_not_sent()
    {
        CasterFeed sent = new CasterFeed(new[]
        {
            new CasterSighting("  ", new WorldPoint(1, 2, 3), 100, 45),
            new CasterSighting("Vela", new WorldPoint(1, 2, 3), 100, 45),
        });

        sent.Seen.Should().ContainSingle().Which.Name.Should().Be("Vela");
    }

    [TestMethod]
    public void The_numbers_are_invariant()
    {
        string line = new CasterFeed(new[]
        {
            new CasterSighting("Vela", new WorldPoint(-1.5, 2.25, 3), 99.5, 44.5),
        }).Encode();

        line.Should().Contain("-1.5").And.Contain("2.25").And.NotContain(",");
    }

    [TestMethod]
    public void Two_feeds_saying_the_same_thing_are_the_same_feed()
    {
        CasterSighting[] seen = { new CasterSighting("Vela", new WorldPoint(1, 2, 3), 100, 45) };

        new CasterFeed(seen).Should().Be(new CasterFeed(seen));
        new CasterFeed(seen).GetHashCode().Should().Be(new CasterFeed(seen).GetHashCode());
        new CasterFeed(seen).Should().NotBe(new CasterFeed(null));
    }
}
