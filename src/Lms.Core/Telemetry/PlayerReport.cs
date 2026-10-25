using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     State only a player's own client can see: elimination, biome, vehicle, held item, inventory,
    ///     equipment and stasis rifle state.
    /// </summary>
    /// <remarks>
    ///     Elimination is decided on the dying player's client and reaches the server only through this
    ///     report. Broadcast data, not evidence: the server stamps the player's identity from the
    ///     authenticated connection, never from the payload. Encoded as tab-separated
    ///     <c>key=value</c> text like <see cref="Lms.Core.Match.MatchBroadcast"/>, since the packet
    ///     envelope lives in the fork and the fields belong to the mod.
    /// </remarks>
    public sealed class PlayerReport
    {
        /// <summary>The first token of the wire form; bumped when a field's meaning changes.</summary>
        public const string WireHeader = "lms-player/1";

        private static readonly char[] PairSplit = { '=' };

        private readonly ReadOnlyCollection<InventoryLine> inventory;
        private readonly ReadOnlyCollection<EquipmentLine> equipment;

        public PlayerReport(bool eliminated, string? biome, string? vehicle, string? heldItem,
            IReadOnlyList<InventoryLine>? inventory,
            IReadOnlyList<EquipmentLine>? equipment = null, bool rifle = false,
            double rifleCharge = 0, bool frozen = false)
        {
            Eliminated = eliminated;
            Biome = biome;
            Vehicle = vehicle;
            HeldItem = heldItem;
            Rifle = rifle;
            RifleCharge = rifleCharge;
            Frozen = frozen;
            this.inventory = new ReadOnlyCollection<InventoryLine>(
                inventory == null ? new List<InventoryLine>() : new List<InventoryLine>(inventory));
            this.equipment = new ReadOnlyCollection<EquipmentLine>(
                equipment == null ? new List<EquipmentLine>() : new List<EquipmentLine>(equipment));
        }

        /// <summary>Whether the match is finished with this player.</summary>
        public bool Eliminated { get; }

        /// <summary>What the place is called, or null where nothing names it.</summary>
        public string? Biome { get; }

        /// <summary>What they are in, by the game's own name for it, or null on foot.</summary>
        public string? Vehicle { get; }

        /// <summary>What is in their hands, or null.</summary>
        public string? HeldItem { get; }

        /// <summary>
        ///     Whether they carry a stasis rifle. Sent as a flag so consumers need not match game item names.
        /// </summary>
        public bool Rifle { get; }

        /// <summary>
        ///     Stasis rifle charge as a fraction of full, 0 to 1; zero when nothing is charging.
        ///     Normalised the same way as the ring's own gate.
        /// </summary>
        public double RifleCharge { get; }

        /// <summary>
        ///     Whether a stasis field is holding this player still. Who froze them is not known here.
        /// </summary>
        public bool Frozen { get; }

        public IReadOnlyList<InventoryLine> Inventory => inventory;

        /// <summary>What they are wearing, by slot. Empty slots are left out.</summary>
        public IReadOnlyList<EquipmentLine> Equipment => equipment;

        /// <summary>Encodes the wire form. Optional fields are omitted rather than written empty.</summary>
        public string Encode()
        {
            StringBuilder line = new StringBuilder(160);
            line.Append(WireHeader);

            if (Eliminated)
            {
                Pair(line, "out", "1");
            }

            Optional(line, "biome", Biome);
            Optional(line, "vehicle", Vehicle);
            Optional(line, "held", HeldItem);

            if (Rifle)
            {
                Pair(line, "rifle", "1");
            }

            if (RifleCharge > 0)
            {
                Pair(line, "charging", RifleCharge.ToString("0.###", CultureInfo.InvariantCulture));
            }

            if (Frozen)
            {
                Pair(line, "frozen", "1");
            }

            if (equipment.Count > 0)
            {
                Pair(line, "equip", Slots());
            }

            if (inventory.Count > 0)
            {
                Pair(line, "inv", Stacks());
            }

            return line.ToString();
        }

        /// <summary>
        ///     Decodes a report, or returns false. An unknown header version is refused; unknown fields
        ///     are ignored so mixed mod versions still interoperate.
        /// </summary>
        public static bool TryDecode(string? line, out PlayerReport report)
        {
            report = Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            bool eliminated = false;
            string? biome = null;
            string? vehicle = null;
            string? held = null;
            bool rifle = false;
            double charge = 0;
            bool frozen = false;
            List<InventoryLine>? pack = null;
            List<EquipmentLine>? worn = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                string[] pair = tokens[i].Split(PairSplit, 2);
                if (pair.Length != 2)
                {
                    continue;
                }

                switch (pair[0])
                {
                    case "out": eliminated = pair[1] == "1"; break;
                    case "biome": biome = pair[1]; break;
                    case "vehicle": vehicle = pair[1]; break;
                    case "held": held = pair[1]; break;
                    case "rifle": rifle = pair[1] == "1"; break;
                    case "charging": charge = Fraction(pair[1]); break;
                    case "frozen": frozen = pair[1] == "1"; break;
                    case "equip": worn = Slots(pair[1]); break;
                    case "inv": pack = Stacks(pair[1]); break;
                }
            }

            report = new PlayerReport(eliminated, biome, vehicle, held, pack, worn, rifle, charge,
                frozen);

            return true;
        }

        /// <summary>A player who has said nothing: on foot, empty-handed, still in the match.</summary>
        public static PlayerReport Empty { get; } = new PlayerReport(false, null, null, null, null);

        public override string ToString() => Encode();

        private string Stacks()
        {
            StringBuilder stacks = new StringBuilder(inventory.Count * 16);

            foreach (InventoryLine line in inventory)
            {
                if (stacks.Length > 0)
                {
                    stacks.Append(',');
                }

                stacks.Append(Field(line.Item))
                    .Append(':')
                    .Append(line.Count.ToString(CultureInfo.InvariantCulture));
            }

            return stacks.ToString();
        }

        /// <summary>Encodes equipment as <c>slot:item</c> pairs separated by commas, like the inventory.</summary>
        private string Slots()
        {
            StringBuilder slots = new StringBuilder(equipment.Count * 24);

            foreach (EquipmentLine line in equipment)
            {
                if (slots.Length > 0)
                {
                    slots.Append(',');
                }

                slots.Append(Field(line.Slot)).Append(':').Append(Field(line.Item));
            }

            return slots.ToString();
        }

        /// <summary>Decodes equipment, dropping malformed pairs.</summary>
        private static List<EquipmentLine> Slots(string written)
        {
            List<EquipmentLine> worn = new List<EquipmentLine>();

            foreach (string slot in written.Split(','))
            {
                int mark = slot.IndexOf(':');
                if (mark <= 0 || mark == slot.Length - 1)
                {
                    continue;
                }

                worn.Add(new EquipmentLine(slot.Substring(0, mark), slot.Substring(mark + 1)));
            }

            return worn;
        }

        /// <summary>Parses a charge; an unparseable value reads as zero rather than refusing the report.</summary>
        private static double Fraction(string written) =>
            double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value) && value > 0
                ? value
                : 0;

        /// <summary>Decodes inventory; a stack without a valid count is dropped rather than read as one.</summary>
        private static List<InventoryLine> Stacks(string written)
        {
            List<InventoryLine> pack = new List<InventoryLine>();

            foreach (string stack in written.Split(','))
            {
                int mark = stack.LastIndexOf(':');
                if (mark <= 0)
                {
                    continue;
                }

                if (int.TryParse(stack.Substring(mark + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int count))
                {
                    pack.Add(new InventoryLine(stack.Substring(0, mark), count));
                }
            }

            return pack;
        }

        private static void Optional(StringBuilder line, string key, string? value)
        {
            if (value != null)
            {
                Pair(line, key, Clean(value));
            }
        }

        private static void Pair(StringBuilder line, string key, string value) =>
            line.Append('\t').Append(key).Append('=').Append(value);

        /// <summary>Replaces tabs and newlines, which would otherwise break the line format.</summary>
        private static string Clean(string value) =>
            value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        /// <summary>Like <see cref="Clean"/>, also stripping the list separators <c>,</c> and <c>:</c>.</summary>
        private static string Field(string value) =>
            Clean(value).Replace(",", string.Empty).Replace(":", string.Empty);
    }
}
