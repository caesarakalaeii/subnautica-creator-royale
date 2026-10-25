using System;
using System.Collections;
using System.Collections.Generic;
using Lms.Core.Match;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Clears this player's inventory to what the round starts with, and puts their health, food,
    ///     water and air back to the start.
    /// </summary>
    /// <remarks>
    ///     Runs on the client because it owns <c>Inventory.main</c>. Items are destroyed, not dropped.
    ///     Instructions for another group or generation are ignored, and a resend is not applied twice.
    /// </remarks>
    internal sealed class RoundKit : MonoBehaviour
    {
        // Nitrox's new-player defaults (SubnauticaServerConfig): the vanilla lifepod start.
        private const float StartHealth = 80f;
        private const float StartFood = 50.5f;
        private const float StartWater = 90.5f;
        private const float StartOxygen = 45f;

        private MatchFollower? match;
        private RoundBroadcast? pending;
        private RoundBroadcast? done;

        /// <summary>Takes an instruction. Applied on a frame that has a body, not here.</summary>
        internal void Told(RoundBroadcast instruction)
        {
            pending = instruction;
        }

        /// <summary>Forgotten with the session, the same way the match is.</summary>
        internal void Forget()
        {
            pending = null;
            done = null;
        }

        private void Awake()
        {
            match = GetComponent<LmsRuntime>()?.Follower;

            if (match == null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (pending == null || Player.main == null || match == null)
            {
                return;
            }

            RoundBroadcast instruction = pending.Value;

            // The round verb sends this before the match starts; wait for the match to check it against.
            if (!match.HasMatch)
            {
                return;
            }

            if (instruction.Group != match.Group || instruction.Generation != match.Generation)
            {
                pending = null;
                Log.Info($"{LmsMod.LogPrefix} ignored a round instruction for " +
                         $"{instruction.Group}.{instruction.Generation}; this client is in " +
                         $"{match.Group}.{match.Generation}.");
                return;
            }

            if (done != null && done.Value.Equals(instruction))
            {
                // A resend; clear it so the check is not repeated every frame.
                pending = null;
                return;
            }

            pending = null;
            done = instruction;

            Apply(instruction);
        }

        private void Apply(RoundBroadcast instruction)
        {
            Inventory inventory = Inventory.main;
            if (inventory == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} there is no inventory to clear for the round.");
                return;
            }

            int destroyed = Emptied(inventory);
            ResetVitals();

            Log.Info($"{LmsMod.LogPrefix} round {instruction.Group}.{instruction.Generation}: " +
                     $"{destroyed} item(s) destroyed, vitals set to the start.");

            ErrorMessage.AddMessage(instruction.Kit.Count == 0
                ? "Round starting: your inventory has been cleared and your vitals reset."
                : "Round starting: your inventory has been cleared, your vitals reset and your kit is on its way.");

            if (instruction.Kit.Count > 0)
            {
                // Prefabs load asynchronously, so the kit is a coroutine; the clear above must stay
                // synchronous so the inventory cannot be used half-cleared.
                StartCoroutine(Giving(inventory, instruction.Kit));
            }
        }

        /// <remarks>
        ///     Set, not topped up, so everyone starts equal. Oxygen is added because the game has no
        ///     setter; it caps at the tank.
        /// </remarks>
        private static void ResetVitals()
        {
            Player.main.liveMixin.health = StartHealth;
            Player.main.oxygenMgr?.AddOxygen(StartOxygen);

            Survival? survival = Player.main.GetComponent<Survival>();
            if (survival != null)
            {
                survival.food = StartFood;
                survival.water = StartWater;
            }
        }

        /// <summary>
        ///     Takes everything out and destroys it.
        /// </summary>
        /// <remarks>
        ///     Iterates a copy, because <c>DestroyItem</c> mutates the container's list. Uses
        ///     <c>ItemsContainer.DestroyItem</c>, which Nitrox transpiles to sync an <c>EntityDestroyed</c>.
        /// </remarks>
        private static int Emptied(Inventory inventory)
        {
            ItemsContainer container = inventory.container;
            if (container == null)
            {
                return 0;
            }

            List<TechType> held = new List<TechType>();
            foreach (InventoryItem item in container)
            {
                if (item?.item != null)
                {
                    held.Add(item.item.GetTechType());
                }
            }

            int destroyed = 0;
            foreach (TechType tech in held)
            {
                try
                {
                    if (container.DestroyItem(tech))
                    {
                        destroyed++;
                    }
                }
                catch (Exception failure)
                {
                    Log.Error($"{LmsMod.LogPrefix} could not destroy {tech}: {failure.Message}");
                }
            }

            return destroyed;
        }

        /// <summary>
        ///     Gives the kit, by the game's own item names.
        /// </summary>
        /// <remarks>
        ///     An unknown name is logged as an error. <c>Inventory.Pickup</c> is patched by Nitrox, so
        ///     the item becomes a synced entity.
        /// </remarks>
        private static IEnumerator Giving(Inventory inventory, IReadOnlyList<string> kit)
        {
            int given = 0;

            foreach (string name in kit)
            {
                if (!TechTypeExtensions.FromString(name, out TechType tech, true)
                    || tech == TechType.None)
                {
                    Log.Error($"{LmsMod.LogPrefix} start.kit names '{name}', which this game does " +
                              "not know. Nobody was given one.");
                    continue;
                }

                TaskResult<GameObject> result = new TaskResult<GameObject>();
                yield return CraftData.InstantiateFromPrefabAsync(tech, result);

                GameObject made = result.Get();
                if (made == null)
                {
                    Log.Error($"{LmsMod.LogPrefix} the game made no {tech} for start.kit.");
                    continue;
                }

                Pickupable pickup = made.GetComponent<Pickupable>();
                if (pickup == null)
                {
                    Log.Error($"{LmsMod.LogPrefix} start.kit names '{name}', which is not something " +
                              "that goes in an inventory.");
                    UnityEngine.Object.Destroy(made);
                    continue;
                }

                pickup.Initialize();

                if (inventory.Pickup(pickup))
                {
                    given++;
                }
                else
                {
                    Log.Warn($"{LmsMod.LogPrefix} there was no room for '{name}'.");
                    UnityEngine.Object.Destroy(made);
                }
            }

            Log.Info($"{LmsMod.LogPrefix} start.kit: {given} of {kit.Count} given.");
        }
    }
}
