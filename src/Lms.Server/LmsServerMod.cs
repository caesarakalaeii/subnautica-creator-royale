using System;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Entry point the Nitrox fork calls (<c>Nitrox.Server.Subnautica.LmsTournament</c>); the only
    ///     public surface of this assembly. Match logic lives in <c>Lms.Core.MatchHost</c>.
    /// </summary>
    public static class LmsServerMod
    {
        /// <summary>Prefix for every log line from this assembly: UTC, no world time.</summary>
        internal static string LogPrefix => LogStamp.Prefix(DateTimeOffset.UtcNow, null);

        /// <summary>
        ///     Reads the tournament config and starts a match, or returns null so the server runs as
        ///     stock Nitrox.
        /// </summary>
        /// <param name="directory">Where the server is, and where its config file is looked for.</param>
        /// <param name="broadcast">Sends a line to every connected player.</param>
        /// <param name="sendTo">Sends a line to one player, by their Nitrox id.</param>
        /// <returns>The tournament, or null when there is no usable config.</returns>
        /// <remarks>
        ///     The fork finds this by reflection, so the signature is a contract. The delegates are plain
        ///     <c>Action</c>s so this assembly never touches a Nitrox packet.
        /// </remarks>
        public static ILmsTournamentHost? Initialize(string directory, Action<string> broadcast,
            Action<ushort, string> sendTo)
        {
            if (broadcast == null)
            {
                throw new ArgumentNullException(nameof(broadcast));
            }

            if (sendTo == null)
            {
                throw new ArgumentNullException(nameof(sendTo));
            }

            Log.Info($"{LogPrefix} Last Man Standing server {Version()} starting");

            Lms.Core.Configuration.TournamentConfig? config =
                TournamentConfigFile.Load(directory, out string yaml);
            if (config == null)
            {
                return null;
            }

            PlacePlayers(config);

            return new TournamentHost(config, directory, broadcast, sendTo, yaml);
        }

        /// <summary>
        ///     Tells the fork where players start. Re-run when a new config is adopted, since
        ///     placement comes from <c>spawn:</c>.
        /// </summary>
        /// <remarks>
        ///     Without <c>spawn.ownPod</c>, players join exactly as in stock Nitrox.
        /// </remarks>
        internal static void PlacePlayers(Lms.Core.Configuration.TournamentConfig config)
        {
            if (!config.Spawn.OwnPod)
            {
                Log.Info($"{LogPrefix} spawning is Nitrox's own: one escape pod between everybody.");
                LmsSpawn.Placement = null;
                return;
            }

            LmsSpawn.Placement = new SpawnPlacement(config.Spawn, config.Zone.Opening);

            // Name the placement: a circle and a scatter need different config fixes.
            Log.Info(config.Spawn.OnACircle
                ? Readable.Line(
                    $"{LogPrefix} a pod each for a group of {config.Spawn.GroupSize}, ",
                    $"on a circle {config.Spawn.CircleRadius:0} m round the origin rather than on the ",
                    $"game's own start points. What the circle manages is said on the first join.")
                : Readable.Line(
                    $"{LogPrefix} a pod each for a group of {config.Spawn.GroupSize}, ",
                    $"at least {config.Spawn.MinSeparation:0} m apart, inside the opening zone ",
                    $"{config.Zone.Opening}. What the arena manages is said on the first join."));
        }

        private static string Version() => typeof(LmsServerMod).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
