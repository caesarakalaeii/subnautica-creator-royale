using System;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>How the tournament table is pushed: on change and on a slow heartbeat, null block in between.</summary>
[TestClass]
public class StandingsPushTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static TelemetryPump NewPump() =>
        new TelemetryPump(new TelemetryPolicy("http://booth:8080/ingest", Second, TimeSpan.FromSeconds(5)));

    private static StandingsTelemetry Table(int matches, int points) =>
        new StandingsTelemetry(matches, 2, new[]
        {
            new StandingsLine(1, "Vela", points, played: matches, wins: 1, bestPlacement: 1,
                kills: 1, outlasted: points - 2),
        });

    /// <summary>One push, and the booth takes it.</summary>
    private static TelemetrySnapshot Push(TelemetryPump pump, DateTimeOffset now,
        StandingsTelemetry? standings, bool accepted = true)
    {
        TelemetrySnapshot snapshot = pump.Take(now, null, null, new PlayerTelemetry[0],
            new AuditLog(), standings);

        if (accepted)
        {
            pump.Accepted();
        }
        else
        {
            pump.Refused();
        }

        return snapshot;
    }

    [TestMethod]
    public void A_tournament_with_nothing_finished_carries_no_table_and_revision_zero()
    {
        TelemetrySnapshot snapshot = Push(NewPump(), Noon, null);

        snapshot.Standings.Should().BeNull();
        snapshot.StandingsRevision.Should().Be(0, "no table has ever been sent");
    }

    [TestMethod]
    public void The_first_table_is_carried_and_moves_the_revision()
    {
        TelemetryPump pump = NewPump();

        TelemetrySnapshot snapshot = Push(pump, Noon, Table(1, 4));

        snapshot.Standings.Should().NotBeNull();
        snapshot.StandingsRevision.Should().Be(1);
        pump.StandingsRevision.Should().Be(1);
    }

    [TestMethod]
    public void An_unchanged_table_is_not_carried_again()
    {
        TelemetryPump pump = NewPump();
        StandingsTelemetry table = Table(1, 4);

        Push(pump, Noon, table);
        TelemetrySnapshot next = Push(pump, Noon + Second, table);

        next.Standings.Should().BeNull();
        next.StandingsRevision.Should().Be(1, "the same revision the booth already has");
    }

    [TestMethod]
    public void A_table_that_moves_is_carried_with_a_new_revision()
    {
        TelemetryPump pump = NewPump();

        Push(pump, Noon, Table(1, 4));
        TelemetrySnapshot next = Push(pump, Noon + Second, Table(2, 9));

        next.Standings.Should().NotBeNull();
        next.Standings!.Matches.Should().Be(2);
        next.StandingsRevision.Should().Be(2);
    }

    [TestMethod]
    public void A_table_that_moved_and_moved_back_is_still_news()
    {
        TelemetryPump pump = NewPump();

        Push(pump, Noon, Table(1, 4));
        Push(pump, Noon + Second, Table(1, 6));
        TelemetrySnapshot back = Push(pump, Noon + Second + Second, Table(1, 4));

        back.Standings.Should().NotBeNull();
        back.StandingsRevision.Should().Be(3);
    }

    [TestMethod]
    public void A_refused_push_does_not_count_as_having_sent_the_table()
    {
        TelemetryPump pump = NewPump();

        Push(pump, Noon, Table(1, 4), accepted: false);
        pump.StandingsRevision.Should().Be(0, "nothing was taken");

        TelemetrySnapshot again = Push(pump, Noon + TimeSpan.FromSeconds(3), Table(1, 4));

        again.Standings.Should().NotBeNull("the booth still has not seen it");
        again.StandingsRevision.Should().Be(1);
    }

    [TestMethod]
    public void A_refused_push_and_the_one_that_replaces_it_name_the_same_revision()
    {
        TelemetryPump pump = NewPump();

        TelemetrySnapshot lost = Push(pump, Noon, Table(1, 4), accepted: false);
        TelemetrySnapshot next = Push(pump, Noon + TimeSpan.FromSeconds(3), Table(2, 9));

        lost.StandingsRevision.Should().Be(1);
        next.StandingsRevision.Should().Be(1, "the booth has still never seen a table");
        next.Standings!.Matches.Should().Be(2, "and it gets the current one, not the lost one");
    }

    [TestMethod]
    public void A_table_that_becomes_nothing_is_carried_as_a_null_with_a_new_revision()
    {
        TelemetryPump pump = NewPump();

        Push(pump, Noon, Table(1, 4));
        TelemetrySnapshot cleared = Push(pump, Noon + Second, null);

        cleared.Standings.Should().BeNull();
        cleared.StandingsRevision.Should().Be(2, "the revision moved, so the null means cleared");
    }

    [TestMethod]
    public void An_unchanged_table_is_sent_again_on_the_heartbeat()
    {
        TelemetryPump pump = NewPump();
        StandingsTelemetry table = Table(1, 4);

        Push(pump, Noon, table);
        Push(pump, Noon + Second, table).Standings.Should().BeNull();

        TelemetrySnapshot beat = Push(pump, Noon + TimeSpan.FromSeconds(30), table);

        beat.Standings.Should().NotBeNull("thirty seconds is the backstop");
        beat.StandingsRevision.Should().Be(1, "a repeat is not a new table");
    }

    [TestMethod]
    public void The_heartbeat_is_measured_from_the_last_table_actually_sent()
    {
        TelemetryPump pump = NewPump();
        StandingsTelemetry table = Table(1, 4);

        Push(pump, Noon, table);
        Push(pump, Noon + TimeSpan.FromSeconds(30), table).Standings.Should().NotBeNull();
        Push(pump, Noon + TimeSpan.FromSeconds(31), table).Standings.Should().BeNull();
        Push(pump, Noon + TimeSpan.FromSeconds(60), table).Standings.Should().NotBeNull();
    }

    [TestMethod]
    public void A_revision_cannot_be_negative()
    {
        Action backwards = () => new TelemetrySnapshot(Noon, null, null, null, null, null, -1);

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Two_tables_with_the_same_rows_are_the_same_table()
    {
        Table(2, 9).Should().Be(Table(2, 9));
        Table(2, 9).Should().NotBe(Table(2, 10));
        Table(1, 9).Should().NotBe(Table(2, 9));
    }
}
