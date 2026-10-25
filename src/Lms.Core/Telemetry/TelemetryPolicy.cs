using System;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Server-side config for pushing telemetry to the casting booth: where, and how often.
    /// </summary>
    /// <remarks>
    ///     The ingest token is not configured here; it comes from the <c>LMS_INGEST_TOKEN</c>
    ///     environment variable, because the config file is distributed to players.
    /// </remarks>
    public sealed class TelemetryPolicy
    {
        public TelemetryPolicy(string? url, TimeSpan interval, TimeSpan timeout,
            bool acceptsCommands = true)
        {
            if (url != null && !IsPushable(url))
            {
                throw new ArgumentException(
                    $"'{url}' is not an http or https address. Write the booth's ingest endpoint in full, " +
                    "as http://host:8080/ingest.", nameof(url));
            }

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval,
                    "A cadence of nothing is not a cadence; leave the url out to push nowhere.");
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), timeout,
                    "A timeout of nothing would refuse every push before it left.");
            }

            Url = url;
            Interval = interval;
            Timeout = timeout;
            AcceptsCommands = acceptsCommands;
        }

        /// <summary>
        ///     The default: no booth, nothing is pushed, and the match runs normally.
        /// </summary>
        public static TelemetryPolicy Off { get; } =
            new TelemetryPolicy(null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

        /// <summary>The booth's ingest endpoint, in full, or null to push nowhere.</summary>
        public string? Url { get; }

        /// <summary>
        ///     The console channel endpoint, derived from <see cref="Url"/> by replacing the last segment:
        ///     <c>http://booth:8080/ingest</c> becomes <c>http://booth:8080/console</c>.
        /// </summary>
        public string? ConsoleUrl =>
            Url == null ? null : new Uri(new Uri(Url), "console").ToString();

        /// <summary>
        ///     How often the match is pushed; one second is the overlays' rate.
        /// </summary>
        /// <remarks>
        ///     The pump is polled once per server tick (a quarter second), so shorter intervals take
        ///     effect in quarter-second steps. A push is about 12.6 KB for 25 players, per connected
        ///     dashboard client, so bandwidth rather than the booth limits the rate.
        /// </remarks>
        public TimeSpan Interval { get; }

        /// <summary>How long one push is given before it is treated as refused.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        ///     Whether this server runs the verbs the booth sends back. Defaults to on, since configuring
        ///     a booth URL is the opt-in.
        /// </summary>
        /// <remarks>
        ///     Set false to push to a booth without being driven by it; the booth's reconciler then
        ///     cannot override in-game verbs such as <c>lms unghost</c>.
        /// </remarks>
        public bool AcceptsCommands { get; }

        /// <summary>Whether anything is pushed at all.</summary>
        public bool Enabled => Url != null;

        /// <summary>Validated at config load so a bad URL fails loudly instead of silently never pushing.</summary>
        private static bool IsPushable(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}
