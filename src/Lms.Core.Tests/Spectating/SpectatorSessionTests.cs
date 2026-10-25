using System;
using FluentAssertions;
using Lms.Core.Spectating;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Spectating;

/// <summary>The state machine for a player the match is finished with.</summary>
[TestClass]
public class SpectatorSessionTests
{
    private static readonly TimeSpan DeathCam = TimeSpan.FromSeconds(5);

    private static SpectatorSession NewSession(bool spectate = true) =>
        new SpectatorSession(new SpectatorPolicy(DeathCam, spectate));

    private static SpectatorSession Flying()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();
        session.Update(DeathCam);

        return session;
    }

    [TestMethod]
    public void A_player_who_is_still_in_is_playing_and_nothing_is_different_about_them()
    {
        SpectatorSession session = NewSession();

        session.State.Should().Be(SpectatorState.Playing);
        session.IsPlaying.Should().BeTrue();
        session.TakesZoneDamage.Should().BeTrue();
        session.CanBeCaged.Should().BeTrue();
        session.Update(TimeSpan.FromSeconds(30)).Should().Be(SpectatorChange.Nothing);
    }

    [TestMethod]
    public void Being_eliminated_starts_the_death_cam_the_game_already_plays()
    {
        SpectatorSession session = NewSession();

        session.Eliminate().Should().Be(SpectatorChange.DeathCamStarted);
        session.State.Should().Be(SpectatorState.DeathCam);
        session.UntilFreeCamera.Should().Be(DeathCam);
    }

    [TestMethod]
    public void An_eliminated_player_is_out_of_the_match_from_the_moment_they_go()
    {
        SpectatorSession session = NewSession();

        session.Eliminate();

        session.IsPlaying.Should().BeFalse();
        session.TakesZoneDamage.Should().BeFalse();
        session.CanBeCaged.Should().BeFalse();
    }

    [TestMethod]
    public void The_camera_is_handed_over_when_the_death_cam_runs_out()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();

        session.Update(TimeSpan.FromSeconds(4)).Should().Be(SpectatorChange.Nothing);
        session.UntilFreeCamera.Should().Be(TimeSpan.FromSeconds(1));

        session.Update(TimeSpan.FromSeconds(1)).Should().Be(SpectatorChange.FreeCameraTaken);
        session.State.Should().Be(SpectatorState.Spectating);
        session.UntilFreeCamera.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void The_handover_is_announced_once_and_then_never_again()
    {
        SpectatorSession session = Flying();

        session.Update(TimeSpan.FromSeconds(1)).Should().Be(SpectatorChange.Nothing);
        session.Update(TimeSpan.FromMinutes(10)).Should().Be(SpectatorChange.Nothing);
        session.State.Should().Be(SpectatorState.Spectating);
    }

    [TestMethod]
    public void Dying_twice_is_dying_once()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();
        session.Update(TimeSpan.FromSeconds(3));

        session.Eliminate().Should().Be(SpectatorChange.Nothing);
        session.UntilFreeCamera.Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public void A_reset_puts_a_spectator_back_in_the_match()
    {
        SpectatorSession session = Flying();

        session.Restore().Should().Be(SpectatorChange.Restored);
        session.State.Should().Be(SpectatorState.Playing);
        session.TakesZoneDamage.Should().BeTrue();
    }

    [TestMethod]
    public void A_reset_in_the_middle_of_a_death_cam_still_puts_them_back()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();
        session.Update(TimeSpan.FromSeconds(2));

        session.Restore().Should().Be(SpectatorChange.Restored);
        session.State.Should().Be(SpectatorState.Playing);
    }

    [TestMethod]
    public void Restoring_somebody_who_never_died_changes_nothing()
    {
        NewSession().Restore().Should().Be(SpectatorChange.Nothing);
    }

    [TestMethod]
    public void With_spectating_switched_off_the_death_cam_ends_in_being_removed()
    {
        SpectatorSession session = NewSession(spectate: false);
        session.Eliminate();

        session.Update(DeathCam).Should().Be(SpectatorChange.Removed);
        session.State.Should().Be(SpectatorState.Removed);
        session.Update(TimeSpan.FromSeconds(1)).Should().Be(SpectatorChange.Nothing);
    }

    [TestMethod]
    public void Somebody_the_fallback_already_removed_is_not_put_back_by_a_reset()
    {
        SpectatorSession session = NewSession(spectate: false);
        session.Eliminate();
        session.Update(DeathCam);

        session.Restore().Should().Be(SpectatorChange.Nothing);
        session.State.Should().Be(SpectatorState.Removed);
    }

    [TestMethod]
    public void A_death_cam_of_no_length_hands_over_on_the_next_frame_rather_than_never()
    {
        SpectatorSession session = new SpectatorSession(new SpectatorPolicy(TimeSpan.Zero, spectate: true));
        session.Eliminate();

        session.Update(TimeSpan.FromSeconds(0.02)).Should().Be(SpectatorChange.FreeCameraTaken);
    }

    [TestMethod]
    public void Match_time_does_not_run_backwards_here_either()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();

        Action backwards = () => session.Update(TimeSpan.FromSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void A_death_cam_cannot_be_negative()
    {
        Action backwards = () => new SpectatorPolicy(TimeSpan.FromSeconds(-1), spectate: true);

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void New_rules_do_not_put_an_eliminated_player_back_in_the_match()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();

        session.Adopt(new SpectatorPolicy(TimeSpan.FromSeconds(5), spectate: true));

        session.IsPlaying.Should().BeFalse();
        session.State.Should().Be(SpectatorState.DeathCam);
    }

    [TestMethod]
    public void New_rules_change_a_death_cam_that_is_already_running()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();
        session.Update(TimeSpan.FromSeconds(2)).Should().Be(SpectatorChange.Nothing);

        session.Adopt(new SpectatorPolicy(TimeSpan.FromSeconds(3), spectate: true));

        session.Update(TimeSpan.FromSeconds(1.5)).Should().Be(SpectatorChange.FreeCameraTaken);
    }

    [TestMethod]
    public void New_rules_that_switch_spectating_off_are_taken_at_the_handover()
    {
        SpectatorSession session = NewSession(spectate: true);
        session.Eliminate();

        session.Adopt(new SpectatorPolicy(DeathCam, spectate: false));

        session.Update(DeathCam).Should().Be(SpectatorChange.Removed);
        session.State.Should().Be(SpectatorState.Removed);
    }

    [TestMethod]
    public void A_spectator_whose_rules_changed_is_still_put_back_by_the_next_match()
    {
        SpectatorSession session = Flying();

        session.Adopt(new SpectatorPolicy(DeathCam, spectate: true));

        session.Restore().Should().Be(SpectatorChange.Restored);
        session.State.Should().Be(SpectatorState.Playing);
    }

    [TestMethod]
    public void A_session_refuses_rules_that_are_not_there()
    {
        SpectatorSession session = NewSession();

        Action nothing = () => session.Adopt(null!);

        nothing.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void A_player_told_they_are_already_out_goes_straight_to_the_camera()
    {
        SpectatorSession session = NewSession();

        session.AlreadyOut().Should().Be(SpectatorChange.FreeCameraTaken);
        session.State.Should().Be(SpectatorState.Spectating);
        session.TakesZoneDamage.Should().BeFalse();
        session.CanBeCaged.Should().BeFalse();
    }

    [TestMethod]
    public void And_the_fallback_still_sees_them_out_instead()
    {
        SpectatorSession session = NewSession(spectate: false);

        session.AlreadyOut().Should().Be(SpectatorChange.Removed);
        session.State.Should().Be(SpectatorState.Removed);
    }

    [TestMethod]
    public void Being_told_twice_is_not_a_second_handover()
    {
        SpectatorSession session = NewSession();
        session.AlreadyOut();

        session.AlreadyOut().Should().Be(SpectatorChange.Nothing);
        session.State.Should().Be(SpectatorState.Spectating);
    }

    [TestMethod]
    public void And_it_does_not_interrupt_a_death_cam_somebody_is_halfway_through()
    {
        SpectatorSession session = NewSession();
        session.Eliminate();

        session.AlreadyOut().Should().Be(SpectatorChange.Nothing);
        session.State.Should().Be(SpectatorState.DeathCam);
    }

    [TestMethod]
    public void And_a_reset_still_puts_them_back()
    {
        SpectatorSession session = NewSession();
        session.AlreadyOut();

        session.Restore().Should().Be(SpectatorChange.Restored);
        session.State.Should().Be(SpectatorState.Playing);
    }

    [TestMethod]
    public void The_default_death_cam_is_the_one_the_game_already_plays()
    {
        SpectatorPolicy.Default.DeathCam.Should().Be(TimeSpan.FromSeconds(5));
        SpectatorPolicy.Default.Spectate.Should().BeTrue();
    }
}
