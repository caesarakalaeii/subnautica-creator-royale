using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Host for everything the tournament does per frame, on a <c>DontDestroyOnLoad</c> object so
    ///     it survives moving between the main menu and a session. Owns the state components share.
    /// </summary>
    internal sealed class LmsRuntime : MonoBehaviour
    {
        /// <summary>The tournament rules, or null when there is no usable config: readout only.</summary>
        internal TournamentConfig? Config { get; private set; }

        /// <summary>
        ///     Which revision of the rules this client is running. Zero is the local file.
        /// </summary>
        internal long Revision { get; private set; }

        /// <summary>
        ///     Raised when the server has replaced the rules, so components can rebuild what they
        ///     derived from them.
        /// </summary>
        /// <remarks>
        ///     Never unsubscribed: every handler lives on this same <c>DontDestroyOnLoad</c> object.
        /// </remarks>
        internal event System.Action? Reconfigured;

        /// <summary>
        ///     Takes rules the server sent, and tells everything that captured the old ones.
        /// </summary>
        /// <remarks>
        ///     Only newer revisions apply; an equal or older one is a resend. Only the client-side
        ///     settings are used; the zone itself always comes from the server.
        /// </remarks>
        internal void Adopt(TournamentConfig rules, long revision)
        {
            if (rules == null || revision <= Revision)
            {
                return;
            }

            Config = rules;
            Revision = revision;

            // Swapped in place, not rebuilt: every component holds this session object. A client
            // that started with no config has no session and stays readout-only.
            Spectator?.Adopt(rules.Spectator);

            // A patch, not a component, so it is re-applied rather than notified.
            MedKitGate.Apply(rules.MedKit, Follower);

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} the server replaced the tournament rules: revision {revision}. ",
                $"The wall is drawn in {rules.Colours.Now}, a bite lands every ",
                $"{rules.ZonePolicy.DamageInterval.TotalSeconds:0.#}s, and containment is ",
                $"{(rules.Flags.EnforceContainment ? "on" : "off")}."));

            Reconfigured?.Invoke();
        }

        /// <summary>
        ///     The match this client is following, as the server reports it, or null with no config.
        /// </summary>
        internal MatchFollower? Follower { get; private set; }

        /// <summary>
        ///     What the match has left to do with this player, or null when there is no config.
        ///     Shared so the zone, ring and spectator adapter agree whether the player is still in.
        /// </summary>
        internal SpectatorSession? Spectator { get; private set; }

        /// <summary>
        ///     What has happened to this player and not yet been reported, in order.
        /// </summary>
        /// <remarks>
        ///     Lives here so the producers (zone, ring) need not know about the reporter that drains it.
        /// </remarks>
        internal PlayerEventLog Events { get; } = new PlayerEventLog();

        /// <summary>
        ///     Whether this client is a ghost (an invisible, immune caster). Only the server sets it,
        ///     since it grants immunity; false until told and between sessions.
        /// </summary>
        internal bool IsGhost { get; private set; }

        /// <summary>Takes what the server said about this client's role, and says so once.</summary>
        internal void Told(GhostBroadcast role)
        {
            if (IsGhost == role.IsGhost)
            {
                return;
            }

            IsGhost = role.IsGhost;

            // Logged, since being a ghost is otherwise only a set of absences.
            Log.Info($"{LmsMod.LogPrefix} the server says you are {role}");
        }

        /// <summary>Forgotten when the session goes, the same way the match is.</summary>
        internal void ForgetRole() => IsGhost = false;

        private void Awake()
        {
            Log.Info($"{LmsMod.LogPrefix} runtime attached to '{gameObject.name}'");

            // Before the components: AddComponent runs Awake immediately, and they read the match here.
            Config = TournamentConfigFile.Load();
            if (Config != null)
            {
                Follower = new MatchFollower();
                Spectator = new SpectatorSession(Config.Spectator);
            }

            // Before anything can open the options panel.
            Preferences.Declare();

            gameObject.AddComponent<BiomeSampler>();
            gameObject.AddComponent<ZoneEnforcer>();
            gameObject.AddComponent<ZoneRenderer>();
            gameObject.AddComponent<ZoneHud>();
            gameObject.AddComponent<PodMarker>();
            gameObject.AddComponent<MatchListener>();

            // Harmony patches, applied whatever the config says: the server decides the rules.
            PrawnStrikeReporter.Apply();
            BladeStrikeReporter.Apply();
            PvPRelayGate.Apply();
            RemotePlayerVisibility.Apply();

            gameObject.AddComponent<SpectatorAdapter>();
            gameObject.AddComponent<BeaconGate>();
            gameObject.AddComponent<RingEnforcer>();
            gameObject.AddComponent<GhostSurvival>();

            // Before GhostCamera and CasterHud, which read it in their Awake.
            gameObject.AddComponent<CasterSightings>();
            gameObject.AddComponent<GhostCamera>();
            gameObject.AddComponent<CasterHud>();

            gameObject.AddComponent<RoundKit>();
            gameObject.AddComponent<RoundRecipes>();

            if (Config != null)
            {
                MedKitGate.Apply(Config.MedKit, Follower);
            }

            // After the sampler, which it reads.
            gameObject.AddComponent<TelemetryReporter>();

            // Diagnostic console commands; they need no config.
            gameObject.AddComponent<AtmosphereSurveyCommand>();
            gameObject.AddComponent<PodPrefabSurveyCommand>();
        }
    }
}
