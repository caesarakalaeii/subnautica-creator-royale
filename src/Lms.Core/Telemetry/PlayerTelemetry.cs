using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>One player's row on the dashboard: a full snapshot, not a diff.</summary>
    /// <remarks>
    ///     Partly self-reported by the client, so it is broadcast data; the audit log is the record.
    /// </remarks>
    public sealed class PlayerTelemetry
    {
        private readonly ReadOnlyCollection<InventoryLine> inventory;
        private readonly ReadOnlyCollection<EquipmentLine> equipment;

        public PlayerTelemetry(string name, bool alive, WorldPoint position, string? biome,
            double health, double oxygen, double food, double water,
            string? vehicle, string? heldItem, bool insideZone, double distanceOutside,
            IReadOnlyList<InventoryLine>? inventory = null,
            IReadOnlyList<EquipmentLine>? equipment = null, bool rifle = false,
            double rifleCharge = 0, bool frozen = false)
        {
            Name = name;
            Alive = alive;
            Position = position;
            Biome = biome;
            Health = health;
            Oxygen = oxygen;
            Food = food;
            Water = water;
            Vehicle = vehicle;
            HeldItem = heldItem;
            InsideZone = insideZone;
            DistanceOutside = distanceOutside;
            Rifle = rifle;
            RifleCharge = rifleCharge;
            Frozen = frozen;
            this.inventory = new ReadOnlyCollection<InventoryLine>(
                inventory == null ? new List<InventoryLine>() : new List<InventoryLine>(inventory));
            this.equipment = new ReadOnlyCollection<EquipmentLine>(
                equipment == null ? new List<EquipmentLine>() : new List<EquipmentLine>(equipment));
        }

        public string Name { get; }

        /// <summary>Still in the match. A spectator is false and still has a row.</summary>
        public bool Alive { get; }

        public WorldPoint Position { get; }

        /// <summary>
        ///     Metres below the surface. Negative above sea level (e.g. in an escape pod); not clamped.
        /// </summary>
        public double Depth => Position.Depth;

        /// <summary>What the place is called, or null where nothing names it.</summary>
        public string? Biome { get; }

        public double Health { get; }

        public double Oxygen { get; }

        public double Food { get; }

        public double Water { get; }

        /// <summary>What they are in, or null on foot.</summary>
        public string? Vehicle { get; }

        /// <summary>What is in their hands, or null.</summary>
        public string? HeldItem { get; }

        /// <summary>Whether they are carrying a stasis rifle.</summary>
        public bool Rifle { get; }

        /// <summary>Stasis rifle charge, 0 to 1. Zero when not charging.</summary>
        public double RifleCharge { get; }

        /// <summary>Whether a stasis field is holding them still.</summary>
        public bool Frozen { get; }

        public bool InsideZone { get; }

        /// <summary>Metres past the wall; zero inside it. The dashboard sorts on this.</summary>
        public double DistanceOutside { get; }

        public IReadOnlyList<InventoryLine> Inventory => inventory;

        /// <summary>
        ///     What they are wearing, by slot.
        /// </summary>
        public IReadOnlyList<EquipmentLine> Equipment => equipment;
    }
}
