using System;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Refuses the player-versus-player damage Nitrox's own relay sends this client, so a hit is
    ///     paid once, by <see cref="ZoneEnforcer.Hurt"/> with the server-judged amount.
    /// </summary>
    /// <remarks>
    ///     Applied whatever the config says. NitroxClient is not referenced, so the processor is found
    ///     by name; if it is missing this warns, since a contestant would then take damage twice.
    /// </remarks>
    internal static class PvPRelayGate
    {
        private const string Id = "lms.pvp.relay";

        private const string ProcessorType = "NitroxClient.Communication.Packets.Processors.PvPAttackProcessor";

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
                Type? processor = AccessTools.TypeByName(ProcessorType);
                MethodInfo? process = processor == null ? null : AccessTools.Method(processor, "Process");

                if (process == null)
                {
                    Log.Warn($"{LmsMod.LogPrefix} no {ProcessorType}.Process in this Nitrox, so " +
                             "nothing here can refuse its player-versus-player damage. If this " +
                             "server still relays it, every judged hit is paid twice.");
                    return;
                }

                new Harmony(Id).Patch(process,
                    prefix: new HarmonyMethod(typeof(PvPRelayGate), nameof(Refuse)));

                Log.Info($"{LmsMod.LogPrefix} Nitrox's own player-versus-player damage is refused on " +
                         "this client. Damage between players is the tournament's, judged by the " +
                         "server.");
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not refuse Nitrox's player-versus-player " +
                         $"damage: {ex.Message}");
            }
        }

        /// <summary>
        ///     Skips the original. Silent on purpose: the victim could not act on a message about it.
        /// </summary>
        private static bool Refuse() => false;
    }
}
