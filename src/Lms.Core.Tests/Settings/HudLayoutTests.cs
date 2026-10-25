using System;
using FluentAssertions;
using Lms.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Settings;

/// <summary>Placement of the HUD's two panels at any resolution.</summary>
[TestClass]
public class HudLayoutTests
{
    private static HudLayout At(int width, int height) => new HudLayout(width, height);

    [TestMethod]
    public void The_minimap_is_in_the_top_left_corner_at_every_resolution()
    {
        foreach (HudLayout screen in new[] { At(1920, 1080), At(2560, 1440), At(3840, 2160) })
        {
            screen.Minimap.Left.Should().BeApproximately(screen.Margin, 0.01f);
            screen.Minimap.Top.Should().BeApproximately(screen.Margin, 0.01f);
        }
    }

    [TestMethod]
    public void The_readout_is_in_the_top_right_corner_at_every_resolution()
    {
        foreach (HudLayout screen in new[] { At(1920, 1080), At(2560, 1440), At(3840, 2160) })
        {
            screen.Readout.Right.Should().BeApproximately(screen.Width - screen.Margin, 0.01f);
            screen.Readout.Top.Should().BeApproximately(screen.Margin, 0.01f);
        }
    }

    [TestMethod]
    public void The_hud_is_the_same_fraction_of_the_screen_at_1080p_and_1440p()
    {
        HudLayout small = At(1920, 1080);
        HudLayout large = At(2560, 1440);

        (small.Readout.Height / small.Height).Should()
            .BeApproximately(large.Readout.Height / large.Height, 0.0005f);
        (small.Minimap.Width / small.Height).Should()
            .BeApproximately(large.Minimap.Width / large.Height, 0.0005f);
    }

    [TestMethod]
    public void The_reference_resolution_draws_the_pixels_it_was_authored_at()
    {
        HudLayout screen = At(1920, 1080);

        screen.Scale.Should().Be(1f);
        screen.Minimap.Width.Should().Be(232f);
        screen.Readout.Width.Should().Be(460f);
        screen.Readout.Height.Should().Be(114f);
        screen.Margin.Should().Be(12f);
    }

    [TestMethod]
    public void A_2160p_capture_gets_twice_the_pixels_and_the_same_share_of_the_screen()
    {
        HudLayout screen = At(3840, 2160);

        screen.Scale.Should().Be(2f);
        screen.Minimap.Width.Should().Be(464f);
        screen.Font(12).Should().Be(24);
    }

    [TestMethod]
    public void The_text_grows_with_the_box_rather_than_sitting_in_a_bigger_one()
    {
        At(1920, 1080).Font(12).Should().Be(12);
        At(2560, 1440).Font(12).Should().Be(16);
        At(3840, 2160).Font(12).Should().Be(24);
    }

    [TestMethod]
    public void The_size_the_text_is_drawn_at_today_is_what_gets_scaled()
    {
        At(1920, 1080).Font(11).Should().Be(11);
        At(1920, 1080).Font(18).Should().Be(18);
    }

    [TestMethod]
    public void It_stops_shrinking_before_it_stops_being_readable()
    {
        HudLayout small = At(1280, 720);

        small.Scale.Should().BeApproximately(0.67f, 0.001f);
        small.Font(12).Should().BeGreaterThanOrEqualTo(9);
        small.Font(3).Should().Be(9, "the clamp must not be undone by rounding down");
    }

    [TestMethod]
    public void It_stops_growing_too_so_a_4k_hud_does_not_eat_the_game()
    {
        At(7680, 4320).Scale.Should().Be(2f, "a HUD readable from the sofa is not the trade");
    }

    [TestMethod]
    public void The_two_panels_do_not_touch_at_any_resolution_anybody_plays_at()
    {
        foreach (HudLayout screen in new[]
                 {
                     At(1280, 720), At(1600, 900), At(1920, 1080), At(2560, 1440), At(3440, 1440),
                     At(3840, 2160),
                 })
        {
            screen.PanelsCollide.Should().BeFalse($"{screen}");
        }
    }

    [TestMethod]
    public void A_window_too_narrow_for_both_says_so_rather_than_overlapping_them()
    {
        At(700, 1080).PanelsCollide.Should().BeTrue();
    }

    [TestMethod]
    public void A_screen_with_no_pixels_is_refused_rather_than_divided_by()
    {
        Action none = () => new HudLayout(1920, 0);

        none.Should().Throw<ArgumentOutOfRangeException>();
    }
}
