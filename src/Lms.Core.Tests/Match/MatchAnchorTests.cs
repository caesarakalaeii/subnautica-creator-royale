using System;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The whole of a match's shared state.</summary>
[TestClass]
public class MatchAnchorTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);

    [TestMethod]
    public void A_match_nobody_has_started_is_in_the_lobby_and_no_time_has_passed()
    {
        MatchAnchor lobby = MatchAnchor.Lobby;

        lobby.State.Should().Be(MatchClockState.NotStarted);
        lobby.Group.Should().Be(1);
        lobby.Generation.Should().Be(1);
        lobby.ElapsedAt(Noon).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void A_started_match_is_as_old_as_the_world_clock_says()
    {
        MatchAnchor running = MatchAnchor.Lobby.Started(Noon);

        running.State.Should().Be(MatchClockState.Running);
        running.ElapsedAt(Noon).Should().Be(TimeSpan.Zero);
        running.ElapsedAt(Noon + TimeSpan.FromMinutes(7)).Should().Be(TimeSpan.FromMinutes(7));
    }

    [TestMethod]
    public void A_paused_match_stops_ageing_and_a_resumed_one_does_not_lose_the_time_it_ran()
    {
        MatchAnchor anchor = MatchAnchor.Lobby.Started(Noon).Paused(Noon + TimeSpan.FromMinutes(3));

        anchor.State.Should().Be(MatchClockState.Paused);
        anchor.ElapsedAt(Noon + TimeSpan.FromMinutes(9)).Should().Be(TimeSpan.FromMinutes(3),
            "six minutes of pause are not six minutes of match");

        anchor = anchor.Resumed(Noon + TimeSpan.FromMinutes(9));

        anchor.State.Should().Be(MatchClockState.Running);
        anchor.ElapsedAt(Noon + TimeSpan.FromMinutes(9)).Should().Be(TimeSpan.FromMinutes(3));
        anchor.ElapsedAt(Noon + TimeSpan.FromMinutes(10)).Should().Be(TimeSpan.FromMinutes(4));
    }

    [TestMethod]
    public void Skipping_pulls_the_epoch_back_so_the_phase_falls_due_now()
    {
        MatchAnchor anchor = MatchAnchor.Lobby.Started(Noon);

        anchor = anchor.Skipped(TimeSpan.FromMinutes(4));

        anchor.ElapsedAt(Noon).Should().Be(TimeSpan.FromMinutes(4));
        anchor.ElapsedAt(Noon + TimeSpan.FromMinutes(1)).Should().Be(TimeSpan.FromMinutes(5));
    }

    [TestMethod]
    public void A_reset_is_a_new_generation_of_the_same_group_waiting_in_the_lobby()
    {
        MatchAnchor anchor = MatchAnchor.Lobby.Started(Noon).Reset();

        anchor.Group.Should().Be(1);
        anchor.Generation.Should().Be(2);
        anchor.State.Should().Be(MatchClockState.NotStarted);
        anchor.ElapsedAt(Noon + TimeSpan.FromMinutes(5)).Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void The_next_group_is_a_new_group_and_a_new_generation()
    {
        MatchAnchor anchor = MatchAnchor.Lobby.Started(Noon).NextGroup();

        anchor.Group.Should().Be(2);
        anchor.Generation.Should().Be(2);
        anchor.State.Should().Be(MatchClockState.NotStarted);
    }

    [TestMethod]
    public void Every_move_takes_the_next_sequence_number()
    {
        MatchAnchor anchor = MatchAnchor.Lobby;
        anchor.Sequence.Should().Be(0);

        anchor = anchor.Started(Noon);
        anchor.Sequence.Should().Be(1);

        anchor = anchor.Paused(Noon).Resumed(Noon).Reset();
        anchor.Sequence.Should().Be(4);
    }

    [TestMethod]
    public void An_anchor_survives_being_written_down_and_read_back()
    {
        MatchAnchor written = MatchAnchor.Lobby
            .Started(Noon)
            .Paused(Noon + TimeSpan.FromMinutes(2))
            .Resumed(Noon + TimeSpan.FromMinutes(5))
            .NextGroup()
            .Started(Noon + TimeSpan.FromMinutes(6));

        MatchAnchor.TryParse(written.ToKey(), out MatchAnchor read).Should().BeTrue();

        read.Should().Be(written);
        read.ElapsedAt(Noon + TimeSpan.FromMinutes(9)).Should().Be(TimeSpan.FromMinutes(3));
    }

    [TestMethod]
    public void A_paused_anchor_survives_being_written_down_too()
    {
        MatchAnchor written = MatchAnchor.Lobby.Started(Noon).Paused(Noon + TimeSpan.FromMinutes(3));

        MatchAnchor.TryParse(written.ToKey(), out MatchAnchor read).Should().BeTrue();

        read.Should().Be(written);
        read.State.Should().Be(MatchClockState.Paused);
        read.ElapsedAt(Noon + TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromMinutes(3));
    }

    [TestMethod]
    public void A_key_names_itself_so_it_can_be_picked_out_of_everything_else_in_the_save()
    {
        string key = MatchAnchor.Lobby.Started(Noon).ToKey();

        key.Should().StartWith(MatchAnchor.KeyPrefix);
        MatchAnchor.IsAnchorKey(key).Should().BeTrue();
        MatchAnchor.IsAnchorKey("Goal_Aurora_Explosion").Should().BeFalse();
    }

    [TestMethod]
    public void A_key_carries_no_character_a_story_goal_cannot()
    {
        string key = MatchAnchor.Lobby.Started(Noon).Paused(Noon + TimeSpan.FromMinutes(1)).ToKey();

        key.Should().MatchRegex("^[A-Za-z0-9.-]+$");
    }

    [TestMethod]
    public void Something_that_is_not_an_anchor_is_refused_rather_than_guessed_at()
    {
        MatchAnchor.TryParse("Goal_Aurora_Explosion", out _).Should().BeFalse();
        MatchAnchor.TryParse("LmsMatch.nonsense", out _).Should().BeFalse();
        MatchAnchor.TryParse("LmsMatch.1.1.1.X.0.0.0", out _).Should().BeFalse("X is not a state");
        MatchAnchor.TryParse(string.Empty, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_key_from_a_newer_build_with_more_in_it_still_reads()
    {
        string key = MatchAnchor.Lobby.Started(Noon).ToKey() + ".7";

        MatchAnchor.TryParse(key, out MatchAnchor read).Should().BeTrue();
        read.ElapsedAt(Noon + TimeSpan.FromMinutes(2)).Should().Be(TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Every_move_an_operator_makes_counts()
    {
        MatchAnchor started = MatchAnchor.Lobby.Started(Noon);

        started.Sequence.Should().Be(1);
        started.Reset().Sequence.Should().Be(2);
    }

    [TestMethod]
    public void An_anchor_from_before_the_world_started_is_refused()
    {
        Action backwards = () => MatchAnchor.Lobby.Started(TimeSpan.FromSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }
}
