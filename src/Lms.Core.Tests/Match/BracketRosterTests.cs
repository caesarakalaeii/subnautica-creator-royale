using System;
using System.Collections.Generic;
using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Who the bracket says is in the running group.</summary>
[TestClass]
public class BracketRosterTests
{
    [TestMethod]
    public void A_bracket_with_nobody_in_it_changes_no_policy()
    {
        BracketRoster bracket = new BracketRoster(3, Array.Empty<string>());

        bracket.IsContesting("Ada").Should().BeFalse();
        bracket.Count.Should().Be(0);
    }

    [TestMethod]
    public void A_member_of_the_playing_group_is_contesting()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "Ada", "Bea" });

        bracket.IsContesting("Ada").Should().BeTrue();
        bracket.IsContesting("Bea").Should().BeTrue();
        bracket.IsContesting("Caster").Should().BeFalse();
    }

    [TestMethod]
    public void A_name_typed_into_the_bracket_matches_the_one_a_client_reports_whatever_its_case()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "ada" });

        bracket.IsContesting("Ada").Should().BeTrue();
        bracket.IsContesting("ADA").Should().BeTrue();
    }

    [TestMethod]
    public void Nothing_with_no_name_is_contesting()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "Ada" });

        bracket.IsContesting(null).Should().BeFalse();
        bracket.IsContesting(string.Empty).Should().BeFalse();
        bracket.IsContesting("   ").Should().BeFalse();
    }

    [TestMethod]
    public void The_next_replacement_supersedes_the_whole_roster()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "Ada", "Bea" });

        bracket.Replace(3, new[] { "Bea", "Cy" });

        bracket.IsContesting("Ada").Should().BeFalse();
        bracket.IsContesting("Bea").Should().BeTrue();
        bracket.IsContesting("Cy").Should().BeTrue();
    }

    [TestMethod]
    public void A_group_that_moved_on_carries_its_new_number()
    {
        BracketRoster bracket = new BracketRoster(1, new[] { "Ada" });

        bracket.Replace(2, new[] { "Bea" });

        bracket.Group.Should().Be(2);
    }

    [TestMethod]
    public void An_empty_replacement_is_the_guard_rather_than_an_instruction()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "Ada" });

        bracket.Replace(3, null);

        bracket.Count.Should().Be(0);
        bracket.IsContesting("Ada").Should().BeFalse();
    }

    [TestMethod]
    public void The_members_read_back_in_one_order_so_a_status_line_is_the_same_line_twice()
    {
        BracketRoster bracket = new BracketRoster(3, new[] { "Zed", "Ada", "Mo" });

        bracket.Members.Should().Equal("Ada", "Mo", "Zed");
    }

    [TestMethod]
    public void A_status_line_says_the_group_and_who_is_in_it()
    {
        new BracketRoster(3, new[] { "Ada", "Bea" }).ToString().Should().Be("group 3: Ada, Bea");
    }

    [TestMethod]
    public void A_status_line_of_an_empty_bracket_says_there_is_no_policy()
    {
        new BracketRoster(3, Array.Empty<string>()).ToString().Should().Be("no bracket");
    }
}
