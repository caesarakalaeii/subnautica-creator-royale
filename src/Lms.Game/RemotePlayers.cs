using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Nitrox's id for the player a game object belongs to, or nothing if it is not a player.
    ///     Shared by every weapon that reports a hit.
    /// </summary>
    /// <remarks>
    ///     <c>RemotePlayerIdentifier</c> lives in NitroxClient, which this assembly does not reference,
    ///     so it is resolved by name once. If that fails, no weapon can hurt anybody, and it is logged.
    /// </remarks>
    internal static class RemotePlayers
    {
        /// <summary>Where the id of a remote player is, in the assembly this one cannot see.</summary>
        private const string IdentifierType = "NitroxClient.GameLogic.PlayerLogic.RemotePlayerIdentifier";

        /// <summary>Whether the reflection below has been tried. It is tried once, not per swing.</summary>
        private static bool resolved;

        private static Type? identifier;
        private static FieldInfo? remotePlayer;
        private static PropertyInfo? playerId;

        /// <summary>
        ///     Whose body this is, or null if it is not somebody's.
        /// </summary>
        /// <remarks>
        ///     <c>GetComponentInParent</c>, because a trace returns the collider it met and the
        ///     identifier sits on the body above it.
        /// </remarks>
        internal static ushort? Identify(UnityEngine.GameObject hit)
        {
            if (hit == null || !Resolve())
            {
                return null;
            }

            UnityEngine.Component found = hit.GetComponentInParent(identifier!);
            if (found == null)
            {
                return null;
            }

            object? remote = remotePlayer!.GetValue(found);
            if (remote == null)
            {
                return null;
            }

            return (ushort)playerId!.GetValue(remote, null);
        }

        /// <summary>Resolves the reflection once per process; a failure logs one line.</summary>
        private static bool Resolve()
        {
            if (resolved)
            {
                return playerId != null;
            }

            resolved = true;

            identifier = AccessTools.TypeByName(IdentifierType);
            if (identifier == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} no {IdentifierType} in this Nitrox; nothing this mod " +
                         "reports can tell a player from a rock, so no weapon will hurt anybody.");
                return false;
            }

            remotePlayer = AccessTools.Field(identifier, "RemotePlayer");
            playerId = remotePlayer == null
                ? null
                : AccessTools.Property(remotePlayer.FieldType, "PlayerId");

            if (playerId == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} {IdentifierType} has no RemotePlayer.PlayerId in this " +
                         "Nitrox; no weapon will hurt anybody.");
                return false;
            }

            return true;
        }
    }
}
