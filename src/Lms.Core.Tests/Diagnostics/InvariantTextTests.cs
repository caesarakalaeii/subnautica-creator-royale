using System;
using System.Globalization;
using System.Threading;
using FluentAssertions;
using Lms.Core.Audit;
using Lms.Core.Settings;
using Lms.Core.Zones;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Diagnostics;

/// <summary>Log summaries are culture-invariant, even on a machine whose locale writes <c>40,2</c>.</summary>
[TestClass]
public class InvariantTextTests
{
    private static readonly WorldPoint Centre = new WorldPoint(0, -100, 0);

    private static readonly WorldPoint Outside = new WorldPoint(0, -100, 700);

    private CultureInfo? restore;

    private static CultureInfo DecimalComma()
    {
        CultureInfo comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        return comma;
    }

    [TestInitialize]
    public void WriteTheLogInAnotherLocale()
    {
        restore = CultureInfo.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = DecimalComma();
    }

    [TestCleanup]
    public void PutTheLocaleBack() => Thread.CurrentThread.CurrentCulture = restore!;

    [TestMethod]
    public void An_arrears_bill_reads_the_same_wherever_it_was_charged()
    {
        ZoneSchedule schedule = new ZoneSchedule(new ZoneSphere(Centre, 500), 2.5,
            new[] { new ZonePhase(1, new ZoneSphere(Centre, 200), TimeSpan.FromSeconds(30), 10) });
        ZoneArrears arrears = new ZoneArrears(schedule, new ZonePolicy(TimeSpan.FromSeconds(2), 150),
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), new DisconnectPolicy(TimeSpan.Zero));

        ZoneDebt debt = arrears.Of(Outside, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(16), 100);

        debt.ToString().Should().Be("7.5 over 3 ticks, away 6s");
    }

    [TestMethod]
    public void The_disconnect_grace_on_the_load_line_reads_the_same_everywhere()
    {
        new DisconnectPolicy(TimeSpan.FromSeconds(1.5)).ToString()
            .Should().Be("1.5s of a disconnect forgiven");
    }

    [TestMethod]
    public void The_position_cadence_on_the_load_line_reads_the_same_everywhere()
    {
        new AuditPolicy(TimeSpan.FromSeconds(0.5)).ToString()
            .Should().Be("positions every 0.5s");
    }

    [TestMethod]
    public void The_hud_layout_describes_itself_the_same_everywhere()
    {
        new HudLayout(2560, 1440).ToString()
            .Should().Be("2560x1440 at 1.33x: map (16, 16) 309x309, readout (1931, 16) 613x152");
    }

    [TestMethod]
    public void A_hud_rectangle_reads_the_same_everywhere()
    {
        new HudRect(1.5f, 2.5f, 3.5f, 4.5f).ToString().Should().Be("(2, 3) 4x5");
    }
}
