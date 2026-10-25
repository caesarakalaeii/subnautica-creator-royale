using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Declines the two things <c>ResetPlayerOnDeath</c> would do to a player who is being left
    ///     where they died: the teleport, and putting them back in normal mode.
    /// </summary>
    /// <remarks>
    ///     The corpse is what other clients see, so it must not teleport to the escape pod. The rest of
    ///     <c>ResetPlayerOnDeath</c> still runs, since it fires <c>playerDeathEvent</c> that Nitrox
    ///     relays. If the teleport patch fails, <see cref="Active"/> stays false and no body is held.
    /// </remarks>
    internal static class RespawnSuppression
    {
        private const string Id = "lms.spectator.respawn";

        private static bool applied;

        /// <summary>Whether the teleport is actually being refused. False means do not hold a body.</summary>
        internal static bool Active { get; private set; }

        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            try
            {
                MethodInfo original = AccessTools.Method(typeof(Player), nameof(Player.MovePlayerToRespawnPoint));
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no Player.MovePlayerToRespawnPoint to patch; " +
                             "an eliminated player would be teleported away from their own body, so spectating is off.");
                    return;
                }

                new Harmony(Id).Patch(original,
                    new HarmonyMethod(typeof(RespawnSuppression), nameof(LeaveTheBodyWhereItFell)));

                Active = true;
                Log.Info($"{LmsMod.LogPrefix} an eliminated player's body stays where it fell.");

                KeepTheHandover();
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not keep a body where it fell: {ex.Message}");
            }
        }

        /// <summary>
        ///     Stops the death coroutine taking the spectator out of the free camera's mode.
        /// </summary>
        /// <remarks>
        ///     At the five-second mark the coroutine calls <c>ToNormalMode</c>, racing the spectator
        ///     handover. Not essential, so a missing method only warns.
        /// </remarks>
        private static void KeepTheHandover()
        {
            MethodInfo original = AccessTools.Method(typeof(Player), nameof(Player.ToNormalMode));
            if (original == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} no Player.ToNormalMode to patch; the death sequence may " +
                         "take the camera back off a spectator a second after it was handed over.");
                return;
            }

            new Harmony(Id).Patch(original,
                new HarmonyMethod(typeof(RespawnSuppression), nameof(LeaveTheSpectatorFlying)));

            Log.Info($"{LmsMod.LogPrefix} the death sequence cannot put a spectator back in normal mode.");
        }

        /// <summary>The prefix itself: skip the original while the body is being left behind.</summary>
        private static bool LeaveTheBodyWhereItFell() => !SpectatorHold.KeepsTheBody;

        /// <summary>
        ///     The second prefix. Reports success without acting: the coroutine ignores the result and
        ///     other callers must not see a refusal.
        /// </summary>
        private static bool LeaveTheSpectatorFlying(ref bool __result)
        {
            if (!SpectatorHold.KeepsTheBody)
            {
                return true;
            }

            // Logged so the race is visible in a spectator's log.
            Log.Info($"{LmsMod.LogPrefix} declined a return to normal mode while the local player is " +
                     "spectating their own body.");

            __result = true;
            return false;
        }
    }
}
