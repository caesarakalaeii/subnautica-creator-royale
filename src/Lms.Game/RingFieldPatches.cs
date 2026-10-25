using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Sizes every stasis field from the config, prices the rifle, enforces one ring at a time,
    ///     and tells the enforcer the moment a field goes up.
    /// </summary>
    /// <remarks>
    ///     <c>StasisSphere.Shoot</c> turns a charge into radius and lifetime, and the fork fires remote
    ///     players' fields through it too. <c>EnableField</c> runs when any bubble appears.
    /// </remarks>
    internal static class RingFieldPatches
    {
        private const string Id = "lms.ring.fields";

        private static bool applied;

        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            Harmony harmony = new Harmony(Id);

            bool sized = Patch(harmony, "Shoot", nameof(BeforeShoot), prefix: true,
                typeof(Vector3), typeof(Quaternion), typeof(float), typeof(float), typeof(float));
            bool seen = Patch(harmony, "EnableField", nameof(AfterEnableField), prefix: false);
            bool priced = PatchRifle(harmony, "Awake", nameof(AfterRifleAwake), prefix: false);

            // Refusing the trigger too means a rifle on cooldown spends no battery.
            bool held = PatchRifle(harmony, "OnRightHandDown", nameof(BeforeRightHandDown), prefix: true);
            bool gated = PatchRifle(harmony, "Fire", nameof(BeforeFire), prefix: true);

            if (sized && seen && priced && held && gated)
            {
                return;
            }

            Log.Error($"{LmsMod.LogPrefix} the ring is not fully hooked into the game " +
                      $"(sized: {sized}, noticed: {seen}, priced: {priced}, one-ring-at-a-time: " +
                      $"{held && gated}). Fields may be the wrong size, found late, cost what the " +
                      "game asks rather than what the config does, or despawn each other.");
        }

        /// <summary>The game is about to turn a charge into a field. These are the numbers it uses.</summary>
        private static void BeforeShoot(StasisSphere __instance) => RingEnforcer.Resize(__instance);

        /// <summary>A bubble has just appeared, wherever it came from.</summary>
        private static void AfterEnableField(StasisSphere __instance) => RingEnforcer.FieldEnabled(__instance);

        /// <summary>A rifle has just been built. This is what a ring costs whoever fires it.</summary>
        private static void AfterRifleAwake(StasisRifle __instance) => RingEnforcer.Price(__instance);

        /// <summary>
        ///     The trigger, refused while the player's own ring is still up.
        /// </summary>
        /// <remarks>
        ///     Refused here as well as at the shot so no energy is spent and no charge animation plays.
        /// </remarks>
        private static bool BeforeRightHandDown(ref bool __result)
        {
            if (RingEnforcer.MayFire())
            {
                return true;
            }

            RingEnforcer.RefusedToFire();
            __result = false;

            return false;
        }

        /// <summary>
        ///     The shot itself: the hard gate, and where a permitted shot is recorded.
        /// </summary>
        /// <remarks>
        ///     <c>Fire</c> is reached both from releasing the trigger and from <c>Charge</c> when a full
        ///     charge fires itself, so it sees every shot. A zero charge is the game's no-op and starts
        ///     no cooldown.
        /// </remarks>
        private static bool BeforeFire(StasisRifle __instance, float ___chargeAmount)
        {
            if (___chargeAmount <= 0f)
            {
                return true;
            }

            if (!RingEnforcer.MayFire())
            {
                RingEnforcer.RefusedToFire();
                return false;
            }

            // The rifle's own normalisation, which the field lerps radius and lifetime from.
            float cost = __instance.energyCost;
            RingEnforcer.Fired(cost > 0f ? ___chargeAmount / cost : 1f);

            return true;
        }

        private static bool PatchRifle(Harmony harmony, string method, string handler, bool prefix)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(StasisRifle), method);
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no StasisRifle.{method} to patch.");
                    return false;
                }

                HarmonyMethod patch = new HarmonyMethod(typeof(RingFieldPatches), handler);
                harmony.Patch(original, prefix ? patch : null, prefix ? null : patch);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not patch StasisRifle.{method}: {ex.Message}");
                return false;
            }
        }

        private static bool Patch(Harmony harmony, string method, string handler, bool prefix, params Type[] parameters)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(StasisSphere), method, parameters.Length == 0 ? null : parameters);
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no StasisSphere.{method} to patch.");
                    return false;
                }

                HarmonyMethod patch = new HarmonyMethod(typeof(RingFieldPatches), handler);
                harmony.Patch(original, prefix ? patch : null, prefix ? null : patch);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not patch StasisSphere.{method}: {ex.Message}");
                return false;
            }
        }
    }
}
