using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary><c>lms look &lt;player&gt;</c> — a caster puts the ghost's camera on a contestant.</summary>
[TestClass]
public class MatchHostLookVerbTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly WorldPoint Somewhere = new WorldPoint(123.456, -42.1, 88.9);

    private static PlayerReading Player(ushort id, string name, WorldPoint where) =>
        new PlayerReading(id, name, where, true, 92, 40, 70, 65);

    private static MatchHost Host(out List<LookBroadcast> told, params string[] ghosts)
    {
        List<LookBroadcast> sent = new List<LookBroadcast>();
        told = sent;

        MatchHost host = new MatchHost(Configured(ghosts), TimeSpan.FromSeconds(10));

        host.Connected = () => new[]
        {
            Player(1, "Vela", Somewhere),
            Player(2, "Ro\\ok", new WorldPoint(-5, -300, 12)),
            Player(3, "Caster", new WorldPoint(0, -10, 0)),
        };

        host.PointsTheCameras = look =>
        {
            sent.Add(look);
            return host.Ghosts.Count;
        };

        return host;
    }

    private static TournamentConfig Configured(params string[] ghosts)
    {
        string list = ghosts.Length == 0
            ? string.Empty
            : "\nghosts:\n" + string.Join(string.Empty, Array.ConvertAll(ghosts, name => $"  - {name}\n"));

        TournamentConfigLoadResult result = TournamentConfigLoader.Load(@"
match:
  cycleLength: 5m
  warningLead: 1m
zone:
  damageInterval: 2s
  opening:
    centre: [0, -100, 0]
    radius: 1400
    damagePerTick: 1
  phases:
    - centre: [0, -100, 0]
      radius: 900
      shrinkFor: 90s
      damagePerTick: 3
" + list);

        result.Problems.Should().BeEmpty();

        return result.Config!;
    }

    [TestMethod]
    public void The_camera_is_sent_to_where_the_named_contestant_is()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        string answer = host.Command("look Vela", Noon);

        told.Should().ContainSingle();
        told[0].Name.Should().Be("Vela");
        told[0].Where.Should().Be(Somewhere);
        answer.Should().Contain("Vela");
    }

    [TestMethod]
    public void The_answer_says_how_many_cameras_moved()
    {
        MatchHost host = Host(out _, "Caster", "Producer");

        host.Command("look Vela", Noon).Should().Contain("2 camera(s) on Vela");
    }

    [TestMethod]
    public void A_tournament_with_no_caster_in_it_says_so_rather_than_saying_done()
    {
        MatchHost host = Host(out List<LookBroadcast> told);

        host.Command("look Vela", Noon).Should().Contain("nobody is a ghost");
        told.Should().ContainSingle("the verb still ran; there was simply nobody listening");
    }

    [TestMethod]
    public void A_name_nobody_is_connected_as_is_refused_by_name()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        string answer = host.Command("look Nobody", Noon);

        answer.Should().Contain("Nobody").And.Contain("not in the match");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void A_camera_is_not_sent_to_another_ghost()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        string answer = host.Command("look Caster", Noon);

        answer.Should().Contain("Caster").And.Contain("ghost");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void The_verb_on_its_own_asks_for_a_name_rather_than_being_unrecognised()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        host.Command("look", Noon).Should().Contain("needs a name");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void The_name_is_matched_the_way_an_operator_typed_it()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        host.Command("look vELA", Noon).Should().Contain("Vela");
        told[0].Name.Should().Be("Vela");
    }

    [TestMethod]
    public void Cam_is_the_same_verb()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        host.Command("cam Vela", Noon).Should().Contain("Vela");
        told.Should().ContainSingle();
    }

    [TestMethod]
    public void An_awkward_name_survives_being_looked_at()
    {
        MatchHost host = Host(out List<LookBroadcast> told, "Caster");

        host.Command(@"look Ro\ok", Noon).Should().Contain(@"Ro\ok");
        told[0].Where.Should().Be(new WorldPoint(-5, -300, 12));
    }

    [TestMethod]
    public void Nothing_is_written_to_the_audit_log()
    {
        MatchHost host = Host(out _, "Caster");

        host.Command("look Vela", Noon);

        host.Audit.Records.Should().BeEmpty();
    }
}
