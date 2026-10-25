using System;
using Lms.Core.Biomes;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     The entry point the Nitrox fork calls (see <c>NitroxPatcher.LmsHook</c>), and the only
    ///     public surface of this assembly.
    /// </summary>
    /// <remarks>
    ///     This assembly only adapts the game; decisions live in <c>Lms.Core</c>, which is testable
    ///     without a game install.
    /// </remarks>
    public static class LmsMod
    {
        /// <summary>
        ///     Prefix for every log line: UTC time plus world time, so logs from different machines line up.
        /// </summary>
        internal static string LogPrefix =>
            LogStamp.Prefix(DateTimeOffset.UtcNow, WorldClock.Available ? WorldClock.Now : (TimeSpan?)null);

        private static bool initialized;

        /// <summary>Called once, from the Nitrox fork, after Nitrox has finished initializing.</summary>
        public static void Initialize()
        {
            if (initialized)
            {
                Log.Warn($"{LogPrefix} Initialize was called twice; ignoring the second call.");
                return;
            }
            initialized = true;

            // Touches Core types so a missing Lms.Core.dll fails here at startup, not mid-match.
            Log.Info($"{LogPrefix} Last Man Standing {Version(typeof(LmsMod))} loaded, " +
                     $"core {Version(typeof(TournamentBiome))} with {KnownBiomes.Playable.Count} playable biomes");

            GameObject root = new GameObject("LmsTournament");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<LmsRuntime>();
        }

        private static Version Version(Type type) => type.Assembly.GetName().Version;
    }
}
