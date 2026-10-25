namespace Lms.Core.Telemetry
{
    /// <summary>One filled equipment slot: what a player is wearing, and where. Empty slots are omitted.</summary>
    public readonly struct EquipmentLine
    {
        public EquipmentLine(string slot, string item)
        {
            Slot = slot;
            Item = item;
        }

        /// <summary>The game's own name for the slot: <c>Head</c>, <c>Body</c>, <c>Tank</c>.</summary>
        public string Slot { get; }

        /// <summary>The game's own name for what is in it, unmapped.</summary>
        public string Item { get; }
    }
}
