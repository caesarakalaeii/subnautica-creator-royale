using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Stops a player the ring is holding from climbing into a vehicle, a helm or through a hatch.
    /// </summary>
    /// <remarks>
    ///     Boarding goes through <c>Vehicle.OnHandClick</c>, <c>PilotingChair.OnHandClick</c> into
    ///     <c>Player.EnterPilotingMode</c>, and <c>UseableDiveHatch</c> for hulls; <c>Vehicle.CanPilot</c>
    ///     only gates steering. These gates are cosmetic; <see cref="RingEnforcer"/> enforces the rule
    ///     every tick. A method that cannot be found is logged and skipped.
    /// </remarks>
    internal static class RingBoardingGate
    {
        private const string Id = "lms.ring.boarding";

        private static bool applied;

        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            Harmony harmony = new Harmony(Id);
            HarmonyMethod refuse = new HarmonyMethod(typeof(RingBoardingGate), nameof(RefuseWhileHeld));
            HarmonyMethod refuseEntry = new HarmonyMethod(typeof(RingBoardingGate), nameof(RefuseEntryWhileHeld));

            int gates = 0;
            gates += Patch(harmony, refuse, typeof(Vehicle), "OnHandHover", typeof(GUIHand));
            gates += Patch(harmony, refuse, typeof(Vehicle), "OnHandClick", typeof(GUIHand));
            gates += Patch(harmony, refuse, typeof(PilotingChair), "OnHandHover", typeof(GUIHand));
            gates += Patch(harmony, refuse, typeof(PilotingChair), "OnHandClick", typeof(GUIHand));
            gates += Patch(harmony, refuse, typeof(Player), "EnterPilotingMode", typeof(PilotingChair), typeof(bool));
            gates += Patch(harmony, refuseEntry, typeof(UseableDiveHatch), "OnHandHover", typeof(GUIHand));
            gates += Patch(harmony, refuseEntry, typeof(UseableDiveHatch), "OnHandClick", typeof(GUIHand));

            Log.Info($"{LmsMod.LogPrefix} boarding is refused while a ring holds you ({gates}/7 gates).");
        }

        /// <summary>The prefix itself: skip the original while a ring has the player.</summary>
        private static bool RefuseWhileHeld() => !RingHold.IsHeld;

        /// <summary>
        ///     The same, for <c>UseableDiveHatch</c>, which is both the way in and the way out.
        /// </summary>
        /// <remarks>
        ///     Only entering is refused (decided by <c>Player.IsInsideWalkable()</c>), so a held player
        ///     already inside is not trapped. Allowed with no player at all, i.e. between sessions.
        /// </remarks>
        private static bool RefuseEntryWhileHeld() =>
            !RingHold.IsHeld || Player.main == null || Player.main.IsInsideWalkable();

        private static int Patch(Harmony harmony, HarmonyMethod prefix, Type type, string method, params Type[] parameters)
        {
            try
            {
                MethodInfo original = AccessTools.Method(type, method, parameters);
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no {type.Name}.{method} to patch; that way into a vehicle is not gated.");
                    return 0;
                }

                harmony.Patch(original, prefix);
                return 1;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not gate {type.Name}.{method}: {ex.Message}");
                return 0;
            }
        }
    }
}
