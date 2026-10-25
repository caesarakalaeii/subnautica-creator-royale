using Lms.Core.Match;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Marks this player's own escape pod with a ping on the HUD compass, visible only to the
    ///     owner and only while a match is live.
    /// </summary>
    /// <remarks>
    ///     Anchored to <c>EscapePod.main</c>, which the fork moves to the local player's spot, so the
    ///     marker follows every placement. The anchor is local and unsynced; its <c>Lms</c> name prefix
    ///     keeps the fork's entity keying from touching it.
    /// </remarks>
    internal sealed class PodMarker : MonoBehaviour
    {
        private MatchFollower? match;
        private PingInstance? ping;
        private GameObject? anchor;

        private void Awake()
        {
            match = GetComponent<LmsRuntime>()?.Follower;
        }

        private void Update()
        {
            if (match == null)
            {
                return;
            }

            // Re-read per tick: EscapePod.main appears only after the scene loads and is moved, not
            // replaced, between rounds.
            EscapePod pod = EscapePod.main;
            if (pod == null)
            {
                return;
            }

            if (ping == null)
            {
                Make(pod);
                return;
            }

            if (anchor != null && anchor.transform.parent != pod.transform)
            {
                anchor.transform.SetParent(pod.transform, false);
            }

            bool show = match.IsLive;
            if (ping.visible != show)
            {
                ping.SetVisible(show);
            }
        }

        private void OnDestroy()
        {
            if (anchor != null)
            {
                Destroy(anchor);
            }
        }

        /// <remarks>
        ///     A real <see cref="PingInstance"/> draws through terrain at any range with a compass
        ///     bearing. It registers itself in <c>Start</c>.
        /// </remarks>
        private void Make(EscapePod pod)
        {
            anchor = new GameObject("LmsOwnPod");
            anchor.transform.SetParent(pod.transform, false);
            DontDestroyOnLoad(anchor);

            ping = anchor.AddComponent<PingInstance>();
            // origin is [AssertNotNull] and read during Start, so it has to be set before then.
            ping.origin = anchor.transform;
            ping.pingType = PingType.Signal;
            ping.SetLabel("Your pod");
            ping.displayPingInManager = false;
            ping.visible = false;
        }
    }
}
