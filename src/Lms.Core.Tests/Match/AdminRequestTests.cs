using FluentAssertions;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>A client asking the server to run an admin verb.</summary>
[TestClass]
public class AdminRequestTests
{
    [TestMethod]
    [DataRow(AdminCommand.Status)]
    [DataRow(AdminCommand.Start)]
    [DataRow(AdminCommand.Pause)]
    [DataRow(AdminCommand.Resume)]
    [DataRow(AdminCommand.Skip)]
    [DataRow(AdminCommand.Reset)]
    [DataRow(AdminCommand.NextGroup)]
    public void Every_verb_survives_the_round_trip(AdminCommand command)
    {
        AdminRequest.TryDecode(AdminRequest.Encode(command), out AdminCommand read).Should().BeTrue();

        read.Should().Be(command);
    }

    [TestMethod]
    public void The_wire_form_is_the_header_and_the_word_the_operator_types()
    {
        AdminRequest.Encode(AdminCommand.NextGroup).Should().Be("lms-admin/1\tverb=next");
    }

    [TestMethod]
    public void A_report_is_not_an_admin_request()
    {
        AdminRequest.TryDecode("lms-player/1\tverb=start", out _).Should().BeFalse();
        AdminRequest.TryDecode("lms-events/1\tgen=1", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_version_this_build_does_not_know_is_refused_rather_than_guessed_at()
    {
        AdminRequest.TryDecode("lms-admin/2\tverb=start", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_verb_this_build_does_not_know_is_refused()
    {
        AdminRequest.TryDecode("lms-admin/1\tverb=abandon", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_request_with_no_verb_at_all_is_refused()
    {
        AdminRequest.TryDecode("lms-admin/1", out _).Should().BeFalse();
        AdminRequest.TryDecode("lms-admin/1\tverb=", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Nothing_at_all_is_refused()
    {
        AdminRequest.TryDecode(null, out _).Should().BeFalse();
        AdminRequest.TryDecode("", out _).Should().BeFalse();
        AdminRequest.TryDecode("   ", out _).Should().BeFalse();
    }

    [TestMethod]
    public void An_unknown_field_does_not_spoil_a_request_this_build_understands()
    {
        AdminRequest.TryDecode("lms-admin/1\tverb=skip\treason=chaos", out AdminCommand read)
            .Should().BeTrue();

        read.Should().Be(AdminCommand.Skip);
    }

    [TestMethod]
    public void The_header_is_recognisable_on_its_own()
    {
        AdminRequest.IsRequest("lms-admin/1\tverb=start").Should().BeTrue();
        AdminRequest.IsRequest("lms-player/1\tsomething=1").Should().BeFalse();
        AdminRequest.IsRequest(null).Should().BeFalse();
    }
}
