using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Leaves a ghost's body undrawn on this client.
    /// </summary>
    /// <remarks>
    ///     Nitrox's <c>RemotePlayer.UpdatePosition</c> calls <c>Body.SetActive(true)</c> on every
    ///     movement packet, so a postfix there hides it again. Deactivating <c>Body</c>, not the model,
    ///     also removes the collider and sounds. NitroxClient is not referenced, so this is by name.
    /// </remarks>
    internal static class RemotePlayerVisibility
    {
        private const string Id = "lms.ghost.visibility";

        /// <summary>The fork's own type, resolved by name so this assembly need not reference it.</summary>
        private const string RemotePlayerType = "NitroxClient.GameLogic.RemotePlayer";

        private static bool applied;

        /// <summary>
        ///     Whose body is not to be drawn, by name, case-insensitive like <c>GhostRoster</c>.
        /// </summary>
        private static readonly HashSet<string> hidden =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static PropertyInfo? bodyOf;
        private static PropertyInfo? nameOf;

        /// <summary>How many names this client has been told to hide, for the log line.</summary>
        internal static int Count => hidden.Count;

        /// <summary>Applies the patches, once. Safe to call before any remote player exists.</summary>
        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            Type? remote = AccessTools.TypeByName(RemotePlayerType);
            if (remote == null)
            {
                Log.Error($"{LmsMod.LogPrefix} no {RemotePlayerType} to patch, so a ghost will be " +
                          "visible to everybody. Is this the tournament's Nitrox fork?");
                return;
            }

            bodyOf = AccessTools.Property(remote, "Body");
            nameOf = AccessTools.Property(remote, "PlayerName");

            if (bodyOf == null || nameOf == null)
            {
                Log.Error($"{LmsMod.LogPrefix} {RemotePlayerType} has no " +
                          $"{(bodyOf == null ? "Body" : "PlayerName")}, so a ghost will be visible " +
                          "to everybody. Has Nitrox been rebased?");
                return;
            }

            Harmony harmony = new Harmony(Id);

            // UpdatePosition re-activates the body on every packet. The other two cover a body being
            // created and moving inside a Cyclops, where a still player sends no movement packets.
            bool moved = Patch(harmony, remote, "UpdatePosition");
            bool made = Patch(harmony, remote, "InitializeGameObject");
            bool piloted = Patch(harmony, remote, "UpdatePositionInCyclops");

            if (moved && made && piloted)
            {
                Log.Info($"{LmsMod.LogPrefix} ghosts will not be drawn on this client");
                return;
            }

            Log.Error($"{LmsMod.LogPrefix} a ghost may be visible on this client " +
                      $"(movement: {moved}, spawn: {made}, cyclops: {piloted}).");
        }

        /// <summary>Takes the list the server sent, and applies it to whoever is already in the world.</summary>
        internal static void Told(IReadOnlyList<string> names)
        {
            hidden.Clear();

            foreach (string name in names)
            {
                hidden.Add(name);
            }

            // Now, not on the next packet: a player standing still sends none.
            Sweep();
        }

        /// <summary>Clears the list with the session.</summary>
        internal static void Forget()
        {
            hidden.Clear();
        }

        /// <summary>Whether this name is one the server said to hide.</summary>
        private static bool IsHidden(string? name) =>
            !string.IsNullOrEmpty(name) && hidden.Contains(name!);

        /// <summary>
        ///     Hides every remote player already in the world who is on the list.
        /// </summary>
        /// <remarks>
        ///     Nitrox names each unparented remote body after its player, so <c>GameObject.Find</c>
        ///     finds it. Revealing is left to the next movement packet.
        /// </remarks>
        private static void Sweep()
        {
            foreach (string name in hidden)
            {
                GameObject? body = GameObject.Find(name);

                if (body != null && body.activeSelf)
                {
                    body.SetActive(false);
                }
            }
        }

        /// <summary>
        ///     A remote player has just been placed, moved, or created. Undraw it if it is a ghost.
        /// </summary>
        /// <remarks>
        ///     <c>__instance</c> is <see cref="object"/> because this assembly does not reference the
        ///     patched type; Harmony accepts that.
        /// </remarks>
        private static void AfterUpdate(object __instance)
        {
            if (hidden.Count == 0 || bodyOf == null || nameOf == null)
            {
                return;
            }

            try
            {
                if (!IsHidden(nameOf.GetValue(__instance) as string))
                {
                    return;
                }

                if (bodyOf.GetValue(__instance) is GameObject body && body.activeSelf)
                {
                    body.SetActive(false);
                }
            }
            catch (Exception failure)
            {
                // Never throw into Nitrox's movement path: it would fire per packet per player.
                Log.Error($"{LmsMod.LogPrefix} could not hide a ghost: {failure.Message}");
            }
        }

        private static bool Patch(Harmony harmony, Type remote, string method)
        {
            try
            {
                MethodInfo original = AccessTools.Method(remote, method);
                if (original == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no RemotePlayer.{method} to patch.");
                    return false;
                }

                harmony.Patch(original, postfix: new HarmonyMethod(
                    typeof(RemotePlayerVisibility).GetMethod(nameof(AfterUpdate),
                        BindingFlags.NonPublic | BindingFlags.Static)));

                return true;
            }
            catch (Exception failure)
            {
                Log.Error($"{LmsMod.LogPrefix} could not patch RemotePlayer.{method}: {failure.Message}");
                return false;
            }
        }
    }
}
