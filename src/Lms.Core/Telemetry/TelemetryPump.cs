using System;
using System.Collections.Generic;
using Lms.Core.Audit;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Schedules telemetry pushes to the casting booth and decides what each one carries.
    /// </summary>
    /// <remarks>
    ///     One push in flight at a time; a cursor over the audit log so refused events are resent and
    ///     accepted ones are not; and a cap per push, since ingest rejects bodies over 1 MB. Cadence
    ///     uses wall-clock time because Nitrox's world clock stops while nobody is connected. Not
    ///     thread-safe; the host drives it under its match lock.
    /// </remarks>
    public sealed class TelemetryPump
    {
        /// <summary>
        ///     The most events one push carries; keeps a push well under ingest's 1 MB limit.
        /// </summary>
        private const int MaxEventsPerPush = 500;

        /// <summary>
        ///     Maximum gap between pushes while the booth is down.
        /// </summary>
        private static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     How often an unchanged tournament table is resent, so a restarted booth recovers it.
        /// </summary>
        private static readonly TimeSpan StandingsHeartbeat = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     A push may be early by interval / this, absorbing timer jitter so a slightly early tick
        ///     does not skip a slot.
        /// </summary>
        /// <remarks>
        ///     Assumes the server tick divides the interval (it is a quarter second).
        /// </remarks>
        private const int SlackDivisor = 20;

        private readonly TelemetryPolicy policy;

        private TimeSpan backoff;
        private DateTimeOffset dueAt;
        private DateTimeOffset takenAt;
        private bool inFlight;
        private bool started;

        /// <summary>The highest sequence in the push currently in flight.</summary>
        private long sent;

        /// <summary>
        ///     The table the booth has actually taken. Committed on <see cref="Accepted"/>, not
        ///     <see cref="Take"/>, so a table that changed during an outage is still resent.
        /// </summary>
        private StandingsTelemetry? acknowledgedStandings;

        /// <summary>The table this push is carrying, and the revision it is carrying it as.</summary>
        private StandingsTelemetry? offeredStandings;
        private int offeredRevision;

        /// <summary>When the table was last accepted, for the heartbeat.</summary>
        private DateTimeOffset standingsSentAt;
        private bool standingsEverSent;

        public TelemetryPump(TelemetryPolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            backoff = policy.Interval;
        }

        /// <summary>
        ///     The highest audit sequence the booth has taken. Everything after it is still owed.
        /// </summary>
        public long Acknowledged { get; private set; }

        /// <summary>
        ///     Which version of the tournament table the booth has taken. Zero before the first.
        /// </summary>
        public int StandingsRevision { get; private set; }

        /// <summary>
        ///     Whether a push is due at this wall-clock time. Pure.
        /// </summary>
        /// <remarks>
        ///     An unanswered push is abandoned after timeout plus interval. The HTTP client must give up
        ///     after <see cref="TelemetryPolicy.Timeout"/>, or a late answer could acknowledge the push
        ///     that replaced it.
        /// </remarks>
        public bool IsDue(DateTimeOffset now)
        {
            if (!policy.Enabled)
            {
                return false;
            }

            if (inFlight)
            {
                return now - takenAt >= policy.Timeout + policy.Interval;
            }

            return !started || now >= dueAt;
        }

        /// <summary>
        ///     Takes the payload to push now, and marks one push in flight.
        /// </summary>
        /// <param name="at">Wall-clock time in UTC; the snapshot time and the cadence's clock.</param>
        /// <param name="match">The match, or null when the server has none.</param>
        /// <param name="zone">The wall, or null before a match starts.</param>
        /// <param name="players">Everybody in the world, alive or spectating.</param>
        /// <param name="log">The audit log, read from the cursor.</param>
        /// <param name="standings">The tournament table, or null when no match has finished.</param>
        /// <param name="pods">Every escape pod in the world.</param>
        /// <remarks>
        ///     The table is included only when it changed or the heartbeat is due; otherwise the block is
        ///     null with the unchanged revision, telling the booth to keep what it has.
        /// </remarks>
        public TelemetrySnapshot Take(DateTimeOffset at, MatchTelemetry? match,
            ZoneTelemetry? zone, IReadOnlyList<PlayerTelemetry> players, AuditLog log,
            StandingsTelemetry? standings = null, IReadOnlyList<PodTelemetry>? pods = null)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            if (!IsDue(at))
            {
                // Would put a second push in flight.
                throw new InvalidOperationException(
                    "No push is due: ask IsDue first, and answer the one in flight with Accepted or Refused.");
            }

            List<EventTelemetry> owed = Owed(log);

            Offer(at, standings);

            started = true;
            inFlight = true;
            takenAt = at;
            sent = examined;

            return new TelemetrySnapshot(at, match, zone, players, owed,
                offeredStandings, offeredRevision, pods);
        }

        /// <summary>
        ///     Decides whether this push carries the table, and as which revision.
        /// </summary>
        /// <remarks>
        ///     The revision is relative to what the booth has accepted, not what was last offered, so a
        ///     refused push and its retry use the same number. A change to null also bumps it.
        /// </remarks>
        private void Offer(DateTimeOffset at, StandingsTelemetry? standings)
        {
            bool moved = standings == null
                ? acknowledgedStandings != null
                : !standings.Equals(acknowledgedStandings);

            bool due = !standingsEverSent || at - standingsSentAt >= StandingsHeartbeat;

            if (!moved && !due)
            {
                offeredStandings = null;
                offeredRevision = StandingsRevision;
                return;
            }

            offeredStandings = standings;
            offeredRevision = moved ? StandingsRevision + 1 : StandingsRevision;
        }

        /// <summary>
        ///     The booth accepted the push in flight: its events are no longer owed and the cadence
        ///     returns to normal. Ingest's <c>lastSequence</c> reply is not needed, since it accepts all or nothing.
        /// </summary>
        public void Accepted()
        {
            if (!inFlight)
            {
                return;
            }

            Acknowledged = sent;

            // Only a push that carried the table (or a new revision) commits it.
            if (offeredRevision != StandingsRevision || offeredStandings != null)
            {
                acknowledgedStandings = offeredStandings;
                StandingsRevision = offeredRevision;
                standingsSentAt = takenAt;
                standingsEverSent = true;
            }

            backoff = policy.Interval;
            Finish();
        }

        /// <summary>
        ///     The push failed: its events are still owed, and the interval backs off until the booth answers.
        /// </summary>
        public void Refused()
        {
            if (!inFlight)
            {
                return;
            }

            TimeSpan doubled = backoff + backoff;
            backoff = doubled > BackoffCap ? BackoffCap : doubled;
            Finish();
        }

        private void Finish()
        {
            inFlight = false;
            dueAt = takenAt + backoff - TimeSpan.FromTicks(backoff.Ticks / SlackDivisor);
        }

        /// <summary>
        ///     The highest sequence this push has dealt with, sent or skipped, so skipped position
        ///     samples are not rescanned on every push.
        /// </summary>
        private long examined;

        /// <summary>
        ///     Events owed to the booth, oldest first and capped at <see cref="MaxEventsPerPush"/>.
        /// </summary>
        /// <remarks>
        ///     Position samples are skipped: every push already carries live positions, and samples would
        ///     flood the booth's bounded event window.
        /// </remarks>
        private List<EventTelemetry> Owed(AuditLog log)
        {
            List<EventTelemetry> owed = new List<EventTelemetry>();
            examined = Acknowledged;

            foreach (AuditRecord record in log.Records)
            {
                if (record.Sequence <= Acknowledged)
                {
                    continue;
                }

                if (record.Kind == AuditRecordKind.PlayerPosition)
                {
                    examined = record.Sequence;
                    continue;
                }

                owed.Add(EventTelemetry.Of(record));
                examined = record.Sequence;

                if (owed.Count == MaxEventsPerPush)
                {
                    break;
                }
            }

            return owed;
        }
    }
}
