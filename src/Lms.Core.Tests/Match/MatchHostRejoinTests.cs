using System;
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

/// <summary>What the log says about a returning player's standing.</summary>
[TestClass]
public class MatchHostRejoinTests
{
    private static readonly TimeSpan Noon = TimeSpan.FromHours(12);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    private static readonly WorldPoint Inside = new WorldPoint(750, -110, 450);

    private readonly PlayerRoster roster = new PlayerRoster();

    private static MatchHost NewHost()
    {
        TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText("tournament.test.yaml"));
        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));

        return new MatchHost(result.Config!, Heartbeat);
    }

    private static PlayerReading Reading(ushort id = 1, string name = "Vela") =>
        new PlayerReading(id, name, Inside, true, 92, 40, 70, 65);

    private void Tick(MatchHost host, TimeSpan worldNow, params PlayerReading[] present) =>
        Ticked(host, worldNow, present);

    private RosterUpdate Ticked(MatchHost host, TimeSpan worldNow, params PlayerReading[] present)
    {
        foreach (PlayerReading reading in present)
        {
            if (host.IsOut(reading.Name, worldNow))
            {
                roster.Out(reading.Name, host.Generation);
            }
        }

        RosterUpdate update = roster.Update(present, host.Zone(worldNow).Sphere, host.Generation);
        host.Record(update.Changes);

        return update;
    }

    private static TimeSpan At(double seconds) => Noon + TimeSpan.FromSeconds(seconds);

    private MatchHost Eliminated()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Tick(host, Noon, Reading());

        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Tick(host, At(60), Reading());
        Tick(host, At(90));

        return host;
    }

    [TestMethod]
    public void The_log_still_knows_somebody_the_roster_has_forgotten()
    {
        MatchHost host = Eliminated();

        host.IsOut("Vela", At(120)).Should().BeTrue();
    }

    [TestMethod]
    public void A_name_the_log_has_never_heard_of_is_in_the_match()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        host.IsOut("Someone", At(120)).Should().BeFalse();
        host.IsOut(null, At(120)).Should().BeFalse();
        host.IsOut(" ", At(120)).Should().BeFalse();
    }

    [TestMethod]
    public void Somebody_who_merely_dropped_is_still_in_the_match()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Tick(host, Noon, Reading());
        Tick(host, At(60));

        host.IsOut("Vela", At(120)).Should().BeFalse();
    }

    [TestMethod]
    public void An_elimination_does_not_follow_somebody_into_the_next_group()
    {
        MatchHost host = Eliminated();
        host.Command("next", At(120));

        host.IsOut("Vela", At(150)).Should().BeFalse();
    }

    [TestMethod]
    public void And_a_reset_clears_it_because_that_is_what_a_reset_means()
    {
        MatchHost host = Eliminated();
        host.Command("reset", At(120));

        host.IsOut("Vela", At(150)).Should().BeFalse();
    }

    [TestMethod]
    public void The_whole_rejoin_leaves_one_record_and_an_alive_count_that_never_moved()
    {
        MatchHost host = NewHost();
        host.Command("start", Noon);

        Tick(host, Noon, Reading(1, "Vela"), Reading(2, "Rook"));

        roster.Reported(1, new PlayerReport(true, null, null, null, null));
        Tick(host, At(60), Reading(1, "Vela"), Reading(2, "Rook"));

        Tick(host, At(90), Reading(2, "Rook"));

        for (double when = 120; when <= 150; when += 10)
        {
            RosterUpdate update = Ticked(host, At(when), Reading(9, "Vela"), Reading(2, "Rook"));

            PlayerRoster.Alive(update.Rows).Should().Be(1, "the match is still finished with Vela");
        }

        host.Audit.Records.Count(record => record.Kind == AuditRecordKind.PlayerEliminated)
            .Should().Be(1, "the standings are a fold over this log");
    }

    [TestMethod]
    public void It_is_the_log_that_answers_rather_than_anything_kept_beside_it()
    {
        MatchHost host = Eliminated();

        host.IsOut("Vela", At(120)).Should().BeTrue();
        host.IsOut("Vela", At(300)).Should().BeTrue();
    }
}
