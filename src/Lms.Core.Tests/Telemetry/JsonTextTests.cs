using System;
using FluentAssertions;
using Lms.Core.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lms.Core.Tests.Telemetry;

/// <summary>The hand-written JSON encoder under the telemetry contract: escaping, culture and precision.</summary>
[TestClass]
public class JsonTextTests
{
    [TestMethod]
    public void An_object_is_written_in_the_order_its_fields_were_added()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Text("name", "Vela");
        json.Number("health", 92);
        json.CloseObject();

        json.ToString().Should().Be(@"{""name"":""Vela"",""health"":92}");
    }

    [TestMethod]
    public void A_quote_or_a_backslash_in_a_name_does_not_end_the_string()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Text("name", "Ve\"la\\Rook");
        json.CloseObject();

        json.ToString().Should().Be(@"{""name"":""Ve\""la\\Rook""}");
    }

    [TestMethod]
    public void Control_characters_are_escaped_rather_than_passed_through()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Text("biome", "kelp\nforest\ttrench");
        json.Text("odd", "a" + (char)1 + "b");
        json.CloseObject();

        json.ToString().Should().Be(@"{""biome"":""kelp\nforest\ttrench"",""odd"":""a\u0001b""}");
    }

    [TestMethod]
    public void Null_is_written_as_null_rather_than_as_an_empty_string()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Text("vehicle", null);
        json.CloseObject();

        json.ToString().Should().Be(@"{""vehicle"":null}");
    }

    [TestMethod]
    public void Numbers_are_written_the_same_way_whatever_the_machine_calls_a_decimal_point()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Number("x", 1.5);
        json.CloseObject();

        json.ToString().Should().Be(@"{""x"":1.5}");
    }

    [TestMethod]
    public void A_number_is_cut_to_millimetres_rather_than_written_in_full()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Number("x", 1.23456789);
        json.Number("y", -0.0004);
        json.CloseObject();

        json.ToString().Should().Be(@"{""x"":1.234,""y"":0}");
    }

    [TestMethod]
    public void A_number_that_is_not_a_number_is_written_as_null()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Number("x", double.NaN);
        json.Number("y", double.PositiveInfinity);
        json.CloseObject();

        json.ToString().Should().Be(@"{""x"":null,""y"":null}");
    }

    [TestMethod]
    public void A_flag_is_written_lower_case()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Flag("alive", true);
        json.Flag("piloting", false);
        json.CloseObject();

        json.ToString().Should().Be(@"{""alive"":true,""piloting"":false}");
    }

    [TestMethod]
    public void An_array_of_objects_nests_and_separates_itself()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.OpenArray("players");
        json.OpenObject();
        json.Text("name", "Vela");
        json.CloseObject();
        json.OpenObject();
        json.Text("name", "Rook");
        json.CloseObject();
        json.CloseArray();
        json.CloseObject();

        json.ToString().Should().Be(@"{""players"":[{""name"":""Vela""},{""name"":""Rook""}]}");
    }

    [TestMethod]
    public void An_empty_array_is_still_an_array()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.OpenArray("events");
        json.CloseArray();
        json.CloseObject();

        json.ToString().Should().Be(@"{""events"":[]}");
    }

    [TestMethod]
    public void A_time_is_written_as_utc_to_the_millisecond()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Time("at", new DateTimeOffset(2026, 10, 24, 12, 34, 56, 789, TimeSpan.FromHours(2)));
        json.CloseObject();

        json.ToString().Should().Be(@"{""at"":""2026-10-24T10:34:56.789Z""}");
    }

    [TestMethod]
    public void A_property_can_be_written_as_a_bare_null_object()
    {
        JsonText json = new JsonText();
        json.OpenObject();
        json.Null("zone");
        json.CloseObject();

        json.ToString().Should().Be(@"{""zone"":null}");
    }
}
