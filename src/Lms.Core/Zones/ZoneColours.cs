namespace Lms.Core.Zones
{
    /// <summary>The colours of the current wall and the wall it is closing to. Cosmetic only.</summary>
    public sealed class ZoneColours
    {
        /// <summary>
        ///     Gold for the wall that bites, and a faint green ghost (alpha 0x1A) for where it is going.
        /// </summary>
        /// <remarks>
        ///     The ghost is faint because players often sit inside it for a whole phase; the minimap
        ///     and bearing line carry the detail.
        /// </remarks>
        public static readonly ZoneColours Default = new ZoneColours(
            new ZoneColour(0xFF, 0xD6, 0x5C),
            new ZoneColour(0x7C, 0xFF, 0x5C, 0x1A));

        public ZoneColours(ZoneColour now, ZoneColour next)
        {
            Now = now;
            Next = next;
        }

        /// <summary>The wall that is enforced right now — the one that bites.</summary>
        public ZoneColour Now { get; }

        /// <summary>The ghost: where the wall is closing to, drawn from inside the current one.</summary>
        public ZoneColour Next { get; }
    }
}
