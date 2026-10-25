using Lms.Core.Zones;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Converts an operator-configured <see cref="ZoneColour"/> into a Unity colour.
    /// </summary>
    internal static class GameColour
    {
        private const float FromByte = 1f / 255f;

        /// <summary>The colour as written, alpha included.</summary>
        internal static Color From(ZoneColour colour) =>
            new Color(colour.Red * FromByte, colour.Green * FromByte, colour.Blue * FromByte, colour.Alpha * FromByte);

        /// <summary>The colour fully opaque, for thin minimap rings where the world alpha would vanish.</summary>
        internal static Color Solid(ZoneColour colour) =>
            new Color(colour.Red * FromByte, colour.Green * FromByte, colour.Blue * FromByte, 1f);
    }
}
