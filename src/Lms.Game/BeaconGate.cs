using System;
using System.Collections.Generic;
using Lms.Core.Audit;
using Lms.Core.Match;
using Lms.Core.Settings;
using Lms.Core.Telemetry;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Keeps other players' locations off a contestant's screen: beacons, and the signal pings
    ///     that leak the same fact. <see cref="BeaconRule"/> decides who may see them.
    /// </summary>
    /// <remarks>
    ///     Swept every tick, since a player can re-enable a ping from the PDA. Nitrox hangs a
    ///     <c>Signal</c> ping with the player's name on every remote player; story signals, the own pod
    ///     and the mod's own <c>Lms</c>-anchored pings are left alone.
    /// </remarks>
    internal sealed class BeaconGate : MonoBehaviour
    {
        private LmsRuntime? runtime;
        private MatchFollower? match;
        private PlayerEventLog? events;

        /// <summary>Rebuilt from the config on every rules push.</summary>
        private BeaconRule rule = BeaconRule.Hidden;

        /// <summary>
        ///     The beacons this client knows about, by the game's own id.
        /// </summary>
        /// <remarks>
        ///     <c>PingManager</c>'s dictionary is private, so this tracks its registration events.
        /// </remarks>
        private readonly Dictionary<string, PingInstance> beacons = new Dictionary<string, PingInstance>();

        /// <summary>
        ///     The signal pings this client knows about, by the game's own id.
        /// </summary>
        /// <remarks>
        ///     Only the ones that are somebody's location; see <see cref="IsALocation"/>.
        /// </remarks>
        private readonly Dictionary<string, PingInstance> signals = new Dictionary<string, PingInstance>();

        /// <summary>Pings already recorded as re-enabled, so each is recorded once.</summary>
        private readonly HashSet<string> reported = new HashSet<string>();

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();

            if (runtime == null)
            {
                enabled = false;
                return;
            }

            match = runtime.Follower;
            events = runtime.Events;

            Reconfigure();
            runtime.Reconfigured += Reconfigure;

            PingManager.onAdd += Added;
            PingManager.onRemove += Removed;
        }

        private void OnDestroy()
        {
            if (runtime != null)
            {
                runtime.Reconfigured -= Reconfigure;
            }

            PingManager.onAdd -= Added;
            PingManager.onRemove -= Removed;
        }

        /// <remarks>
        ///     Rebuilt rather than kept, because <c>lms rules push</c> can change it between groups.
        /// </remarks>
        private void Reconfigure()
        {
            rule = BeaconRule.For(runtime?.Config?.Flags.HideBeacons ?? true);
        }

        private void Added(PingInstance ping)
        {
            if (ping == null)
            {
                return;
            }

            if (ping.pingType == PingType.Beacon)
            {
                beacons[ping.Id] = ping;
            }
            else if (ping.pingType == PingType.Signal && IsALocation(ping))
            {
                signals[ping.Id] = ping;
            }
            else if (SignalLocation.IsMarker(ping.name))
            {
                // The fork sets pingType after the ping registers, so a marker can arrive before
                // it says Signal. The name is set before registration and cannot be ordered wrong.
                signals[ping.Id] = ping;
            }
            else
            {
                return;
            }

            // Immediately as well as on the tick, so it never renders for a frame.
            Apply(ping, sayIfRefused: false);
        }

        private void Removed(string id)
        {
            beacons.Remove(id);
            signals.Remove(id);
            reported.Remove(id);
        }

        private void Update()
        {
            if (beacons.Count == 0 && signals.Count == 0)
            {
                return;
            }

            bool ghost = runtime != null && runtime.IsGhost;

            if (rule.Draws(ghost))
            {
                return;
            }

            foreach (KeyValuePair<string, PingInstance> beacon in beacons)
            {
                Apply(beacon.Value, sayIfRefused: true);
            }

            foreach (KeyValuePair<string, PingInstance> signal in signals)
            {
                Apply(signal.Value, sayIfRefused: true);
            }
        }

        /// <param name="ping">The ping to hide.</param>
        /// <param name="sayIfRefused">
        ///     Whether hiding it is worth a record. False on arrival: a new ping was not turned back on.
        /// </param>
        private void Apply(PingInstance ping, bool sayIfRefused)
        {
            if (ping == null)
            {
                return;
            }

            bool ghost = runtime != null && runtime.IsGhost;

            if (rule.Draws(ghost))
            {
                return;
            }

            if (!ping.visible)
            {
                return;
            }

            bool refused = sayIfRefused && rule.Refuses(wasVisible: true, isGhost: ghost);

            ping.SetVisible(false);

            if (!refused || !reported.Add(ping.Id))
            {
                return;
            }

            Record(ping);
        }

        /// <summary>
        ///     Whether this signal ping is somebody's location rather than a story signal.
        /// </summary>
        /// <remarks>
        ///     Gathers the facts for <see cref="SignalLocation"/>, which decides: the object name,
        ///     whether a story <see cref="SignalPing"/> is attached, and whether it hangs off another pod.
        /// </remarks>
        private static bool IsALocation(PingInstance ping)
        {
            EscapePod? home = EscapePod.main;
            EscapePod? pod = ping.GetComponentInParent<EscapePod>();

            return SignalLocation.IsALocation(
                ping.name,
                ping.GetComponent<SignalPing>() != null,
                pod != null && pod != home);
        }

        /// <remarks>
        ///     Once per ping, not per tick.
        /// </remarks>
        private void Record(PingInstance ping)
        {
            Log.Info($"{LmsMod.LogPrefix} a {ping.pingType.ToString().ToLowerInvariant()} ping was " +
                     "turned back on and has been hidden again; " +
                     $"{rule}.");

            if (events == null || match == null || !match.HasMatch)
            {
                return;
            }

            events.Add(match.Generation,
                new PlayerEvent(AuditRecordKind.BeaconRefused, match.Elapsed,
                    detail: ping.pingType.ToString()));
        }
    }
}
