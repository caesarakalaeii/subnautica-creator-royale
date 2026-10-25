using System;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Schedules console-channel posts: on change, plus a slow heartbeat so a restarted booth
    ///     recovers the state.
    /// </summary>
    /// <remarks>
    ///     One report in flight at a time, so two cannot race to be the one the booth keeps. Not
    ///     thread-safe; the host drives it under its match lock.
    /// </remarks>
    public sealed class ConsolePump
    {
        /// <summary>
        ///     How often an unchanged report is sent again.
        /// </summary>
        public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);

        /// <summary>How far apart reports get while the booth is refusing them.</summary>
        private static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(60);

        private readonly TimeSpan timeout;

        private ConsoleReport? accepted;
        private ConsoleReport? offered;
        private DateTimeOffset takenAt;
        private DateTimeOffset sentAt;
        private TimeSpan backoff = TimeSpan.Zero;
        private bool inFlight;
        private bool everSent;

        /// <param name="timeout">
        ///     How long one report is given before it is abandoned. The HTTP client must give up by then,
        ///     or a late answer would acknowledge the report that replaced it.
        /// </param>
        public ConsolePump(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "A timeout of nothing would abandon every report before it left.");
            }

            this.timeout = timeout;
        }

        /// <summary>
        ///     Whether this freshly built report is worth sending now. Pure; called once per tick.
        /// </summary>
        public bool IsDue(DateTimeOffset now, ConsoleReport report)
        {
            if (report == null)
            {
                return false;
            }

            if (inFlight)
            {
                return now - takenAt >= timeout + Heartbeat;
            }

            if (!everSent)
            {
                return true;
            }

            if (now < sentAt + backoff)
            {
                return false;
            }

            return !report.Equals(accepted) || now - sentAt >= Heartbeat;
        }

        /// <summary>Marks one report in flight and hands it back.</summary>
        public ConsoleReport Take(DateTimeOffset now, ConsoleReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (!IsDue(now, report))
            {
                throw new InvalidOperationException(
                    "No report is due: ask IsDue first, and answer the one in flight.");
            }

            offered = report;
            takenAt = now;
            inFlight = true;

            return report;
        }

        /// <summary>
        ///     The booth accepted the report in flight; commits it as the last one sent.
        /// </summary>
        /// <returns>What it carried, which the caller then forgets from each queue.</returns>
        /// <remarks>
        ///     Committed here rather than in <see cref="Take"/>, so a change made during an outage is
        ///     still sent afterwards.
        /// </remarks>
        public ConsoleCarried Accepted()
        {
            if (!inFlight)
            {
                return default;
            }

            ConsoleCarried carried = new ConsoleCarried(
                offered?.Answers.Count ?? 0,
                offered?.Rules.Count ?? 0);

            accepted = offered;
            sentAt = takenAt;
            everSent = true;
            backoff = TimeSpan.Zero;
            inFlight = false;

            return carried;
        }

        /// <summary>It did not land: nothing is forgotten and the booth is asked less often.</summary>
        public void Refused()
        {
            if (!inFlight)
            {
                return;
            }

            TimeSpan doubled = backoff == TimeSpan.Zero ? Heartbeat : backoff + backoff;
            backoff = doubled > BackoffCap ? BackoffCap : doubled;

            sentAt = takenAt;
            everSent = true;
            inFlight = false;
        }
    }
}
