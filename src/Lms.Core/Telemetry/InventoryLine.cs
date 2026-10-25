namespace Lms.Core.Telemetry
{
    /// <summary>One stack in a player's inventory: an item name and a count.</summary>
    public readonly struct InventoryLine
    {
        public InventoryLine(string item, int count)
        {
            Item = item;
            Count = count;
        }

        /// <summary>The game's own name for it, unmapped.</summary>
        public string Item { get; }

        public int Count { get; }
    }
}
