using System;
using System.Collections.Generic;
using HarmonyLib;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Reports to the server what only this machine can see: whether this player is out, biome,
    ///     vehicle, held item, pack, equipment and rifle state, plus this player's events.
    /// </summary>
    /// <remarks>
    ///     Two messages: the snapshot is latest-wins and skipped when unchanged, while events are
    ///     sent as their own batch and never deduplicated.
    /// </remarks>
    internal sealed class TelemetryReporter : MonoBehaviour
    {
        /// <summary>
        ///     How often this client reports, matching the server's push rate to the caster booth.
        /// </summary>
        private const float ReportIntervalSeconds = 1f;

        private BiomeSampler? biomes;
        private SpectatorSession? spectator;
        private PlayerEventLog? events;
        private float nextReportTime;

        /// <summary>The last thing said, so an unchanged report is not sent again.</summary>
        private string? lastSent;

        /// <summary>
        ///     The two private fields a rifle's charge is made of, resolved once.
        /// </summary>
        private static AccessTools.FieldRef<StasisRifle, float>? chargeAmount;
        private static AccessTools.FieldRef<StasisRifle, bool>? isCharging;
        private static bool chargeUnreadable;

        private void Awake()
        {
            LmsRuntime? runtime = GetComponent<LmsRuntime>();

            biomes = GetComponent<BiomeSampler>();

            // Safe to hold across a rules push: Adopt swaps the session's policy, not the session,
            // and Events cannot be reassigned. Anything taken off Config would need re-reading.
            spectator = runtime?.Spectator;
            events = runtime?.Events;
        }

        /// <remarks>
        ///     The snapshot is sent only on change; the server keeps the last one.
        /// </remarks>
        private void Update()
        {
            if (Time.time < nextReportTime)
            {
                return;
            }

            nextReportTime = Time.time + ReportIntervalSeconds;

            if (Player.main == null)
            {
                return;
            }

            // Events first: delivery is reliable and ordered, so a fatal bite precedes the report.
            SendEvents();

            string report = Report().Encode();
            if (report == lastSent)
            {
                return;
            }

            lastSent = report;
            LmsReporting.Report(report);
        }

        /// <remarks>
        ///     Never deduplicated: two identical bites are two bites.
        /// </remarks>
        private void SendEvents()
        {
            if (events == null || !events.Take(out PlayerEventBatch batch))
            {
                return;
            }

            if (batch.Lost > 0)
            {
                Log.Warn($"{LmsMod.LogPrefix} {batch.Lost} event(s) were dropped before they could be " +
                         "reported; the audit log will have a gap.");
            }

            LmsReporting.Report(batch.Encode());
        }

        private PlayerReport Report()
        {
            // Sampled once: hands, rifle and charge all read the same object.
            Pickupable held = Inventory.main == null ? null : Inventory.main.GetHeld();

            return new PlayerReport(spectator != null && !spectator.IsPlaying,
                biomes == null ? null : biomes.Named, Vehicle(), Held(held), Pack(),
                Worn(), Rifle(held), Charge(held), RingHold.IsHeld);
        }

        /// <remarks>
        ///     A Cyclops counts, a base does not: every base corridor is also a <c>GetCurrentSub</c>.
        /// </remarks>
        private static string? Vehicle()
        {
            Vehicle vehicle = Player.main.GetVehicle();
            if (vehicle != null)
            {
                return Name(vehicle.gameObject);
            }

            SubRoot sub = Player.main.GetCurrentSub();

            return sub != null && !sub.isBase ? Name(sub.gameObject) : null;
        }

        private static string? Held(Pickupable held) =>
            held == null ? null : Named(held.GetTechType());

        /// <summary>
        ///     What they are wearing, by slot. Empty slots hold null and are skipped.
        /// </summary>
        /// <remarks>
        ///     Uses the public <c>Equipment.GetEquipment()</c> enumerator, not the private dictionary.
        /// </remarks>
        private static IReadOnlyList<EquipmentLine> Worn()
        {
            List<EquipmentLine> worn = new List<EquipmentLine>();

            Equipment equipment = Inventory.main == null ? null : Inventory.main.equipment;
            if (equipment == null)
            {
                return worn;
            }

            Dictionary<string, InventoryItem>.Enumerator slots = equipment.GetEquipment();
            while (slots.MoveNext())
            {
                KeyValuePair<string, InventoryItem> slot = slots.Current;
                Pickupable item = slot.Value == null ? null : slot.Value.item;

                if (item != null)
                {
                    worn.Add(new EquipmentLine(slot.Key, Named(item.GetTechType())));
                }
            }

            return worn;
        }

        /// <summary>
        ///     Whether they have a stasis rifle, in hand or in the pack.
        /// </summary>
        private static bool Rifle(Pickupable held)
        {
            if (held != null && held.GetTechType() == TechType.StasisRifle)
            {
                return true;
            }

            return Inventory.main != null && Inventory.main.container != null &&
                   Inventory.main.container.GetCount(TechType.StasisRifle) > 0;
        }

        /// <summary>
        ///     How far into a charge the trigger is, from 0 to 1.
        /// </summary>
        /// <remarks>
        ///     <c>chargeAmount</c> is in energy, so it is divided by <c>energyCost</c> as the game does.
        ///     Gated on <c>isCharging</c>: a refused shot never zeroes the amount.
        /// </remarks>
        private static double Charge(Pickupable held)
        {
            StasisRifle rifle = held == null ? null : held.GetComponent<StasisRifle>();
            if (rifle == null || !ChargeIsReadable())
            {
                return 0;
            }

            if (!isCharging!(rifle))
            {
                return 0;
            }

            float cost = rifle.energyCost;

            return cost > 0f ? chargeAmount!(rifle) / cost : 1;
        }

        /// <remarks>
        ///     Resolved once; a missing field (a game update) degrades to "not charging" and warns once.
        /// </remarks>
        private static bool ChargeIsReadable()
        {
            if (chargeUnreadable)
            {
                return false;
            }

            if (chargeAmount != null && isCharging != null)
            {
                return true;
            }

            try
            {
                chargeAmount = AccessTools.FieldRefAccess<StasisRifle, float>("chargeAmount");
                isCharging = AccessTools.FieldRefAccess<StasisRifle, bool>("isCharging");

                return true;
            }
            catch (Exception ex)
            {
                chargeUnreadable = true;
                Log.Warn($"{LmsMod.LogPrefix} cannot read a stasis rifle's charge " +
                         $"({ex.Message}); the booth's rifle column will say \"carried\" and never " +
                         "count a charge up.");

                return false;
            }
        }

        /// <remarks>
        ///     By type and count, as the container already keeps it.
        /// </remarks>
        private static IReadOnlyList<InventoryLine> Pack()
        {
            List<InventoryLine> pack = new List<InventoryLine>();

            if (Inventory.main == null || Inventory.main.container == null)
            {
                return pack;
            }

            foreach (TechType type in Inventory.main.container.GetItemTypes())
            {
                pack.Add(new InventoryLine(Named(type), Inventory.main.container.GetCount(type)));
            }

            return pack;
        }

        private static string? Name(GameObject thing)
        {
            TechType type = CraftData.GetTechType(thing);

            return type == TechType.None ? null : Named(type);
        }

        /// <remarks>
        ///     The game's own name, unmapped; display names are the front end's job.
        /// </remarks>
        private static string Named(TechType type) => type.AsString();
    }
}
