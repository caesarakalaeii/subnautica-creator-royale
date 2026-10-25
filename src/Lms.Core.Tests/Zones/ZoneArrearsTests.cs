using System;
using FluentAssertions;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Zones;

/// <summary>Wall damage owed by a player who was disconnected, replayed against the wall in force and charged on return.</summary>
[TestClass]
public class ZoneArrearsTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -100, 0);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private static readonly WorldPoint WellInside = Centre;

    private static readonly WorldPoint Outside = new WorldPoint(0, -100, 700);

    private static readonly WorldPoint InsideUntilPhaseOne = new WorldPoint(0, -100, 300);

    private static ZoneSchedule Schedule() => new ZoneSchedule(
        new ZoneSphere(Centre, 500), 1,
        new[] { new ZonePhase(1, new ZoneSphere(Centre, 200), TimeSpan.FromSeconds(30), 10) });

    private static ZoneArrears NewArrears(TimeSpan? grace = null, TimeSpan? opening = null) =>
        new ZoneArrears(Schedule(), new ZonePolicy(Tick, 150), Cycle, opening ?? Cycle,
            new DisconnectPolicy(grace ?? Grace));

    private static ZoneDebt Owed(WorldPoint where, double leftAtSeconds, double returnedAtSeconds,
        double health = 100, TimeSpan? grace = null, TimeSpan? opening = null) =>
        NewArrears(grace, opening).Of(where, TimeSpan.FromSeconds(leftAtSeconds),
            TimeSpan.FromSeconds(returnedAtSeconds), health);

    [TestMethod]
    public void A_player_who_dropped_inside_the_wall_owes_nothing()
    {
        ZoneDebt debt = Owed(WellInside, 10, 110);

        debt.Owed.Should().BeFalse();
        debt.Damage.Should().Be(0);
        debt.Ticks.Should().Be(0);
        debt.Absence.Should().Be(TimeSpan.FromSeconds(100));
    }

    [TestMethod]
    public void An_absence_inside_the_grace_period_costs_nothing_even_out_in_the_wall()
    {
        ZoneDebt debt = Owed(Outside, 10, 39);

        debt.Owed.Should().BeFalse();
        debt.Forgiven.Should().Be(TimeSpan.FromSeconds(29));
        debt.Charged.Should().Be(TimeSpan.Zero);
    }

    [TestMethod]
    public void Only_the_absence_past_the_grace_period_is_charged()
    {
        ZoneDebt debt = Owed(Outside, 0, 50);

        debt.Absence.Should().Be(TimeSpan.FromSeconds(50));
        debt.Forgiven.Should().Be(Grace);
        debt.Charged.Should().Be(TimeSpan.FromSeconds(20));
        debt.Ticks.Should().Be(10);
        debt.Damage.Should().Be(10);
        debt.Owed.Should().BeTrue();
    }

    [TestMethod]
    public void A_tick_that_would_fall_due_after_they_are_back_is_not_owed()
    {
        ZoneDebt debt = Owed(Outside, 0, 35);

        debt.Charged.Should().Be(TimeSpan.FromSeconds(5));
        debt.Ticks.Should().Be(2);
    }

    [TestMethod]
    public void The_bill_is_priced_at_the_wall_each_tick_fell_due_under()
    {
        ZoneDebt debt = Owed(Outside, 0, 110, health: 1000);

        debt.Charged.Should().Be(TimeSpan.FromSeconds(80));
        debt.Ticks.Should().Be(40);
        debt.Damage.Should().Be(14 * 1 + 26 * 10);
    }

    [TestMethod]
    public void A_longer_opening_holds_the_opening_price_until_the_first_phase_really_fires()
    {
        ZoneDebt debt = Owed(Outside, 0, 110, health: 1000, opening: TimeSpan.FromSeconds(90));

        debt.Ticks.Should().Be(40);
        debt.Damage.Should().Be(29 * 1 + 11 * 10);
    }

    [TestMethod]
    public void The_wall_that_closed_over_somebody_who_was_away_still_reaches_them()
    {
        ZoneDebt debt = Owed(InsideUntilPhaseOne, 0, 110, health: 1000);

        debt.Owed.Should().BeTrue();

        debt.Ticks.Should().BeInRange(1, 20);
        debt.Damage.Should().Be(debt.Ticks * 10);
    }

    [TestMethod]
    public void The_bill_stops_at_the_moment_it_would_have_killed_them()
    {
        ZoneDebt debt = Owed(Outside, 0, 600, health: 10);

        debt.Ticks.Should().Be(10);
        debt.Damage.Should().Be(10);
        debt.IsFatal.Should().BeTrue();
        debt.HealthLeft.Should().Be(0);
    }

    [TestMethod]
    public void A_survivable_bill_says_what_it_leaves_them_on()
    {
        ZoneDebt debt = Owed(Outside, 0, 50, health: 100);

        debt.IsFatal.Should().BeFalse();
        debt.HealthLeft.Should().Be(90);
    }

    [TestMethod]
    public void A_return_that_is_not_after_the_departure_owes_nothing()
    {
        Owed(Outside, 90, 90).Owed.Should().BeFalse();
        Owed(Outside, 90, 10).Owed.Should().BeFalse();
    }

    [TestMethod]
    public void A_grace_of_nothing_charges_the_whole_absence()
    {
        ZoneDebt debt = Owed(Outside, 0, 10, grace: TimeSpan.Zero);

        debt.Forgiven.Should().Be(TimeSpan.Zero);
        debt.Charged.Should().Be(TimeSpan.FromSeconds(10));
        debt.Ticks.Should().Be(5);
    }

    [TestMethod]
    public void A_grace_longer_than_a_match_forgives_everything()
    {
        Owed(Outside, 0, 1200, grace: TimeSpan.FromHours(1)).Owed.Should().BeFalse();
    }

    [TestMethod]
    public void The_bill_reads_as_a_sentence()
    {
        Owed(Outside, 0, 50).ToString().Should().Be("10 over 10 ticks, away 50s");
        Owed(WellInside, 10, 110).ToString().Should().Be("nothing owed, away 100s");
    }

    [TestMethod]
    public void A_grace_period_cannot_run_backwards()
    {
        Action backwards = () => new DisconnectPolicy(TimeSpan.FromSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Ninety_seconds_is_what_a_config_that_says_nothing_gets()
    {
        DisconnectPolicy.Default.Grace.Should().Be(TimeSpan.FromSeconds(90));
    }
}
