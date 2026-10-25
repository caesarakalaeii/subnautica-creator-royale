using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Lms.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Settings;

/// <summary>The settings tab catalogue offered to one player.</summary>
[TestClass]
public class ClientPreferencesTests
{
    [TestMethod]
    public void Every_key_is_unique_because_a_key_is_where_the_value_is_stored()
    {
        string[] keys = ClientPreferences.All.Select(preference => preference.Key).ToArray();

        keys.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void Every_key_is_ours_by_name()
    {
        ClientPreferences.All.Should().OnlyContain(preference => preference.Key.StartsWith("Lms."));
    }

    [TestMethod]
    public void Every_preference_says_something_in_the_menu()
    {
        ClientPreferences.All.Should().OnlyContain(preference => !string.IsNullOrWhiteSpace(preference.Label));
    }

    [TestMethod]
    public void Nothing_is_off_until_somebody_turns_it_off()
    {
        ClientPreferences.All
            .Where(preference => preference.Kind == ClientPreferenceKind.Toggle)
            .Should().OnlyContain(preference => preference.IsOnByDefault);
    }

    [TestMethod]
    public void A_slider_has_bounds_it_can_actually_move_between()
    {
        foreach (ClientPreference slider in ClientPreferences.All.Where(p => p.Kind == ClientPreferenceKind.Slider))
        {
            slider.Minimum.Should().BeLessThan(slider.Maximum, slider.Key);
            slider.Step.Should().BeGreaterThan(0f, slider.Key);
            slider.Default.Should().BeInRange(slider.Minimum, slider.Maximum, slider.Key);
        }
    }

    [TestMethod]
    public void The_warning_can_be_silenced_without_being_removed()
    {
        ClientPreference volume = ClientPreferences.All.Single(p => p.Key == ClientPreferences.WarningVolumeKey);

        volume.Kind.Should().Be(ClientPreferenceKind.Slider);
        volume.Minimum.Should().Be(0f);
        volume.Default.Should().Be(volume.Maximum);
    }

    [TestMethod]
    public void There_is_a_button_for_every_verb_the_tab_is_allowed_to_offer()
    {
        ClientPreferences.AdminButtons.Should().BeEquivalentTo(
            AdminCommands.Buttons,
            options => options.WithStrictOrdering());
    }

    [TestMethod]
    public void Every_button_is_a_verb_the_server_will_accept()
    {
        foreach (string verb in ClientPreferences.AdminButtons)
        {
            AdminCommands.TryParse(verb, out _).Should().BeTrue(verb);
        }
    }

    [TestMethod]
    public void The_two_headings_are_told_apart_because_one_of_them_changes_a_match()
    {
        ClientPreferences.Heading.Should().NotBe(ClientPreferences.AdminHeading);
        ClientPreferences.Heading.Should().NotBeNullOrWhiteSpace();
        ClientPreferences.AdminHeading.Should().NotBeNullOrWhiteSpace();
    }
}
