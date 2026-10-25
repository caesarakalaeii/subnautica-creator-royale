using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Match;

/// <summary>One player hurting another, in the log.</summary>
[TestClass]
public class MatchHostHurtTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly WorldPoint Where = new WorldPoint(750, -110, 450);

    private readonly PlayerRoster roster = new PlayerRoster();

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private MatchHost Playing()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.Record(roster.Update(
            new[]
            {
                new PlayerReading(1, "Vela", Where, true, 92, 40, 70, 65),
                new PlayerReading(2, "Mara", Where, true, 92, 40, 70, 65),
            },
            host.Zone(Noon).Sphere, host.Generation).Changes);

        return host;
    }

    private static AuditRecord Hurt(MatchHost host) =>
        host.Audit.Records.Single(record => record.Kind == AuditRecordKind.PlayerHurt);

    [TestMethod]
    public void A_landed_hit_writes_one_record_with_both_names_on_it()
    {
        MatchHost host = Playing();

        host.Hurt("Vela", "Mara", StrikeWeapon.Claw, damage: 20, healthBefore: 92, where: Where, biome: "kelp");

        AuditRecord record = Hurt(host);
        record.Player.Should().Be("Vela", "the victim");
        record.Actor.Should().Be("Mara", "the attacker");
        record.Amount.Should().Be(20);
        record.Health.Should().Be(72, "what it leaves them on");
        record.Detail.Should().Be("claw", "which arm did it");
        record.Position.Should().Be(Where);
        record.Biome.Should().Be("kelp");
    }

    [TestMethod]
    public void The_arm_is_named_in_the_record()
    {
        MatchHost host = Playing();

        host.Hurt("Vela", "Mara", StrikeWeapon.Drill, damage: 4, healthBefore: 92);

        Hurt(host).Detail.Should().Be("drill");
    }

    [TestMethod]
    public void A_hit_that_would_take_them_below_nothing_says_nothing_rather_than_a_negative()
    {
        MatchHost host = Playing();

        host.Hurt("Vela", "Mara", StrikeWeapon.Claw, damage: 100, healthBefore: 92);

        Hurt(host).Health.Should().Be(0);
    }

    [TestMethod]
    public void The_damage_counts_towards_what_the_victim_has_taken()
    {
        MatchHost host = Playing();

        host.Hurt("Vela", "Mara", StrikeWeapon.Claw, damage: 20, healthBefore: 92);

        host.Standings(Noon);
        PlayerAudit vela = Replayed(host, "Vela");
        vela.DamageTaken.Should().Be(20);
        vela.Bites.Should().Be(0, "nothing to do with the wall");
    }

    [TestMethod]
    public void A_hit_for_nothing_is_refused_rather_than_written()
    {
        MatchHost host = Playing();

        Action nothing = () => host.Hurt("Vela", "Mara", StrikeWeapon.Drill, damage: 0, healthBefore: 92);

        nothing.Should().Throw<ArgumentOutOfRangeException>();
        host.Audit.Records.Should().NotContain(record => record.Kind == AuditRecordKind.PlayerHurt);
    }

    [TestMethod]
    public void A_hit_with_nobody_s_name_on_it_is_refused()
    {
        MatchHost host = Playing();

        ((Action)(() => host.Hurt("Vela", "  ", StrikeWeapon.Claw, 20, 92))).Should().Throw<ArgumentException>();
        ((Action)(() => host.Hurt("  ", "Mara", StrikeWeapon.Claw, 20, 92))).Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void A_prawn_hit_is_not_something_a_client_may_announce()
    {
        PlayerEvent.CanBeReported(AuditRecordKind.PlayerHurt).Should().BeFalse();
    }

    [TestMethod]
    public void A_hurt_record_has_to_name_the_player_it_is_about()
    {
        AuditRecord.IsAboutAPlayer(AuditRecordKind.PlayerHurt).Should().BeTrue();
    }

    [TestMethod]
    public void A_hurt_record_survives_the_round_trip_through_the_file()
    {
        MatchHost host = Playing();
        host.Hurt("Vela", "Mara", StrikeWeapon.Claw, damage: 20, healthBefore: 92, where: Where, biome: "kelp");

        string line = AuditFormat.Write(Hurt(host));

        AuditFormat.TryRead(line, out AuditRecord read, out string? problem).Should().BeTrue(problem);
        read.Kind.Should().Be(AuditRecordKind.PlayerHurt);
        read.Player.Should().Be("Vela");
        read.Actor.Should().Be("Mara");
        read.Amount.Should().Be(20);
        read.Health.Should().Be(72);
        read.Detail.Should().Be("claw");
    }

    private static PlayerAudit Replayed(MatchHost host, string name)
    {
        AuditReplay replay = new AuditReplay();
        foreach (AuditRecord record in host.Audit.Records)
        {
            replay.Apply(record);
        }

        return replay.Match!.Player(name)!;
    }
}
