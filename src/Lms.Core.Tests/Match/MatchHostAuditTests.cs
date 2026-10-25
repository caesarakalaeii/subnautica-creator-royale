using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>The tournament's evidence, as the match writes it.</summary>
[TestClass]
public class MatchHostAuditTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly DateTimeOffset Wall = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset now = Wall;

    private MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat, () => now);
    }

    private static IReadOnlyList<AuditRecordKind> Kinds(MatchHost host) =>
        host.Audit.Records.Select(record => record.Kind).ToList();

    private static MatchClockSettings Clock()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return result.Config!.Clock;
    }

    private static void PlayOut(MatchHost host)
    {
        for (int second = 1; second <= 300; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second));
        }
    }

    [TestMethod]
    public void A_match_nobody_has_started_writes_nothing()
    {
        MatchHost host = NewHost();

        host.Advance(Noon);
        host.Advance(Noon + TimeSpan.FromMinutes(5));

        host.Audit.Records.Should().BeEmpty();
    }

    [TestMethod]
    public void Starting_a_match_is_the_first_thing_in_the_log()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        AuditRecord record = host.Audit.Records.Should().ContainSingle().Subject;

        record.Sequence.Should().Be(1);
        record.Kind.Should().Be(AuditRecordKind.MatchStarted);
        record.Group.Should().Be(1);
        record.Generation.Should().Be(1);
        record.Elapsed.Should().Be(TimeSpan.Zero);
        record.TimestampUtc.Should().Be(Wall);
    }

    [TestMethod]
    public void Every_verb_that_moves_the_match_leaves_its_own_record()
    {
        MatchHost host = NewHost();

        host.Command("start", Noon);
        host.Command("pause", Noon + TimeSpan.FromSeconds(10));
        host.Command("resume", Noon + TimeSpan.FromSeconds(20));
        host.Command("reset", Noon + TimeSpan.FromSeconds(30));
        host.Command("next", Noon + TimeSpan.FromSeconds(40));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted,
            AuditRecordKind.MatchPaused,
            AuditRecordKind.MatchResumed,
            AuditRecordKind.MatchReset,
            AuditRecordKind.GroupAdvanced);
    }

    [TestMethod]
    public void A_phase_that_fell_due_before_the_verb_is_recorded_before_it()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Command("pause", Noon + TimeSpan.FromSeconds(70));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted,
            AuditRecordKind.PhaseWarned,
            AuditRecordKind.PhaseStarted,
            AuditRecordKind.MatchPaused);

        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.PhaseStarted)
            .Elapsed.Should().Be(Clock().CycleLength, "the phase keeps its own scheduled match time");
        host.Audit.Records.Last().Elapsed.Should().Be(TimeSpan.FromSeconds(70));
    }

    [TestMethod]
    public void A_phase_that_fell_due_before_a_refused_verb_is_still_recorded()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Command("resume", Noon + TimeSpan.FromSeconds(70));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted,
            AuditRecordKind.PhaseWarned,
            AuditRecordKind.PhaseStarted);
    }

    [TestMethod]
    public void A_reset_files_the_phase_in_the_match_that_fired_it()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Command("reset", Noon + TimeSpan.FromSeconds(61));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted,
            AuditRecordKind.PhaseWarned,
            AuditRecordKind.PhaseStarted,
            AuditRecordKind.MatchReset);

        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.PhaseStarted)
            .Generation.Should().Be(1, "the phase belongs to the attempt that fired it");
        host.Audit.Records.Last().Generation.Should().Be(2, "the reset belongs to the one it created");
    }

    [TestMethod]
    public void A_subject_verb_records_the_phase_that_fired_while_it_was_being_typed()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Command("ghost Caster", Noon + TimeSpan.FromSeconds(70));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted,
            AuditRecordKind.PhaseWarned,
            AuditRecordKind.PhaseStarted);
        host.Ghosts.IsGhost("Caster").Should().BeTrue("the verb still did what it was typed for");
    }

    [TestMethod]
    public void A_refused_command_is_not_evidence_of_anything()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("resume", Noon + TimeSpan.FromSeconds(5));
        host.Command("nonsense", Noon + TimeSpan.FromSeconds(6));

        Kinds(host).Should().Equal(AuditRecordKind.MatchStarted);
    }

    [TestMethod]
    public void A_phase_is_warned_and_then_fired_once_each()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        MatchClockSettings clock = Clock();

        for (int second = 1; second <= 70; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second));
        }

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted, AuditRecordKind.PhaseWarned, AuditRecordKind.PhaseStarted);

        AuditRecord warned = host.Audit.Records[1];
        AuditRecord fired = host.Audit.Records[2];

        warned.Phase.Should().Be(1);
        fired.Phase.Should().Be(1);

        warned.Elapsed.Should().Be(clock.CycleLength - clock.WarningLead);
        fired.Elapsed.Should().Be(clock.CycleLength);
    }

    [TestMethod]
    public void A_skip_records_both_the_skip_and_the_phase_it_brought_forward()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("skip", Noon + TimeSpan.FromSeconds(5));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted, AuditRecordKind.MatchSkipped,
            AuditRecordKind.PhaseWarned, AuditRecordKind.PhaseStarted);
    }

    [TestMethod]
    public void Asking_where_the_wall_is_does_not_swallow_the_phase_that_just_fired()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Zone(Noon + TimeSpan.FromSeconds(70));
        host.Advance(Noon + TimeSpan.FromSeconds(70));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted, AuditRecordKind.PhaseWarned, AuditRecordKind.PhaseStarted);
    }

    [TestMethod]
    public void Nor_does_asking_what_the_booth_should_be_told()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Telemetry(Noon + TimeSpan.FromSeconds(70), alive: 5);
        host.Status(Noon + TimeSpan.FromSeconds(70));
        host.Line(Noon + TimeSpan.FromSeconds(70));
        host.Advance(Noon + TimeSpan.FromSeconds(70));

        Kinds(host).Should().Equal(
            AuditRecordKind.MatchStarted, AuditRecordKind.PhaseWarned, AuditRecordKind.PhaseStarted);
    }

    [TestMethod]
    public void A_running_match_does_not_announce_itself_once_a_second()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Announce(Noon).Should().NotBeNull("starting is worth exactly one line");

        int announcements = 0;
        for (int second = 1; second <= 55; second++)
        {
            if (host.Announce(Noon + TimeSpan.FromSeconds(second)) != null)
            {
                announcements++;
            }
        }

        announcements.Should().Be(0, "nothing about the match changed in those 55 seconds");
    }

    [TestMethod]
    public void A_phase_firing_is_worth_a_line()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Announce(Noon);

        host.Announce(Noon + TimeSpan.FromSeconds(59)).Should().BeNull();
        host.Announce(Noon + TimeSpan.FromSeconds(61)).Should().NotBeNull("phase 1 fired");
        host.Announce(Noon + TimeSpan.FromSeconds(62)).Should().BeNull();
    }

    [TestMethod]
    public void Starting_pausing_and_resuming_are_each_worth_a_line()
    {
        MatchHost host = NewHost();

        host.Announce(Noon).Should().NotBeNull("the lobby is worth saying once");
        host.Announce(Noon).Should().BeNull();

        host.Command("start", Noon);
        host.Announce(Noon).Should().NotBeNull();

        host.Command("pause", Noon + TimeSpan.FromSeconds(5));
        host.Announce(Noon + TimeSpan.FromSeconds(5)).Should().NotBeNull();

        host.Announce(Noon + TimeSpan.FromSeconds(6)).Should().BeNull();
    }

    [TestMethod]
    public void A_match_that_runs_out_records_the_zone_closing_and_then_the_match_ending()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        PlayOut(host);

        IReadOnlyList<AuditRecordKind> kinds = Kinds(host);

        kinds.Should().ContainInOrder(AuditRecordKind.ZoneClosed, AuditRecordKind.MatchEnded);
        kinds.Count(kind => kind == AuditRecordKind.ZoneClosed).Should().Be(1);
        kinds.Count(kind => kind == AuditRecordKind.MatchEnded).Should().Be(1);
    }

    [TestMethod]
    public void A_match_the_wall_closes_on_names_nobody()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        for (int second = 1; second <= 300; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second), new[] { "Vela" });
        }

        host.Audit.Records.Last(record => record.Kind == AuditRecordKind.MatchEnded)
            .Player.Should().BeNull();
    }

    [TestMethod]
    public void A_match_that_ends_with_a_crowd_still_standing_names_nobody()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        for (int second = 1; second <= 300; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second), new[] { "Vela", "Rook" });
        }

        host.Audit.Records.Last(record => record.Kind == AuditRecordKind.MatchEnded)
            .Player.Should().BeNull();
    }

    [TestMethod]
    public void A_reset_match_can_end_all_over_again()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        PlayOut(host);

        host.Command("reset", Noon + TimeSpan.FromSeconds(400));
        host.Command("start", Noon + TimeSpan.FromSeconds(401));

        for (int second = 402; second <= 700; second++)
        {
            host.Advance(Noon + TimeSpan.FromSeconds(second));
        }

        Kinds(host).Count(kind => kind == AuditRecordKind.MatchEnded).Should().Be(2);
    }

    [TestMethod]
    public void A_record_is_stamped_with_the_match_it_belongs_to_rather_than_the_one_it_replaced()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        host.Command("reset", Noon + TimeSpan.FromSeconds(10));
        host.Command("next", Noon + TimeSpan.FromSeconds(20));

        AuditRecord started = host.Audit.Records[0];
        AuditRecord reset = host.Audit.Records[1];
        AuditRecord advanced = host.Audit.Records[2];

        started.Generation.Should().Be(1);
        reset.Generation.Should().Be(2, "the reset belongs to the attempt it created");
        advanced.Group.Should().Be(2);
        advanced.Generation.Should().Be(3, "a generation counts attempts on this server, not on this group");
    }

    [TestMethod]
    public void Records_are_stamped_with_the_clock_the_host_was_given()
    {
        MatchHost host = NewHost();

        now = Wall;
        host.Command("start", Noon);

        now = Wall.AddMinutes(3);
        host.Command("pause", Noon + TimeSpan.FromSeconds(30));

        host.Audit.Records[0].TimestampUtc.Should().Be(Wall);
        host.Audit.Records.Last().Kind.Should().Be(AuditRecordKind.MatchPaused);
        host.Audit.Records.Last().TimestampUtc.Should().Be(Wall.AddMinutes(3));
    }

    [TestMethod]
    public void What_the_match_wrote_replays_into_the_match_it_ran()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        PlayOut(host);

        MatchAudit match = AuditReplay.Of(host.Audit.Records).Matches.Should().ContainSingle().Subject;

        match.Group.Should().Be(1);
        match.Generation.Should().Be(1);
        match.PhasesFired.Should().Be(4, "three authored phases and the collapse");
        match.IsClosed.Should().BeTrue();
        match.HasEnded.Should().BeTrue();
    }

    [TestMethod]
    public void The_file_the_server_writes_replays_into_the_match_it_ran()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        PlayOut(host);

        List<string> file = new List<string> { AuditFormat.Header };
        file.AddRange(host.Audit.Records.Select(AuditFormat.Write));

        List<AuditRecord> read = new List<AuditRecord>();
        foreach (string line in file)
        {
            if (AuditFormat.TryRead(line, out AuditRecord record, out string? problem))
            {
                read.Add(record);
            }
            else
            {
                problem.Should().BeNull("only the header should fail to read, and it should do so quietly");
            }
        }

        read.Should().HaveCount(host.Audit.Records.Count, "the header is skipped and nothing else is");

        MatchAudit match = AuditReplay.Of(read).Matches.Should().ContainSingle().Subject;

        match.PhasesFired.Should().Be(4);
        match.HasEnded.Should().BeTrue();
    }

    [TestMethod]
    public void Every_line_the_match_wrote_reads_back_as_the_record_that_wrote_it()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        PlayOut(host);

        foreach (AuditRecord written in host.Audit.Records)
        {
            AuditFormat.TryRead(AuditFormat.Write(written), out AuditRecord read, out string? problem)
                .Should().BeTrue(problem);

            read.Should().Be(written);
        }
    }
}
