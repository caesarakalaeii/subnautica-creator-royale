using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary><c>lms follow &lt;player&gt;</c> and <c>lms unfollow</c> — the camera that stays on somebody.</summary>
[TestClass]
public class MatchHostFollowVerbTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly WorldPoint Somewhere = new WorldPoint(123.456, -42.1, 88.9);

    private static PlayerReading Player(ushort id, string name, WorldPoint where) =>
        new PlayerReading(id, name, where, true, 92, 40, 70, 65);

    private static MatchHost Host(out List<FollowBroadcast> told, params string[] ghosts)
    {
        List<FollowBroadcast> sent = new List<FollowBroadcast>();
        told = sent;

        MatchHost host = new MatchHost(Configured(ghosts), TimeSpan.FromSeconds(10));

        host.Connected = () => new[]
        {
            Player(1, "Vela", Somewhere),
            Player(2, "Rook", new WorldPoint(-5, -300, 12)),
            Player(3, "Caster", new WorldPoint(0, -10, 0)),
        };

        host.HoldsTheCameras = follow =>
        {
            sent.Add(follow);
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
    public void The_cameras_are_told_who_to_stay_on()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        string answer = host.Command("follow Vela", Noon);

        told.Should().ContainSingle();
        told[0].Following.Should().BeTrue();
        told[0].Name.Should().Be("Vela");
        answer.Should().Contain("Vela");
    }

    [TestMethod]
    public void The_name_is_the_servers_spelling_rather_than_the_operators()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        host.Command("follow vela", Noon);

        told[0].Name.Should().Be("Vela");
    }

    [TestMethod]
    public void The_answer_says_how_many_cameras_are_on_them()
    {
        MatchHost host = Host(out _, "Caster", "Producer");

        host.Command("follow Vela", Noon).Should().Contain("2 camera(s)").And.Contain("Vela");
    }

    [TestMethod]
    public void A_tournament_with_no_caster_in_it_says_so_rather_than_saying_done()
    {
        MatchHost host = Host(out List<FollowBroadcast> told);

        host.Command("follow Vela", Noon).Should().Contain("nobody is a ghost");
        told.Should().ContainSingle("the verb still ran; there was simply nobody listening");
    }

    [TestMethod]
    public void A_name_nobody_is_connected_as_is_refused_by_name()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        string answer = host.Command("follow Nobody", Noon);

        answer.Should().Contain("Nobody").And.Contain("not in the match");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void A_camera_is_not_locked_onto_another_ghost()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        string answer = host.Command("follow Caster", Noon);

        answer.Should().Contain("Caster").And.Contain("ghost");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void A_half_typed_follow_is_answered_with_the_usage_line()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        host.Command("follow", Noon).Should().Contain("follow <player>");
        told.Should().BeEmpty();
    }

    [TestMethod]
    public void Unfollow_lets_every_camera_go()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        host.Command("follow Vela", Noon);
        string answer = host.Command("unfollow", Noon);

        told.Should().HaveCount(2);
        told[1].Following.Should().BeFalse();
        answer.Should().Contain("fly");
    }

    [TestMethod]
    public void Unfollow_with_nothing_to_let_go_of_is_still_a_yes()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, "Caster");

        host.Command("unfollow", Noon).Should().Contain("fly");
        told.Should().ContainSingle();
    }

    [TestMethod]
    public void The_standing_follow_is_remembered_for_a_caster_who_joins_late()
    {
        MatchHost host = Host(out _, "Caster");

        host.Followed.Following.Should().BeFalse();

        host.Command("follow Vela", Noon);
        host.Followed.Name.Should().Be("Vela");

        host.Command("unfollow", Noon);
        host.Followed.Following.Should().BeFalse();
    }

    [TestMethod]
    public void A_refused_follow_does_not_become_the_standing_one()
    {
        MatchHost host = Host(out _, "Caster");

        host.Command("follow Vela", Noon);
        host.Command("follow Nobody", Noon);

        host.Followed.Name.Should().Be("Vela");
    }

    [TestMethod]
    public void Neither_verb_writes_an_audit_record()
    {
        MatchHost host = Host(out _, "Caster");

        int before = host.Audit.Records.Count;

        host.Command("follow Vela", Noon);
        host.Command("unfollow", Noon);

        host.Audit.Records.Count.Should().Be(before);
    }

    [TestMethod]
    public void A_host_with_no_server_behind_it_answers_rather_than_throwing()
    {
        MatchHost host = new MatchHost(Configured("Caster"), TimeSpan.FromSeconds(10));

        host.Command("follow Vela", Noon).Should().NotBeNullOrWhiteSpace();
        host.Command("unfollow", Noon).Should().NotBeNullOrWhiteSpace();
    }

    private static MatchHost Host(out List<FollowBroadcast> told, out List<FollowBroadcast> given,
        params string[] ghosts)
    {
        MatchHost host = Host(out told, ghosts);

        List<PlayerReading> readings = new List<PlayerReading>();
        foreach (PlayerReading reading in host.Connected!())
        {
            if (Array.IndexOf(ghosts, reading.Name) < 0)
            {
                readings.Add(reading);
            }
        }

        for (int i = 0; i < ghosts.Length; i++)
        {
            readings.Add(Player((ushort)(4 + i), ghosts[i], new WorldPoint(-10 - i, -20, 0)));
        }

        host.Connected = () => readings;

        List<FollowBroadcast> givenSent = new List<FollowBroadcast>();
        given = givenSent;
        host.CamerasFollow = follow =>
        {
            givenSent.Add(follow);

            return true;
        };

        return host;
    }

    [TestMethod]
    public void The_unaddressed_follow_still_fans_out_to_every_ghost()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, out _, "Caster", "Producer");

        host.Command("follow Vela", Noon);

        told.Should().ContainSingle();
        told[0].Ghost.Should().BeEmpty();
    }

    [TestMethod]
    public void Every_connected_ghost_is_also_given_its_own_addressed_follow()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, out List<FollowBroadcast> given,
            "Caster", "Producer");

        host.Command("follow Vela", Noon);

        told.Should().ContainSingle();
        given.Should().HaveCount(2);
        given[0].Ghost.Should().Be("Caster");
        given[1].Ghost.Should().Be("Producer");
        given[0].Name.Should().Be("Vela");
        given.Should().OnlyContain(one => one.Following);
    }

    [TestMethod]
    public void Nobody_connected_means_nobody_is_given_an_addressed_follow()
    {
        MatchHost host = Host(out _, out List<FollowBroadcast> given, "Caster");
        host.Connected = () => Array.Empty<PlayerReading>();

        host.Command("follow Vela", Noon);

        given.Should().BeEmpty();
    }

    [TestMethod]
    public void FollowOne_tells_exactly_one_camera()
    {
        MatchHost host = Host(out List<FollowBroadcast> told, out List<FollowBroadcast> given,
            "Caster", "Producer");

        string answer = host.Command("followOne Caster Vela", Noon);

        told.Should().BeEmpty("the unaddressed fan-out would tell Producer's camera too");
        given.Should().ContainSingle();
        given[0].Ghost.Should().Be("Caster");
        given[0].Name.Should().Be("Vela");
        given[0].Following.Should().BeTrue();
        answer.Should().Contain("Vela");
    }

    [TestMethod]
    public void FollowOne_can_let_one_camera_go()
    {
        MatchHost host = Host(out _, out List<FollowBroadcast> given, "Caster", "Producer");

        host.Command("follow Vela", Noon);
        string answer = host.Command("followOne Caster", Noon);

        given.Should().HaveCount(3);
        given[2].Ghost.Should().Be("Caster");
        given[2].Following.Should().BeFalse();
        answer.Should().Contain("Caster");
    }

    [TestMethod]
    public void The_target_of_a_followOne_is_refused_like_any_other_target()
    {
        MatchHost host = Host(out _, out List<FollowBroadcast> given, "Caster");

        host.Command("followOne Caster Nobody", Noon).Should().Contain("not in the match");
        host.Command("followOne Caster Caster", Noon).Should().Contain("ghost");
        given.Should().BeEmpty();
    }

    [TestMethod]
    public void A_ghost_nobody_is_connected_as_is_refused_by_name()
    {
        MatchHost host = Host(out _, out List<FollowBroadcast> given, "Caster");

        string answer = host.Command("followOne Stranger Vela", Noon);

        answer.Should().Contain("Stranger").And.Contain("not connected");
        given.Should().BeEmpty();
    }

    [TestMethod]
    public void A_followOne_needs_its_ghost_named()
    {
        MatchHost host = Host(out _, out _, "Caster");

        host.Command("followOne", Noon).Should().Contain("followOne");
    }

    [TestMethod]
    public void A_refused_followOne_does_not_become_the_standing_one()
    {
        MatchHost host = Host(out _, out _, "Caster");

        host.Command("follow Vela", Noon);
        host.Command("followOne Caster Nobody", Noon);

        host.Followed.Name.Should().Be("Vela");
    }

    [TestMethod]
    public void A_followOne_with_no_server_to_tell_still_answers()
    {
        MatchHost host = Host(out _, out _, "Caster");
        host.CamerasFollow = null;

        host.Command("followOne Caster Vela", Noon).Should().Contain("cannot move a camera");
    }

    [TestMethod]
    public void The_addressed_word_reaches_the_same_dispatch()
    {
        MatchHost host = Host(out _, out _, "Caster");

        host.Command("followOne Caster Vela", Noon).Should().NotBeNullOrWhiteSpace();
    }
}
