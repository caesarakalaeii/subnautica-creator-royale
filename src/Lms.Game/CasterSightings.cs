using System;
using System.Collections.Generic;
using Lms.Core.Match;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     What the server has told this client about the contestants, and who the booth wants
    ///     filmed. Read by <see cref="CasterHud"/> and <see cref="GhostCamera"/>.
    /// </summary>
    /// <remarks>
    ///     Only a ghost ever receives the feed. Uses <c>Time.time</c>, not the world clock: prediction
    ///     needs a monotonic clock, and the world clock stops while nobody is connected.
    /// </remarks>
    internal sealed class CasterSightings : MonoBehaviour
    {
        private readonly CasterTrack track = new CasterTrack();

        private LmsRuntime? runtime;

        /// <summary>Who the booth has asked every camera to stay on.</summary>
        internal FollowBroadcast Follow { get; private set; } = FollowBroadcast.LetGo;

        /// <summary>Everybody the newest feed named, in the order the server named them.</summary>
        internal IReadOnlyList<string> Named => track.Names;

        /// <summary>Where somebody probably is now, and how they were doing when last heard of.</summary>
        internal bool TryFind(string? name, out CasterSighting sighting) =>
            track.TryFind(name, Now, out sighting);

        /// <summary>Takes one feed from the server.</summary>
        internal void Receive(CasterFeed feed) => track.Receive(feed, Now);

        /// <summary>
        ///     Takes the booth's instruction to stay on somebody, or to let go, and logs the change.
        /// </summary>
        internal void Told(FollowBroadcast follow)
        {
            if (follow.Equals(Follow))
            {
                return;
            }

            Follow = follow;

            Log.Info($"{LmsMod.LogPrefix} the booth says: {follow}.");
        }

        /// <summary>
        ///     Drops everything on the way out of a ghost's role, so stale positions never reach a
        ///     contestant's screen.
        /// </summary>
        internal void Forget()
        {
            track.Receive(new CasterFeed(null), Now);
            Follow = FollowBroadcast.LetGo;
        }

        private static TimeSpan Now => TimeSpan.FromSeconds(Time.time);

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();

            if (runtime == null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (runtime == null)
            {
                return;
            }

            // Only leaving the role needs action; entering it just means feeds start arriving.
            if (!runtime.IsGhost && (Named.Count > 0 || Follow.Following))
            {
                Forget();
            }
        }
    }
}
