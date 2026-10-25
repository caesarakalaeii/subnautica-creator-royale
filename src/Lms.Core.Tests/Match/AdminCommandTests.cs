using System;
using System.Linq;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The words an operator types.</summary>
[TestClass]
public class AdminCommandTests
{
    private static AdminCommand Parse(string word)
    {
        AdminCommands.TryParse(word, out AdminCommand command).Should().BeTrue();
        return command;
    }

    [TestMethod]
    [DataRow("status", AdminCommand.Status)]
    [DataRow("start", AdminCommand.Start)]
    [DataRow("pause", AdminCommand.Pause)]
    [DataRow("resume", AdminCommand.Resume)]
    [DataRow("skip", AdminCommand.Skip)]
    [DataRow("reset", AdminCommand.Reset)]
    [DataRow("next", AdminCommand.NextGroup)]
    public void Every_documented_verb_parses(string word, AdminCommand expected)
    {
        AdminCommands.TryParse(word, out AdminCommand command).Should().BeTrue();

        command.Should().Be(expected);
    }

    [TestMethod]
    public void Casing_and_stray_whitespace_do_not_matter()
    {
        AdminCommands.TryParse("  SkIp  ", out AdminCommand command).Should().BeTrue();

        command.Should().Be(AdminCommand.Skip);
    }

    [TestMethod]
    public void Next_and_nextgroup_are_the_same_command()
    {
        AdminCommands.TryParse("nextgroup", out AdminCommand command).Should().BeTrue();

        command.Should().Be(AdminCommand.NextGroup);
    }

    [TestMethod]
    public void Typing_the_command_with_no_verb_asks_where_the_match_is()
    {
        AdminCommands.TryParse(string.Empty, out AdminCommand command).Should().BeTrue();

        command.Should().Be(AdminCommand.Status);
    }

    [TestMethod]
    public void A_verb_nobody_implemented_is_refused_rather_than_guessed_at()
    {
        AdminCommands.TryParse("stop", out AdminCommand _).Should().BeFalse();
    }

    [TestMethod]
    public void Every_command_appears_in_the_usage_line()
    {
        foreach (AdminCommand command in Enum.GetValues(typeof(AdminCommand)).Cast<AdminCommand>())
        {
            AdminCommands.Verbs.Any(verb => Parse(verb) == command)
                .Should().BeTrue($"{command} has to be typeable");
        }

        foreach (string verb in AdminCommands.Verbs)
        {
            AdminCommands.Usage.Should().Contain(verb);
        }
    }

    [TestMethod]
    public void Every_command_has_a_word_that_reads_back_as_itself()
    {
        foreach (AdminCommand command in Enum.GetValues(typeof(AdminCommand)).Cast<AdminCommand>())
        {
            string word = AdminCommands.Word(command);

            word.Should().NotBeNullOrWhiteSpace();
            Parse(word).Should().Be(command, $"{command} is spelled '{word}'");
        }
    }

    [TestMethod]
    public void There_is_exactly_one_word_per_command()
    {
        AdminCommands.Verbs.Should().HaveCount(Enum.GetValues(typeof(AdminCommand)).Length);
    }

    [TestMethod]
    public void Podclear_all_is_a_second_form_and_leaves_bare_podclear_alone()
    {
        Parse("podclear").Should().Be(AdminCommand.PodClear);
        Parse("podclear all").Should().Be(AdminCommand.PodClearAll);
    }

    [TestMethod]
    public void The_space_in_a_two_word_verb_is_whatever_was_typed()
    {
        Parse("  PODCLEAR   all ").Should().Be(AdminCommand.PodClearAll);
        Parse("podclear\tall").Should().Be(AdminCommand.PodClearAll);
    }

    [TestMethod]
    public void The_form_that_removes_every_pod_has_no_button()
    {
        AdminCommands.Buttons.Should().NotContain(AdminCommands.Word(AdminCommand.PodClearAll));
    }

    [TestMethod]
    public void A_verb_that_needs_a_name_has_no_button_either()
    {
        AdminCommands.Buttons.Should().NotContain("ghost");
        AdminCommands.Buttons.Should().NotContain("unghost");

        AdminCommands.Buttons.Should().Equal(AdminCommands.Verbs.Where(verb =>
            !AdminCommands.MustBeTyped(Parse(verb)) && !AdminCommands.TakesASubject(Parse(verb))));
    }

    [TestMethod]
    public void A_verb_about_a_player_carries_the_name_after_it()
    {
        AdminCommands.TryParse("ghost Caster", out AdminCommand command, out string subject)
            .Should().BeTrue();

        command.Should().Be(AdminCommand.Ghost);
        subject.Should().Be("Caster");
    }

    [TestMethod]
    public void And_unghost_reads_the_same_way()
    {
        AdminCommands.TryParse("  UNGHOST   Caster ", out AdminCommand command, out string subject)
            .Should().BeTrue();

        command.Should().Be(AdminCommand.Unghost);
        subject.Should().Be("Caster");
    }

    [TestMethod]
    public void The_verb_with_no_name_after_it_still_parses()
    {
        AdminCommands.TryParse("ghost", out AdminCommand command, out string subject)
            .Should().BeTrue();

        command.Should().Be(AdminCommand.Ghost);
        subject.Should().BeEmpty();
    }

    [TestMethod]
    public void A_verb_that_takes_no_name_still_refuses_one()
    {
        AdminCommands.TryParse("start now", out AdminCommand _, out string _).Should().BeFalse();
        AdminCommands.TryParse("podclear everything", out AdminCommand _, out string _)
            .Should().BeFalse();
    }

    [TestMethod]
    public void Podclear_all_is_a_verb_rather_than_podclear_with_a_subject()
    {
        AdminCommands.TryParse("podclear all", out AdminCommand command, out string subject)
            .Should().BeTrue();

        command.Should().Be(AdminCommand.PodClearAll);
        subject.Should().BeEmpty();
    }

    [TestMethod]
    public void The_usage_line_shows_where_the_name_goes()
    {
        AdminCommands.Usage.Should().Contain("ghost <player>");
        AdminCommands.Usage.Should().Contain("unghost <player>");
    }

    [TestMethod]
    public void Round_force_is_a_second_form_and_leaves_bare_round_alone()
    {
        Parse("round").Should().Be(AdminCommand.Round);
        Parse("round force").Should().Be(AdminCommand.RoundForce);
        Parse("  ROUND   force ").Should().Be(AdminCommand.RoundForce);
    }

    [TestMethod]
    public void The_form_that_prepares_the_wrong_number_of_players_has_no_button()
    {
        AdminCommands.Buttons.Should().Contain("round");
        AdminCommands.Buttons.Should().NotContain(AdminCommands.Word(AdminCommand.RoundForce));
    }

    [TestMethod]
    public void Every_button_is_still_a_verb_that_reads_back_as_itself()
    {
        foreach (string verb in AdminCommands.Buttons)
        {
            AdminCommands.Word(Parse(verb)).Should().Be(verb);
        }
    }

    [TestMethod]
    public void FollowOne_carries_two_names_one_for_each_half_of_the_instruction()
    {
        AdminCommands.TryParse("followOne Caster Vela", out AdminCommand command, out string subject,
            out string addressee).Should().BeTrue();

        command.Should().Be(AdminCommand.FollowOne);
        addressee.Should().Be("Caster");
        subject.Should().Be("Vela");
    }

    [TestMethod]
    public void FollowOne_far_apart_names_parse_to_the_same_places()
    {
        AdminCommands.TryParse("  FOLLOWONE   Caster    Vela ", out AdminCommand command,
            out string subject, out string addressee).Should().BeTrue();

        command.Should().Be(AdminCommand.FollowOne);
        addressee.Should().Be("Caster");
        subject.Should().Be("Vela");
    }

    [TestMethod]
    public void FollowOne_with_one_name_still_parses_and_leaves_the_target_empty()
    {
        AdminCommands.TryParse("followOne Caster", out AdminCommand command, out string subject,
            out string addressee).Should().BeTrue();

        command.Should().Be(AdminCommand.FollowOne);
        addressee.Should().Be("Caster");
        subject.Should().BeEmpty();
    }

    [TestMethod]
    public void Bare_follow_parses_with_no_addressee()
    {
        AdminCommands.TryParse("follow Vela", out AdminCommand command, out string subject,
            out string addressee).Should().BeTrue();

        command.Should().Be(AdminCommand.Follow);
        subject.Should().Be("Vela");
        addressee.Should().BeEmpty();
    }

    [TestMethod]
    public void The_usage_line_shows_where_both_names_go()
    {
        AdminCommands.Usage.Should().Contain("followOne <ghost> <target>");
    }

    [TestMethod]
    public void FollowOne_needs_typing_because_it_takes_two_names()
    {
        AdminCommands.TakesASubject(AdminCommand.FollowOne).Should().BeTrue();
        AdminCommands.Buttons.Should().NotContain("followOne");
    }
}
