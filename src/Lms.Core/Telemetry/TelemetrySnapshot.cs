using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Everything the casting booth is told in one push: a full state snapshot plus the events since
    ///     the last push.
    /// </summary>
    /// <remarks>
    ///     A full snapshot lets a dashboard join mid-match; events are keyed by audit sequence so gaps
    ///     are visible. Standings are sent only on change or heartbeat, versioned by
    ///     <see cref="StandingsRevision"/>.
    /// </remarks>
    public sealed class TelemetrySnapshot
    {
        /// <summary>
        ///     The contract version, written first on every push so a mismatched backend refuses the
        ///     payload instead of guessing.
        /// </summary>
        /// <remarks>
        ///     Bumping it requires updating the golden payload, the C# and Go tests, the Go decoder and
        ///     <c>feed.js</c>, which rejects any other version.
        /// </remarks>
        public const int Version = 6;

        private readonly ReadOnlyCollection<PlayerTelemetry> players;
        private readonly ReadOnlyCollection<EventTelemetry> events;
        private readonly ReadOnlyCollection<PodTelemetry> pods;

        public TelemetrySnapshot(DateTimeOffset at, MatchTelemetry? match, ZoneTelemetry? zone,
            IReadOnlyList<PlayerTelemetry>? players = null, IReadOnlyList<EventTelemetry>? events = null,
            StandingsTelemetry? standings = null, int standingsRevision = 0,
            IReadOnlyList<PodTelemetry>? pods = null)
        {
            if (standingsRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(standingsRevision), standingsRevision,
                    "A revision counts up from nothing.");
            }

            At = at;
            Match = match;
            Zone = zone;
            Standings = standings;
            StandingsRevision = standingsRevision;
            this.players = new ReadOnlyCollection<PlayerTelemetry>(
                players == null ? new List<PlayerTelemetry>() : new List<PlayerTelemetry>(players));
            this.events = new ReadOnlyCollection<EventTelemetry>(
                events == null ? new List<EventTelemetry>() : new List<EventTelemetry>(events));
            this.pods = new ReadOnlyCollection<PodTelemetry>(
                pods == null ? new List<PodTelemetry>() : new List<PodTelemetry>(pods));
        }

        /// <summary>When this was taken, by the sender's clock, in UTC.</summary>
        public DateTimeOffset At { get; }

        /// <summary>The match, or null when no tournament is running on the server.</summary>
        public MatchTelemetry? Match { get; }

        /// <summary>The wall, or null before a match starts.</summary>
        public ZoneTelemetry? Zone { get; }

        public IReadOnlyList<PlayerTelemetry> Players => players;

        /// <summary>
        ///     Every escape pod in the world. Never null; empty when there are no pods.
        /// </summary>
        public IReadOnlyList<PodTelemetry> Pods => pods;

        /// <summary>What happened since the last push, oldest first.</summary>
        public IReadOnlyList<EventTelemetry> Events => events;

        /// <summary>
        ///     The tournament table, or null. With an unchanged <see cref="StandingsRevision"/> null means
        ///     "keep what you have"; with a new revision it means "there is no table now".
        /// </summary>
        public StandingsTelemetry? Standings { get; }

        /// <summary>
        ///     Which version of the table this is; always present. Zero means no table has been sent yet.
        /// </summary>
        /// <remarks>
        ///     An explicit counter lets the booth tell "unchanged" from "cleared" without distinguishing
        ///     an absent key from null in Go.
        /// </remarks>
        public int StandingsRevision { get; }
    }
}
