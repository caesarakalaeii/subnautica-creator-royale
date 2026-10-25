using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Stops <c>MainCameraControl.cinematicMode</c> throwing while the camera is detached from
    ///     the player, as it is for a spectator.
    /// </summary>
    /// <remarks>
    ///     The setter dereferences <c>GetComponentInParent&lt;Player&gt;()</c>, which is null once the
    ///     free camera has unparented. Nitrox sets cinematic mode on every teleport, so without this a
    ///     ring catching anyone throws on every spectator's client. Patched at the camera, not at
    ///     <c>Player.cinematicModeActive</c>, so the player's half of the setter still runs.
    /// </remarks>
    internal static class CinematicSuppression
    {
        private const string Id = "lms.spectator.cinematic";
        private const string Property = "cinematicMode";

        private static bool applied;

        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            try
            {
                MethodInfo setter = AccessTools.PropertySetter(typeof(MainCameraControl), Property);
                if (setter == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no MainCameraControl.{Property} setter to patch; a spectator " +
                             "will throw a NullReferenceException whenever anybody is teleported.");
                    return;
                }

                new Harmony(Id).Patch(setter,
                    new HarmonyMethod(typeof(CinematicSuppression), nameof(OnlyWhileTheCameraIsOnThePlayer)));

                Log.Info($"{LmsMod.LogPrefix} cinematic mode is declined while the camera is off the player.");
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not guard cinematic mode: {ex.Message}");
            }
        }

        /// <summary>
        ///     Runs the original only when there is a player above the camera.
        /// </summary>
        /// <remarks>Uses Unity's null comparison: a destroyed component is not a C# null.</remarks>
        private static bool OnlyWhileTheCameraIsOnThePlayer(MainCameraControl __instance) =>
            __instance != null && __instance.GetComponentInParent<Player>() != null;
    }
}
