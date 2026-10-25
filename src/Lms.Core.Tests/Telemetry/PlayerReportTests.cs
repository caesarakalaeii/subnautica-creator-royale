using FluentAssertions;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The wire encoding of a client's self-report.</summary>
[TestClass]
public class PlayerReportTests
{
    private static readonly InventoryLine[] Pack =
    {
        new InventoryLine("Titanium", 4),
        new InventoryLine("Quartz", 2)
    };

    private static readonly EquipmentLine[] Worn =
    {
        new EquipmentLine("Body", "ReinforcedDiveSuit"),
        new EquipmentLine("Head", "Rebreather")
    };

    private static PlayerReport Decoded(PlayerReport report)
    {
        PlayerReport.TryDecode(report.Encode(), out PlayerReport read).Should().BeTrue();

        return read;
    }

    [TestMethod]
    public void Everything_a_client_can_see_survives_the_wire()
    {
        PlayerReport read = Decoded(new PlayerReport(false, "kelpForest", "Seamoth", "StasisRifle", Pack));

        read.Eliminated.Should().BeFalse();
        read.Biome.Should().Be("kelpForest");
        read.Vehicle.Should().Be("Seamoth");
        read.HeldItem.Should().Be("StasisRifle");
        read.Inventory.Should().HaveCount(2);
        read.Inventory[0].Item.Should().Be("Titanium");
        read.Inventory[0].Count.Should().Be(4);
    }

    [TestMethod]
    public void A_player_on_foot_with_empty_hands_and_an_empty_pack_says_so()
    {
        PlayerReport read = Decoded(new PlayerReport(false, null, null, null, null));

        read.Biome.Should().BeNull();
        read.Vehicle.Should().BeNull();
        read.HeldItem.Should().BeNull();
        read.Inventory.Should().BeEmpty();
    }

    [TestMethod]
    public void A_player_the_zone_has_finished_with_says_that_too()
    {
        Decoded(new PlayerReport(true, null, null, null, null)).Eliminated.Should().BeTrue();
    }

    [TestMethod]
    public void A_line_that_is_not_a_report_is_declined_rather_than_half_read()
    {
        PlayerReport.TryDecode("lms-zone/1\tanchor=1:1:0:0", out _).Should().BeFalse();
        PlayerReport.TryDecode("", out _).Should().BeFalse();
        PlayerReport.TryDecode(null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_report_from_a_version_this_build_does_not_speak_is_declined()
    {
        PlayerReport.TryDecode("lms-player/2\tbiome=kelpForest", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_field_with_a_tab_in_it_cannot_break_the_line()
    {
        PlayerReport read = Decoded(new PlayerReport(false, "kelp\tforest", null, null, null));

        read.Biome.Should().Be("kelp forest");
    }

    [TestMethod]
    public void An_unknown_field_is_ignored_rather_than_refused()
    {
        PlayerReport.TryDecode("lms-player/1\tbiome=kelpForest\tmood=grim", out PlayerReport read)
            .Should().BeTrue();

        read.Biome.Should().Be("kelpForest");
    }

    [TestMethod]
    public void An_inventory_stack_that_did_not_survive_is_dropped_rather_than_counted_as_one()
    {
        PlayerReport.TryDecode("lms-player/1\tinv=Titanium:4,Quartz,Copper:2", out PlayerReport read)
            .Should().BeTrue();

        read.Inventory.Should().HaveCount(2, "a stack with no count is not a stack of one");
        read.Inventory[1].Item.Should().Be("Copper");
    }

    [TestMethod]
    public void What_a_player_is_wearing_survives_the_wire_slot_and_all()
    {
        PlayerReport read = Decoded(new PlayerReport(false, null, null, null, null, Worn));

        read.Equipment.Should().HaveCount(2);
        read.Equipment[0].Slot.Should().Be("Body");
        read.Equipment[0].Item.Should().Be("ReinforcedDiveSuit");
        read.Equipment[1].Slot.Should().Be("Head");
        read.Equipment[1].Item.Should().Be("Rebreather");
    }

    [TestMethod]
    public void A_player_wearing_nothing_says_so_rather_than_saying_nothing()
    {
        Decoded(new PlayerReport(false, null, null, null, null)).Equipment.Should().BeEmpty();
    }

    [TestMethod]
    public void An_equipment_slot_with_nothing_in_it_is_dropped_rather_than_worn_empty()
    {
        PlayerReport.TryDecode("lms-player/1\tequip=Body:ReinforcedDiveSuit,Gloves,Tank:PlasteelTank",
            out PlayerReport read).Should().BeTrue();

        read.Equipment.Should().HaveCount(2);
        read.Equipment[1].Slot.Should().Be("Tank");
    }

    [TestMethod]
    public void The_rifle_a_caster_is_about_to_call_survives_the_wire()
    {
        PlayerReport read = Decoded(new PlayerReport(false, null, null, "StasisRifle", null, null,
            rifle: true, rifleCharge: 0.62, frozen: true));

        read.Rifle.Should().BeTrue();
        read.RifleCharge.Should().BeApproximately(0.62, 0.0005);
        read.Frozen.Should().BeTrue();
    }

    [TestMethod]
    public void A_player_with_no_rifle_and_nobody_holding_them_says_so()
    {
        PlayerReport read = Decoded(new PlayerReport(false, null, null, null, null));

        read.Rifle.Should().BeFalse();
        read.RifleCharge.Should().Be(0);
        read.Frozen.Should().BeFalse();
    }

    [TestMethod]
    public void A_charge_is_written_in_no_particular_locale()
    {
        new PlayerReport(false, null, null, null, null, null, rifle: true, rifleCharge: 0.5)
            .Encode().Should().Contain("charging=0.5");
    }

    [TestMethod]
    public void A_rifle_being_carried_but_not_charged_leaves_the_charge_out()
    {
        string line = new PlayerReport(false, null, null, null, null, null, rifle: true).Encode();

        line.Should().Contain("rifle=1");
        line.Should().NotContain("charging=");
        line.Should().NotContain("frozen=");
    }
}
