using System;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>A settings-tab button pressed twice is one intention.</summary>
[TestClass]
public class ButtonPressesTests
{
    private static readonly TimeSpan Click = TimeSpan.FromSeconds(0.25);

    [TestMethod]
    public void A_double_click_on_the_same_button_runs_once()
    {
        ButtonPresses presses = new ButtonPresses();

        presses.Admit(AdminCommand.Skip, TimeSpan.FromSeconds(60)).Should().BeTrue();
        presses.Admit(AdminCommand.Skip, TimeSpan.FromSeconds(60) + Click).Should().BeFalse();
    }

    [TestMethod]
    public void The_same_button_pressed_again_a_second_later_runs_again()
    {
        ButtonPresses presses = new ButtonPresses();

        presses.Admit(AdminCommand.Skip, TimeSpan.FromSeconds(60));

        presses.Admit(AdminCommand.Skip, TimeSpan.FromSeconds(60) + ButtonPresses.Window).Should().BeTrue();
    }

    [TestMethod]
    public void A_different_button_is_never_held_back()
    {
        ButtonPresses presses = new ButtonPresses();

        presses.Admit(AdminCommand.Pause, TimeSpan.FromSeconds(60));

        presses.Admit(AdminCommand.Resume, TimeSpan.FromSeconds(60) + Click).Should().BeTrue();
    }
}
