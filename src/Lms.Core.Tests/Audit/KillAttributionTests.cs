using System;
using FluentAssertions;
using Lms.Core.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Audit;

/// <summary>Kill attribution: a kill an operator records is credited, since the game cannot always infer who earned it (for example a wall kill).</summary>
[TestClass]
public class KillAttributionTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private AuditLog log = new AuditLog();

    [TestInitialize]
    public void NewLog()
    {
        log = new AuditLog();
    }

    private void Record(AuditRecordKind kind, string? player = null, string? actor = null,
        string? detail = null)
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(log.Sequence + 1);

        log.Append(Noon + elapsed, new MatchStamp(1, 1, elapsed), kind,
            player, actor, null, null, null, null, null, detail);
    }

    private MatchAudit Fold() => AuditReplay.Of(log.Records).Match!;

    [TestMethod]
    public void A_death_the_game_can_read_names_its_killer_on_the_spot()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", actor: "Ada", detail: "player");

        Fold().Player("Bea")!.KilledBy.Should().Be("Ada");
    }

    [TestMethod]
    public void A_death_with_nobody_behind_it_is_attributed_to_nobody()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", detail: "zone");

        Fold().Player("Bea")!.KilledBy.Should().BeNull();
    }

    [TestMethod]
    public void An_operator_can_attribute_a_kill_the_game_read_as_the_zone()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", detail: "zone");
        Record(AuditRecordKind.KillAttributed, player: "Bea", actor: "Ada");

        Fold().Player("Bea")!.KilledBy.Should().Be("Ada");
    }

    [TestMethod]
    public void The_last_word_on_a_kill_is_the_one_that_counts()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", actor: "Ada", detail: "player");
        Record(AuditRecordKind.KillAttributed, player: "Bea", actor: "Cy");

        Fold().Player("Bea")!.KilledBy.Should().Be("Cy");
    }

    [TestMethod]
    public void An_attribution_to_nobody_takes_a_kill_back()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", actor: "Ada", detail: "player");
        Record(AuditRecordKind.KillAttributed, player: "Bea");

        Fold().Player("Bea")!.KilledBy.Should().BeNull("an operator overruled it");
    }

    [TestMethod]
    public void Attributing_a_kill_does_not_eliminate_anybody()
    {
        Record(AuditRecordKind.PlayerEliminated, player: "Bea", detail: "zone");
        Record(AuditRecordKind.KillAttributed, player: "Bea", actor: "Ada");

        MatchAudit match = Fold();
        match.EliminationOrder.Should().ContainSingle();
        match.Player("Ada")!.IsAlive.Should().BeTrue("naming a killer says nothing about who is out");
    }
}
