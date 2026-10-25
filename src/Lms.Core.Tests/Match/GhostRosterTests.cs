using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Who the match cannot see.</summary>
[TestClass]
public class GhostRosterTests
{
    private static PlayerReading Connected(ushort id, string name) =>
        new PlayerReading(id, name, new WorldPoint(0, 0, 0), true, 100, 45, 100, 100);

    private static readonly IReadOnlyList<PlayerReading> Three = new[]
    {
        Connected(1, "Vela"), Connected(2, "Caster"), Connected(3, "Rook"),
    };

    [TestMethod]
    public void Nobody_is_a_ghost_by_default()
    {
        GhostRoster roster = new GhostRoster();

        roster.IsGhost("Vela").Should().BeFalse();
        roster.Count.Should().Be(0);
    }

    [TestMethod]
    public void The_config_names_the_crew_before_the_day()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster", "Producer" });

        roster.IsGhost("Caster").Should().BeTrue();
        roster.IsGhost("Producer").Should().BeTrue();
        roster.IsGhost("Vela").Should().BeFalse();
    }

    [TestMethod]
    public void A_name_typed_into_a_file_matches_the_one_a_client_reports_whatever_its_case()
    {
        GhostRoster roster = new GhostRoster(new[] { "caster" });

        roster.IsGhost("Caster").Should().BeTrue();
        roster.IsGhost("CASTER").Should().BeTrue();
    }

    [TestMethod]
    public void The_verb_is_for_the_person_who_joins_unexpectedly()
    {
        GhostRoster roster = new GhostRoster();

        roster.Add("Latecomer").Should().BeTrue("nobody had said so yet");
        roster.IsGhost("Latecomer").Should().BeTrue();

        roster.Add("Latecomer").Should().BeFalse("saying it twice changes nothing");
    }

    [TestMethod]
    public void And_it_can_be_taken_back()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster" });

        roster.Remove("caster").Should().BeTrue();
        roster.IsGhost("Caster").Should().BeFalse();

        roster.Remove("Caster").Should().BeFalse("they were not one");
    }

    [TestMethod]
    public void Nothing_with_no_name_is_a_ghost()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster" });

        roster.IsGhost(null).Should().BeFalse();
        roster.IsGhost(string.Empty).Should().BeFalse();
        roster.IsGhost("   ").Should().BeFalse();
    }

    [TestMethod]
    public void A_name_that_is_only_whitespace_cannot_be_added()
    {
        GhostRoster roster = new GhostRoster();

        roster.Add("  ").Should().BeFalse();
        roster.Count.Should().Be(0);
    }

    [TestMethod]
    public void A_name_is_trimmed_on_the_way_in_because_a_console_hands_over_what_was_typed()
    {
        GhostRoster roster = new GhostRoster();

        roster.Add("  Caster  ").Should().BeTrue();
        roster.IsGhost("Caster").Should().BeTrue();
        roster.Named.Should().Equal("Caster");
    }

    [TestMethod]
    public void The_names_read_back_in_one_order_so_a_status_line_is_the_same_line_twice()
    {
        GhostRoster roster = new GhostRoster(new[] { "Zed", "Ada" });
        roster.Add("Mo");

        roster.Named.Should().Equal("Ada", "Mo", "Zed");
    }

    [TestMethod]
    public void The_contestants_are_who_is_connected_minus_the_ghosts()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster" });

        roster.Contestants(Three).Should().HaveCount(2);
        roster.Contestants(Three).Should().OnlyContain(reading => reading.Name != "Caster");
    }

    [TestMethod]
    public void And_the_ghosts_are_the_other_half_because_somebody_has_to_be_told_they_are_one()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster" });

        roster.Ghosts(Three).Should().HaveCount(1);
        roster.Ghosts(Three)[0].Name.Should().Be("Caster");
    }

    [TestMethod]
    public void A_roster_with_no_ghosts_in_it_hands_the_readings_straight_back()
    {
        GhostRoster roster = new GhostRoster();

        roster.Contestants(Three).Should().BeSameAs(Three);
        roster.Ghosts(Three).Should().BeEmpty();
    }

    [TestMethod]
    public void Nothing_at_all_is_no_contestants_rather_than_a_throw()
    {
        GhostRoster roster = new GhostRoster(new[] { "Caster" });

        roster.Contestants(null!).Should().BeEmpty();
        roster.Ghosts(null!).Should().BeEmpty();
    }

    [TestMethod]
    public void A_null_list_of_names_is_a_roster_with_nobody_in_it()
    {
        Action building = () => new GhostRoster(null);

        building.Should().NotThrow("a config with no ghosts: section is the ordinary case");
    }
}
