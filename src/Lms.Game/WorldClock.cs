using System;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     The world clock every client in the session agrees on.
    /// </summary>
    /// <remarks>
    ///     <c>DayNightCycle.main.timePassedAsDouble</c> is written from the server every frame by
    ///     Nitrox's <c>TimeManager</c>. It is missing in the main menu and is still the local clock
    ///     while Nitrox's wait screen is up, so <see cref="Available"/> excludes both.
    /// </remarks>
    internal static class WorldClock
    {
        /// <summary>Whether there is a world, loaded, and therefore the server's clock to read.</summary>
        internal static bool Available => DayNightCycle.main != null && !WaitScreen.IsWaiting;

        /// <summary>World time, shared by every client in the session.</summary>
        internal static TimeSpan Now =>
            DayNightCycle.main != null
                ? TimeSpan.FromSeconds(DayNightCycle.main.timePassedAsDouble)
                : TimeSpan.Zero;
    }
}
