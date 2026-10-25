using System;
using System.Reflection;
using HarmonyLib;
using Lms.Core.Combat;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Tells the server when this client swung a knife or a heat blade at another player.
    /// </summary>
    /// <remarks>
    ///     Reports only: Nitrox leaves a remote player's <c>LiveMixin</c> invincible, and the server
    ///     decides the damage. <c>HeatBlade</c> extends <c>Knife</c> without overriding
    ///     <c>OnToolUseAnim</c>, so one patch covers both. Postfix only, under its own Harmony id,
    ///     because Nitrox transpiles the same method.
    /// </remarks>
    internal static class BladeStrikeReporter
    {
        private const string Id = "lms.blade";

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
                MethodInfo original = AccessTools.Method(typeof(Knife), "OnToolUseAnim",
                    new[] { typeof(GUIHand) });
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no Knife.OnToolUseAnim to patch; neither blade " +
                             "will hurt anybody. Nitrox patches the same method, so if that is " +
                             "still working this is a name change rather than a missing type.");
                    return;
                }

                new Harmony(Id).Patch(original,
                    postfix: new HarmonyMethod(typeof(BladeStrikeReporter), nameof(BladeSwung)));

                Log.Info($"{LmsMod.LogPrefix} blades report their swings to the server. The server " +
                         "decides what they cost.");
            }
            catch (Exception ex)
            {
                // Log and skip: a game update moving this method costs the blades, not the mod.
                Log.Warn($"{LmsMod.LogPrefix} could not patch Knife.OnToolUseAnim: {ex.Message}");
            }
        }

        /// <remarks>
        ///     Re-runs the trace, since the blade stores nothing about what it hit. Deliberately skips
        ///     the blade's <c>GetMostRecent()</c> fallback, its <c>IsValidTarget</c> and its damage: the
        ///     server decides whether a swing landed and what it costs. <c>attackDist</c> is a public
        ///     serialized field, so each prefab's own reach is used.
        /// </remarks>
        private static void BladeSwung(Knife __instance, GUIHand hand)
        {
            try
            {
                // Only the local player's own held tool.
                if (Inventory.main == null || !ReferenceEquals(Inventory.main.GetHeldTool(), __instance))
                {
                    return;
                }

                if (Player.main == null)
                {
                    return;
                }

                GameObject? hit = null;
                Vector3 position = default(Vector3);

                // Ignore the player, not the tool: the blade is parented under the body.
                UWE.Utils.TraceFPSTargetPosition(Player.main.gameObject, __instance.attackDist,
                    ref hit, ref position);

                if (hit == null)
                {
                    return;
                }

                ushort? victim = RemotePlayers.Identify(hit);
                if (victim == null)
                {
                    return;
                }

                // HeatBlade first, because it is a Knife; same order as Nitrox's own switch.
                LmsReporting.Report(StrikeReport.Encode(victim.Value,
                    __instance is HeatBlade ? StrikeWeapon.HeatBlade : StrikeWeapon.Knife));
            }
            catch (Exception ex)
            {
                // Never throw out of the game's animation event.
                Log.Warn($"{LmsMod.LogPrefix} could not report a blade swing: {ex.Message}");
            }
        }
    }
}
