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
    ///     Tells the server when this client swung a prawn suit's arm at another player.
    /// </summary>
    /// <remarks>
    ///     Reports only: Nitrox leaves a remote player's <c>LiveMixin</c> invincible, and the server
    ///     decides the damage and whether the prawn hurts players at all. Postfixes on each arm's
    ///     <c>OnHit</c>, so a game update moving one costs only that arm.
    /// </remarks>
    internal static class PrawnStrikeReporter
    {
        private const string Id = "lms.prawn.arms";

        /// <summary>The claw's own reach, out of the decompiled arm.</summary>
        private const float ClawReach = 6.5f;

        private static bool applied;

        /// <summary>
        ///     <c>ExosuitDrillArm.drillTarget</c>: what the drill hit this call.
        /// </summary>
        /// <remarks>
        ///     <c>OnHit</c> clears it first and sets it only on a real hit, so it also answers whether
        ///     the trigger was held and the arm could swing.
        /// </remarks>
        private static FieldInfo? drillTarget;

        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            Harmony harmony = new Harmony(Id);

            int arms = 0;
            arms += Patch(harmony, nameof(ClawSwung), typeof(ExosuitClawArm));
            arms += Patch(harmony, nameof(DrillSwung), typeof(ExosuitDrillArm));

            Log.Info($"{LmsMod.LogPrefix} prawn arms report their swings to the server " +
                     $"({arms}/2 arms). The server decides what they cost.");
        }

        /// <remarks>
        ///     The claw stores nothing about what it hit, so its trace is re-run in the same frame,
        ///     behind the same two gates the original checks. The arm's
        ///     <c>InteractionVolumeUser.GetMostRecent()</c> fallback is deliberately not followed.
        /// </remarks>
        private static void ClawSwung(ExosuitClawArm __instance)
        {
            try
            {
                Exosuit exosuit = __instance.GetComponentInParent<Exosuit>();
                if (exosuit == null || !exosuit.CanPilot() || !exosuit.GetPilotingMode())
                {
                    return;
                }

                GameObject? hit = null;
                Vector3 position = default(Vector3);
                UWE.Utils.TraceFPSTargetPosition(exosuit.gameObject, ClawReach, ref hit, ref position);

                Report(hit, StrikeWeapon.Claw);
            }
            catch (Exception ex)
            {
                // Never throw out of the game's animation event.
                Log.Warn($"{LmsMod.LogPrefix} could not report a claw swing: {ex.Message}");
            }
        }

        private static void DrillSwung(ExosuitDrillArm __instance)
        {
            try
            {
                if (drillTarget == null)
                {
                    return;
                }

                Report(drillTarget.GetValue(__instance) as GameObject, StrikeWeapon.Drill);
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not report a drill tick: {ex.Message}");
            }
        }

        /// <summary>Sends one swing, if what was hit turns out to be another player.</summary>
        private static void Report(GameObject? hit, StrikeWeapon arm)
        {
            if (hit == null)
            {
                return;
            }

            ushort? victim = RemotePlayers.Identify(hit);
            if (victim == null)
            {
                return;
            }

            LmsReporting.Report(StrikeReport.Encode(victim.Value, arm));
        }

        /// <remarks>
        ///     Logs and skips rather than throws, so a game update moving one <c>OnHit</c> costs only that arm.
        /// </remarks>
        private static int Patch(Harmony harmony, string postfix, Type arm)
        {
            try
            {
                MethodInfo original = AccessTools.Method(arm, "OnHit", Type.EmptyTypes);
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no {arm.Name}.OnHit to patch; that arm will not " +
                             "hurt anybody.");
                    return 0;
                }

                if (arm == typeof(ExosuitDrillArm))
                {
                    drillTarget = AccessTools.Field(arm, "drillTarget");
                    if (drillTarget == null)
                    {
                        Log.Warn($"{LmsMod.LogPrefix} {arm.Name} has no drillTarget field in this " +
                                 "build of the game; the drill will not hurt anybody.");
                        return 0;
                    }
                }

                harmony.Patch(original, postfix: new HarmonyMethod(typeof(PrawnStrikeReporter), postfix));
                return 1;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not patch {arm.Name}.OnHit: {ex.Message}");
                return 0;
            }
        }
    }
}
