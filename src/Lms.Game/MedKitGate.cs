using System;
using System.Reflection;
using HarmonyLib;
using Lms.Core.Combat;
using Lms.Core.Match;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Puts a cooldown on the first aid kit, in match time, while a match is live.
    /// </summary>
    /// <remarks>
    ///     There is no <c>FirstAidKit</c> class; kits go through <c>Survival.Use(GameObject)</c>, which
    ///     returns whether the item was consumed. The prefix refuses (the kit is kept) and the postfix
    ///     starts the clock only when a kit actually healed.
    /// </remarks>
    internal static class MedKitGate
    {
        private const string Id = "lms.medkit.cooldown";

        private static bool applied;

        private static MedKitCooldown? cooldown;
        private static MatchFollower? match;
        private static int matchGeneration = -1;

        /// <summary>
        ///     Takes the rule and the match, and patches the game once.
        /// </summary>
        /// <remarks>
        ///     Nothing is patched while the rule is off. Called again on every rules push, so it must
        ///     also switch the rule back off.
        /// </remarks>
        internal static void Apply(MedKitPolicy policy, MatchFollower? follower)
        {
            if (policy == null || !policy.IsEnforced)
            {
                // Clear rather than leave: the patch may already be in place, and BeforeUse gates on
                // this field. The patch itself stays; it is a no-op while this is null.
                cooldown = null;

                Log.Info($"{LmsMod.LogPrefix} {MedKitPolicy.Default}");
                return;
            }

            cooldown = new MedKitCooldown(policy.Cooldown);
            match = follower;

            if (applied)
            {
                return;
            }

            applied = true;

            try
            {
                MethodInfo original = AccessTools.Method(typeof(Survival), nameof(Survival.Use),
                    new[] { typeof(UnityEngine.GameObject) });

                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no Survival.Use to patch, so first aid kits have " +
                             "no cooldown on this client.");
                    return;
                }

                Harmony harmony = new Harmony(Id);
                harmony.Patch(original,
                    prefix: new HarmonyMethod(typeof(MedKitGate), nameof(BeforeUse)),
                    postfix: new HarmonyMethod(typeof(MedKitGate), nameof(AfterUse)));

                Log.Info($"{LmsMod.LogPrefix} {policy}");
            }
            catch (Exception failure)
            {
                Log.Error($"{LmsMod.LogPrefix} could not put a cooldown on first aid kits: " +
                          failure.Message);
            }
        }

        /// <summary>Forgotten with the session, so the next match starts on nothing.</summary>
        internal static void Forget()
        {
            cooldown?.Ready();
            matchGeneration = -1;
        }

        /// <summary>
        ///     Refuses a kit while the last one is still cooling down.
        /// </summary>
        /// <remarks>
        ///     Only a first aid kit: <c>Survival.Use</c> also handles the enzyme cure, so the tech type
        ///     is resolved the same way the original does.
        /// </remarks>
        private static bool BeforeUse(UnityEngine.GameObject useObj, ref bool __result)
        {
            if (cooldown == null || !IsFirstAidKit(useObj))
            {
                return true;
            }

            TimeSpan now = MatchNow();
            if (now == TimeSpan.MinValue || cooldown.IsReady(now))
            {
                return true;
            }

            // Player's locale, not Readable.Line: on-screen text is read by the player.
            ErrorMessage.AddMessage(
                $"LMS: first aid kit on cooldown — {cooldown.Remaining(now).TotalSeconds:0.#}s");

            // False, so the caller believes nothing was used and the kit stays in the inventory.
            __result = false;

            return false;
        }

        /// <summary>Starts the clock, but only for a kit that actually healed somebody.</summary>
        private static void AfterUse(UnityEngine.GameObject useObj, bool __result)
        {
            if (cooldown == null || !__result || !IsFirstAidKit(useObj))
            {
                return;
            }

            TimeSpan now = MatchNow();
            if (now == TimeSpan.MinValue)
            {
                return;
            }

            cooldown.Used(now);
        }

        /// <summary>
        ///     Match time, or <see cref="TimeSpan.MinValue"/> when there is no live match to pace.
        /// </summary>
        /// <remarks>
        ///     A new match attempt clears the lock, so a cooldown does not carry over a round reset.
        /// </remarks>
        private static TimeSpan MatchNow()
        {
            if (match == null || !match.HasMatch || !match.IsLive || !WorldClock.Available)
            {
                return TimeSpan.MinValue;
            }

            match.AdvanceTo(WorldClock.Now);

            if (matchGeneration != match.Generation)
            {
                matchGeneration = match.Generation;
                cooldown?.Ready();
            }

            return match.Elapsed;
        }

        /// <summary>
        ///     The same tech-type resolution <c>Survival.Use</c> makes.
        /// </summary>
        private static bool IsFirstAidKit(UnityEngine.GameObject useObj)
        {
            if (useObj == null)
            {
                return false;
            }

            TechType tech = CraftData.GetTechType(useObj);
            if (tech == TechType.None)
            {
                Pickupable pickup = useObj.GetComponent<Pickupable>();
                if (pickup != null)
                {
                    tech = pickup.GetTechType();
                }
            }

            return tech == TechType.FirstAidKit;
        }
    }
}
