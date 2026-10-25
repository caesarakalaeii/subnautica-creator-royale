using System;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>What a caster's client does with a feed that arrives once a server tick.</summary>
[TestClass]
public class CasterTrackTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(CasterTrack.MaxLeadSeconds);

    private static CasterFeed Feed(params CasterSighting[] seen) => new CasterFeed(seen);

    private static CasterSighting At(string name, double x, double z, double health = 100,
        double oxygen = 45) =>
        new CasterSighting(name, new WorldPoint(x, 0, z), health, oxygen);

    private static WorldPoint Where(CasterTrack track, string name, TimeSpan now)
    {
        track.TryFind(name, now, out CasterSighting found).Should().BeTrue();

        return found.Where;
    }

    [TestMethod]
    public void One_feed_is_drawn_where_it_landed()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);

        Where(track, "Vela", TimeSpan.Zero).X.Should().BeApproximately(100, 0.001);
    }

    [TestMethod]
    public void Two_feeds_are_rendered_between_the_last_two_sightings_one_tick_behind()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        Where(track, "Vela", Tick + Tick / 2).X.Should().BeApproximately(102, 0.001);
    }

    [TestMethod]
    public void The_plate_is_on_a_sample_a_quarter_of_a_second_after_it_arrives()
    {
        CasterTrack track = new CasterTrack();
        TimeSpan quarter = TimeSpan.FromSeconds(0.25);

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 101, 200)), quarter);

        Where(track, "Vela", quarter * 2).X.Should().BeApproximately(101, 0.001);
    }

    [TestMethod]
    public void A_seamoth_whose_feed_arrives_early_is_swept_rather_than_teleported()
    {
        CasterTrack track = new CasterTrack();
        TimeSpan early = Tick - TimeSpan.FromSeconds(0.1);
        double stride = 20 * Tick.TotalSeconds;

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 100 + stride, 200)), early);

        Where(track, "Vela", early + Tick / 2).X.Should().BeLessThan(100 + stride);
    }

    [TestMethod]
    public void The_plate_reaches_the_newest_sample_one_tick_after_it_arrives()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        Where(track, "Vela", Tick * 2).X.Should().BeApproximately(104, 0.001);
    }

    [TestMethod]
    public void A_new_sample_does_not_snap_the_plate()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        Where(track, "Vela", Tick * 2 - TimeSpan.FromMilliseconds(1)).X
            .Should().BeApproximately(104, 0.05);

        track.Receive(Feed(At("Vela", 108, 200)), Tick * 2);

        Where(track, "Vela", Tick * 2).X.Should().BeApproximately(104, 0.001);
    }

    [TestMethod]
    public void A_feed_that_stops_leaves_the_plate_at_the_newest_sample()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        Where(track, "Vela", Tick + TimeSpan.FromMinutes(5)).X
            .Should().BeApproximately(104, 0.001);
    }

    [TestMethod]
    public void A_teleport_is_not_swept_across_but_lands_at_the_newest_sample()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 0, 0)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 2000, 0)), Tick);

        Where(track, "Vela", Tick + Tick / 2).X.Should().BeApproximately(2000, 0.001);
        Where(track, "Vela", Tick * 2).X.Should().BeApproximately(2000, 0.001);
    }

    [TestMethod]
    public void The_vitals_are_the_newest_ones_and_are_never_predicted()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200, health: 100, oxygen: 45)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200, health: 60, oxygen: 20)), Tick);

        track.TryFind("Vela", Tick + Tick / 2, out CasterSighting found)
            .Should().BeTrue();

        found.Health.Should().Be(60);
        found.Oxygen.Should().Be(20);
    }

    [TestMethod]
    public void Somebody_missing_from_the_newest_feed_is_gone()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200), At("Kai", 300, 400)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        track.Names.Should().ContainSingle().Which.Should().Be("Vela");
        track.TryFind("Kai", Tick, out _).Should().BeFalse();
    }

    [TestMethod]
    public void An_empty_feed_empties_the_screen()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);
        track.Receive(Feed(), Tick);

        track.Names.Should().BeEmpty();
    }

    [TestMethod]
    public void A_name_is_found_however_it_was_typed()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), TimeSpan.Zero);

        track.TryFind("vela", TimeSpan.Zero, out CasterSighting found).Should().BeTrue();
        found.Name.Should().Be("Vela", "the feed's spelling is the one a plate shows");
    }

    [TestMethod]
    public void Somebody_who_rejoins_starts_again_rather_than_on_a_stale_velocity()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 0, 0)), TimeSpan.Zero);
        track.Receive(Feed(At("Vela", 4, 0)), Tick);
        track.Receive(Feed(), Tick * 2);
        track.Receive(Feed(At("Vela", 900, 0)), Tick * 3);

        Where(track, "Vela", Tick * 3 + Tick / 2).X.Should().BeApproximately(900, 0.001);
    }

    [TestMethod]
    public void Nobody_is_found_before_a_feed_has_arrived()
    {
        new CasterTrack().TryFind("Vela", TimeSpan.Zero, out _).Should().BeFalse();
        new CasterTrack().TryFind(null, TimeSpan.Zero, out _).Should().BeFalse();
        new CasterTrack().Names.Should().BeEmpty();
    }

    [TestMethod]
    public void Two_feeds_at_the_same_moment_do_not_divide_by_nothing()
    {
        CasterTrack track = new CasterTrack();

        track.Receive(Feed(At("Vela", 100, 200)), Tick);
        track.Receive(Feed(At("Vela", 104, 200)), Tick);

        WorldPoint where = Where(track, "Vela", Tick + Tick / 2);

        double.IsNaN(where.X).Should().BeFalse();
        where.X.Should().BeApproximately(104, 0.001);
    }
}
