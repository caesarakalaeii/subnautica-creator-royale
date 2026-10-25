using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>Settling an absence on the way back in.</summary>
[TestClass]
public class MatchHostArrearsTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly ZoneSphere Wall = new ZoneSphere(new WorldPoint(200, -15, 100), 1400);

    private static readonly WorldPoint Inside = new WorldPoint(750, -110, 450);

    private static readonly WorldPoint Outside = new WorldPoint(200, -15, 1700);

    private readonly PlayerRoster roster = new PlayerRoster();

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static PlayerReading Reading(WorldPoint where, bool alive = true) =>
        new PlayerReading(1, "Vela", where, alive, 92, 40, 70, 65);

    private void Tick(MatchHost host, TimeSpan worldNow, params PlayerReading[] present) =>
        host.Record(roster.Update(present, host.Zone(worldNow).Sphere, host.Generation).Changes);

    private static TimeSpan At(double seconds) => Noon + TimeSpan.FromSeconds(seconds);

    private MatchHost Left(double leftAt, WorldPoint where)
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Tick(host, Noon, Reading(where));
        Tick(host, At(leftAt), Reading(where));
        Tick(host, At(leftAt));

        return host;
    }

    [TestMethod]
    public void A_player_the_log_never_saw_leave_owes_nothing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);
        Tick(host, Noon, Reading(Outside));

        host.Arrears("Vela", At(300)).Should().BeNull();
    }

    [TestMethod]
    public void A_name_the_log_has_never_heard_of_owes_nothing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Arrears("Someone", At(300)).Should().BeNull();
    }

    [TestMethod]
    public void A_player_who_dropped_inside_the_wall_owes_nothing_however_long_they_were_away()
    {
        MatchHost host = Left(20, Inside);

        host.Arrears("Vela", At(200)).Should().BeNull();
    }

    [TestMethod]
    public void The_bill_is_read_off_the_log_and_priced_by_the_phase_each_tick_fell_due_in()
    {
        MatchHost host = Left(20, Outside);

        ZoneDebt debt = host.Arrears("Vela", At(70))!.Value;

        debt.Absence.Should().Be(TimeSpan.FromSeconds(50));
        debt.Forgiven.Should().Be(TimeSpan.FromSeconds(20));
        debt.Ticks.Should().Be(15);
        debt.Damage.Should().Be(9 * 1 + 6 * 3);
        debt.HealthBefore.Should().Be(92, "the health the log last knew them to be on");
        debt.IsFatal.Should().BeFalse();
    }

    [TestMethod]
    public void Charging_it_writes_one_record_where_they_were_standing()
    {
        MatchHost host = Left(20, Outside);
        ZoneDebt debt = host.Arrears("Vela", At(70))!.Value;

        host.Charged("Vela", debt);

        AuditRecord record = host.Audit.Records
            .Should().ContainSingle(record => record.Kind == AuditRecordKind.ZoneArrears).Subject;

        record.Player.Should().Be("Vela");
        record.Amount.Should().Be(debt.Damage);
        record.Health.Should().Be(debt.HealthLeft);
        record.Position!.Value.Z.Should().Be(Outside.Z, "the last place the log put them");
        record.Elapsed.Should().Be(TimeSpan.FromSeconds(70), "charged when they came back");
        record.Detail.Should().Be("away 50s");
    }

    [TestMethod]
    public void One_absence_is_charged_once()
    {
        MatchHost host = Left(20, Outside);
        host.Charged("Vela", host.Arrears("Vela", At(70))!.Value);

        host.Arrears("Vela", At(80)).Should().BeNull();
    }

    [TestMethod]
    public void A_second_absence_is_charged_again()
    {
        MatchHost host = Left(20, Outside);
        host.Charged("Vela", host.Arrears("Vela", At(70))!.Value);

        Tick(host, At(80), Reading(Outside));
        Tick(host, At(90), Reading(Outside));
        Tick(host, At(100));

        host.Arrears("Vela", At(300)).Should().NotBeNull();
    }

    [TestMethod]
    public void A_player_who_dropped_inside_the_wall_owes_nothing_when_the_opening_outlasts_a_cycle()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.playtest.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));
        TournamentConfig config = result.Config!;
        config.DestinationFor(5)!.Name.Should().Be("Lava Zone, outside the containment",
            "the event's seed sent group 5 there, and the bill has to be read off that squeeze");

        MatchHost host = new MatchHost(config, Heartbeat);
        for (int group = 1; group < 5; group++)
        {
            host.Command("next", Noon);
        }

        host.Command("start", Noon);

        PlayerReading ember = new PlayerReading(1, "Ember",
            new WorldPoint(260.17, -19.583, 52.487), true, 79.333, 40, 70, 65);
        Tick(host, Noon, ember);
        Tick(host, At(995.25), ember);
        Tick(host, At(995.25));

        host.Arrears("Ember", At(1146.972)).Should().BeNull(
            "they were inside the wall the match was under for every second they were away");
    }

    [TestMethod]
    public void A_bill_is_read_off_the_rules_the_replayed_group_runs_under()
    {
        MatchHost host = Left(20, Outside);
        host.Arrears("Vela", At(70)).Should().NotBeNull("the test file forgives 20s, and this was 50s outside");

        host.Command("reset", At(80));
        string yaml = File.ReadAllText("tournament.test.yaml");
        string forgiving = yaml.Replace("grace: 20s", "grace: 1h");
        forgiving.Should().NotBe(yaml, "the test file has to have a grace to edit");
        host.Adopt(TournamentConfigLoader.Load(forgiving).Config!, 2, "ops", out _).Should().BeTrue();
        host.Command("start", At(90));

        Tick(host, At(90), Reading(Outside));
        Tick(host, At(110), Reading(Outside));
        Tick(host, At(110));

        host.Arrears("Vela", At(160)).Should().BeNull("the rules this attempt runs on forgive an hour");
    }

    [TestMethod]
    public void A_player_the_match_was_already_finished_with_owes_nothing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Tick(host, Noon, Reading(Outside));
        Tick(host, At(20), Reading(Outside, alive: false));
        Tick(host, At(30));

        host.Arrears("Vela", At(300)).Should().BeNull();
    }

    [TestMethod]
    public void A_reset_between_leaving_and_returning_cancels_the_bill()
    {
        MatchHost host = Left(20, Outside);

        host.Command("reset", At(60));
        host.Command("start", At(70));

        host.Arrears("Vela", At(300)).Should().BeNull();
    }

    [TestMethod]
    public void A_bill_the_wall_would_not_have_survived_says_so()
    {
        MatchHost host = Left(20, Outside);

        ZoneDebt debt = host.Arrears("Vela", At(300))!.Value;

        debt.IsFatal.Should().BeTrue();
        debt.HealthLeft.Should().Be(0);
        debt.Damage.Should().BeGreaterThanOrEqualTo(92);
    }

    [TestMethod]
    public void The_charge_lands_in_the_match_it_was_charged_in()
    {
        MatchHost host = Left(20, Outside);
        host.Charged("Vela", host.Arrears("Vela", At(70))!.Value);

        AuditRecord record = host.Audit.Records.Last(record => record.Kind == AuditRecordKind.ZoneArrears);

        record.Group.Should().Be(host.Group);
        record.Generation.Should().Be(host.Generation);
    }

    [TestMethod]
    public void A_replay_counts_the_charge_as_damage_and_not_as_a_bite()
    {
        MatchHost host = Left(20, Outside);
        ZoneDebt debt = host.Arrears("Vela", At(70))!.Value;
        host.Charged("Vela", debt);

        PlayerAudit player = AuditReplay.Of(host.Audit.Records).Match!.Player("Vela")!;

        player.DamageTaken.Should().Be(debt.Damage);
        player.Bites.Should().Be(0);
    }

    [TestMethod]
    public void Nothing_is_charged_for_an_absence_the_wall_would_have_ignored()
    {
        MatchHost host = Left(20, Outside);

        host.Arrears("Vela", At(41)).Should().BeNull();
        host.Audit.Records.Should().NotContain(record => record.Kind == AuditRecordKind.ZoneArrears);
    }

    [TestMethod]
    public void A_client_cannot_announce_a_charge_against_itself()
    {
        PlayerEvent.CanBeReported(AuditRecordKind.ZoneArrears).Should().BeFalse();
    }

    [TestMethod]
    public void A_charge_is_about_a_player_and_therefore_has_to_name_one()
    {
        Action nameless = () => new AuditRecord(1, DateTimeOffset.UnixEpoch,
            new MatchStamp(1, 1, TimeSpan.Zero), AuditRecordKind.ZoneArrears);

        nameless.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void A_charge_survives_the_trip_through_the_log_file()
    {
        MatchHost host = Left(20, Outside);
        host.Charged("Vela", host.Arrears("Vela", At(70))!.Value);

        AuditRecord written = host.Audit.Records.Last(record => record.Kind == AuditRecordKind.ZoneArrears);

        AuditFormat.TryRead(AuditFormat.Write(written), out AuditRecord read, out string? problem)
            .Should().BeTrue(problem);
        read.Should().Be(written);
    }
}
