using System;
using System.Collections;
using System.Collections.Generic;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Grants the blueprints a round starts with: <c>start.recipes</c>, minus
    ///     <c>start.lockedRecipes</c>.
    /// </summary>
    /// <remarks>
    ///     The game re-grants its default blueprints on every load, so an empty list is the vanilla
    ///     start. <c>all</c> uses <c>KnownTech.GetAllUnlockables()</c>, which only the client can
    ///     enumerate. Nitrox already syncs each <c>KnownTech.Add</c> into the player's own progress.
    ///     Re-asserted every round; already-known blueprints cost nothing. Nothing is revoked.
    /// </remarks>
    internal sealed class RoundRecipes : MonoBehaviour
    {
        /// <summary>
        ///     How many to grant before yielding a frame. Each grant sends a packet, and round start
        ///     is already busy.
        /// </summary>
        private const int PerFrame = 25;

        private MatchFollower? match;
        private RoundBroadcast? pending;
        private RoundBroadcast? done;
        private bool granting;

        /// <summary>Takes an instruction. Applied on a frame that has a game, not here.</summary>
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
            if (pending == null || granting || match == null || Player.main == null)
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
                Log.Info($"{LmsMod.LogPrefix} ignored a recipe grant for " +
                         $"{instruction.Group}.{instruction.Generation}; this client is in " +
                         $"{match.Group}.{match.Generation}.");
                return;
            }

            if (done != null && done.Value.Equals(instruction))
            {
                // A resend; skip walking hundreds of tech types again.
                pending = null;
                return;
            }

            pending = null;
            done = instruction;

            StartCoroutine(Granting(instruction));
        }

        private IEnumerator Granting(RoundBroadcast instruction)
        {
            granting = true;

            HashSet<TechType> withheld = Named(instruction.Locked, "start.lockedRecipes");
            bool everything = GrantsEverything(instruction.Recipes);
            ICollection<TechType> floor = everything
                ? KnownTech.GetAllUnlockables()
                : Named(instruction.Recipes, "start.recipes");

            int granted = 0;
            int since = 0;

            foreach (TechType tech in floor)
            {
                if (withheld.Contains(tech) || KnownTech.Contains(tech))
                {
                    continue;
                }

                // `verbose: false`: true queues a "new blueprint" popup per item, on every client.
                if (KnownTech.Add(tech, verbose: false))
                {
                    granted++;
                }

                if (++since < PerFrame)
                {
                    continue;
                }

                since = 0;
                yield return null;
            }

            string what = everything ? "every blueprint the game has" : "named by start.recipes";

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} round {instruction.Group}.{instruction.Generation}: ",
                $"{granted} recipe(s) granted of {floor.Count} in the floor ({what}), ",
                $"{withheld.Count} withheld by start.lockedRecipes. ",
                $"Anything else is scanned for."));

                // Only when something landed, so the message is not noise every round.
            if (granted > 0)
            {
                ErrorMessage.AddMessage(everything
                    ? (withheld.Count == 0
                        ? "LMS: every blueprint is yours."
                        : $"LMS: every blueprint is yours but {withheld.Count}.")
                    : $"LMS: {granted} blueprint(s) to start with. Scan for the rest.");
            }

            granting = false;
        }

        /// <summary>Whether the instruction asks for every blueprint the game has.</summary>
        /// <remarks>
        ///     <c>StartPolicy</c> has already refused <c>all</c> mixed with other names.
        /// </remarks>
        private static bool GrantsEverything(IReadOnlyList<string> recipes)
        {
            foreach (string name in recipes)
            {
                if (string.Equals(name, StartPolicy.Everything, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     A list of config names as the game's own tech types.
        /// </summary>
        /// <remarks>
        ///     An unknown name is logged as an error with its field, since the operator has no other
        ///     way to find out.
        /// </remarks>
        private static HashSet<TechType> Named(IReadOnlyList<string> names, string field)
        {
            HashSet<TechType> resolved = new HashSet<TechType>();

            foreach (string name in names)
            {
                if (!TechTypeExtensions.FromString(name, out TechType tech, true)
                    || tech == TechType.None)
                {
                    Log.Error($"{LmsMod.LogPrefix} {field} names '{name}', which this game does " +
                              "not know. IT HAS BEEN SKIPPED.");
                    continue;
                }

                resolved.Add(tech);
            }

            return resolved;
        }
    }
}
