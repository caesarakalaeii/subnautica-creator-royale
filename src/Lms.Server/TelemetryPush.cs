using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Lms.Core.Diagnostics;
using Lms.Core.Telemetry;
using Nitrox.Model.Logger;

namespace Lms.Server
{
    /// <summary>
    ///     Posts match snapshots and console reports to the casting booth over HTTP; scheduling and
    ///     retry decisions belong to <see cref="TelemetryPump"/>.
    /// </summary>
    /// <remarks>
    ///     Never blocks the match tick: posts run asynchronously and answer through a callback. The
    ///     bearer token comes from the environment, not the config file, because the config is shared
    ///     with players.
    /// </remarks>
    internal sealed class TelemetryPush
    {
        /// <summary>Environment variable holding the bearer token; the booth reads the same one.</summary>
        internal const string TokenVariable = "LMS_INGEST_TOKEN";

        /// <summary>
        ///     Header carrying the highest booth command this server has run. A header keeps the frozen
        ///     payload contract unchanged; no header means the server accepts no commands.
        /// </summary>
        private const string WatermarkHeader = "X-Lms-Commands-Ran";

        /// <summary>
        ///     Header carrying the config revision this server runs; absent means it takes no config
        ///     from the booth.
        /// </summary>
        private const string RulesHeader = "X-Lms-Rules-Revision";

        /// <summary>Empty command list, the usual reply.</summary>
        private static readonly IReadOnlyList<BoothCommand> NoCommands = new BoothCommand[0];

        /// <summary>Maximum characters of a refusal body to log.</summary>
        private const int ReasonLength = 200;

        private readonly HttpClient client;
        private readonly string url;

        /// <summary>The booth's console endpoint, beside its ingest endpoint.</summary>
        private readonly string consoleUrl;

        /// <summary>Last push failure reason, so a down booth is logged once.</summary>
        private string? failing;

        /// <summary>Last console failure reason, logged once.</summary>
        private string? consoleFailing;

        private TelemetryPush(HttpClient client, string url, string consoleUrl)
        {
            this.client = client;
            this.url = url;
            this.consoleUrl = consoleUrl;
        }

        /// <summary>
        ///     A push, or null when this server is not feeding a booth; logs which reason applies.
        /// </summary>
        internal static TelemetryPush? Open(TelemetryPolicy policy)
        {
            if (!policy.Enabled)
            {
                Log.Info($"{LmsServerMod.LogPrefix} no casting booth configured; the match is not pushed anywhere. " +
                         "Set telemetry.url in the tournament config to feed one.");
                return null;
            }

            string? token = Environment.GetEnvironmentVariable(TokenVariable);
            if (string.IsNullOrEmpty(token))
            {
                Log.Warn($"{LmsServerMod.LogPrefix} the config names a casting booth at {policy.Url} but " +
                         $"{TokenVariable} is not set, so every push would be refused. Nothing is pushed.");
                return null;
            }

            // The pump relies on this exact timeout to abandon a push a full cadence later. The client
            // lives for the whole process, so it is never disposed.
            HttpClient client = new HttpClient { Timeout = policy.Timeout };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} pushing the match to {policy.Url} every ",
                $"{policy.Interval.TotalSeconds:0.#}s."));

            return new TelemetryPush(client, policy.Url!, policy.ConsoleUrl!);
        }

        /// <summary>
        ///     Sends one payload and hands the verdict back when it arrives.
        /// </summary>
        /// <param name="payload">The snapshot, already serialized.</param>
        /// <param name="watermark">
        ///     The highest command this server has run, or null when it accepts no commands.
        /// </param>
        /// <param name="revision">The config revision this server runs, or null when it takes none.</param>
        /// <param name="answered">
        ///     Called exactly once, on the reply's thread and never before this method returns, with
        ///     whether the booth accepted it plus any commands and config it sent back.
        /// </param>
        internal void Send(string payload, long? watermark, long? revision,
            Action<bool, IReadOnlyList<BoothCommand>, BoothRulesOffer?> answered)
        {
            // Fire and forget: the pump never offers a second payload while this one is in flight.
            _ = Post(payload, watermark, revision, answered);
        }

        private async Task Post(string payload, long? watermark, long? revision,
            Action<bool, IReadOnlyList<BoothCommand>, BoothRulesOffer?> answered)
        {
            bool accepted = false;
            string reason;
            IReadOnlyList<BoothCommand> commands = NoCommands;
            BoothRulesOffer? rules = null;

            try
            {
                using (StringContent content = new StringContent(payload, Encoding.UTF8, "application/json"))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
                {
                    if (watermark != null)
                    {
                        request.Headers.TryAddWithoutValidation(WatermarkHeader,
                            watermark.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    if (revision != null)
                    {
                        request.Headers.TryAddWithoutValidation(RulesHeader,
                            revision.Value.ToString(CultureInfo.InvariantCulture));
                    }

                    using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        accepted = response.IsSuccessStatusCode;

                        if (accepted)
                        {
                            commands = Commands(body);
                            rules = Rules(body);
                            reason = Took(body, commands.Count, rules != null);
                        }
                        else
                        {
                            reason = Refused(response, body);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Socket failures and timeouts: a down booth is routine, so log a reason, not a trace.
                reason = ex.Message;
            }

            try
            {
                answered(accepted, commands, rules);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{LmsServerMod.LogPrefix} the telemetry pump threw while being told about a push");
            }

            if (!accepted)
            {
                Report(reason);
            }
            else
            {
                Recovered(reason);
            }
        }

        /// <summary>
        ///     Sends one console report to the booth's <c>/console</c> endpoint and hands the verdict back.
        /// </summary>
        /// <remarks>
        ///     Driven by its own pump so a slow console report never delays match telemetry.
        /// </remarks>
        internal void SendConsole(string payload, Action<bool> answered)
        {
            _ = PostConsole(payload, answered);
        }

        private async Task PostConsole(string payload, Action<bool> answered)
        {
            bool accepted = false;
            string reason;

            try
            {
                using (StringContent content = new StringContent(payload, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await client.PostAsync(consoleUrl, content).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    accepted = response.IsSuccessStatusCode;
                    reason = accepted ? "accepted" : Refused(response, body);
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }

            try
            {
                answered(accepted);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{LmsServerMod.LogPrefix} the console pump threw while being told about a report");
            }

            if (!accepted)
            {
                if (reason != consoleFailing)
                {
                    consoleFailing = reason;

                    // Warn once per reason: a failing console channel does not affect the match.
                    Log.Warn($"{LmsServerMod.LogPrefix} the casting booth is not taking the console " +
                             $"channel at {consoleUrl}: {reason}");
                }
            }
            else if (consoleFailing != null)
            {
                consoleFailing = null;
                Log.Info($"{LmsServerMod.LogPrefix} the casting booth is taking the console channel again.");
            }
        }

        /// <summary>
        ///     Log text for an accepted push. <c>newEvents</c> stuck at zero means this end is resending
        ///     what the booth already has.
        /// </summary>
        private static string Took(string body, int commands, bool rules)
        {
            string carried = commands == 0 ? string.Empty : $", {commands} command(s) back";
            carried += rules ? ", and a config" : string.Empty;

            try
            {
                using (JsonDocument reply = JsonDocument.Parse(body))
                {
                    int events = reply.RootElement.GetProperty("newEvents").GetInt32();
                    long last = reply.RootElement.GetProperty("lastSequence").GetInt64();

                    return $"{events} new event(s), booth at sequence {last}{carried}";
                }
            }
            catch (Exception)
            {
                // A 2xx with an unexpected body still counts as accepted.
                return "accepted" + carried;
            }
        }

        /// <summary>
        ///     Reads a tournament config the booth sent back, if any. Validation and adoption happen
        ///     elsewhere; an unparseable reply drops the config, never the push.
        /// </summary>
        private static BoothRulesOffer? Rules(string body)
        {
            try
            {
                using (JsonDocument reply = JsonDocument.Parse(body))
                {
                    if (!reply.RootElement.TryGetProperty("rules", out JsonElement sent)
                        || sent.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    if (!sent.TryGetProperty("revision", out JsonElement revision)
                        || !sent.TryGetProperty("yaml", out JsonElement yaml)
                        || !revision.TryGetInt64(out long number)
                        || number <= 0
                        || yaml.ValueKind != JsonValueKind.String)
                    {
                        return null;
                    }

                    string? document = yaml.GetString();
                    if (string.IsNullOrWhiteSpace(document))
                    {
                        return null;
                    }

                    string? by = sent.TryGetProperty("by", out JsonElement wrote)
                                 && wrote.ValueKind == JsonValueKind.String
                        ? wrote.GetString()
                        : null;

                    return new BoothRulesOffer(number, document!, by);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Reads the commands the booth queued for this server; <see cref="BoothCommandQueue"/>
        ///     decides what to run. Malformed rows are skipped; an unparseable reply drops the commands,
        ///     never the push.
        /// </summary>
        private static IReadOnlyList<BoothCommand> Commands(string body)
        {
            try
            {
                using (JsonDocument reply = JsonDocument.Parse(body))
                {
                    if (!reply.RootElement.TryGetProperty("commands", out JsonElement queued)
                        || queued.ValueKind != JsonValueKind.Array)
                    {
                        return NoCommands;
                    }

                    List<BoothCommand> commands = new List<BoothCommand>();

                    foreach (JsonElement element in queued.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object
                            || !element.TryGetProperty("id", out JsonElement id)
                            || !element.TryGetProperty("verb", out JsonElement verb)
                            || !id.TryGetInt64(out long number)
                            || number <= 0)
                        {
                            continue;
                        }

                        string? word = verb.ValueKind == JsonValueKind.String ? verb.GetString() : null;
                        if (string.IsNullOrWhiteSpace(word))
                        {
                            continue;
                        }

                        commands.Add(new BoothCommand(number, word!));
                    }

                    return commands;
                }
            }
            catch (Exception)
            {
                return NoCommands;
            }
        }

        private static string Refused(HttpResponseMessage response, string body)
        {
            string trimmed = body.Length > ReasonLength ? body.Substring(0, ReasonLength) + "…" : body;

            return $"HTTP {(int)response.StatusCode}: {trimmed.Trim()}";
        }

        /// <summary>Logs a push failure once per distinct reason, not once per push.</summary>
        private void Report(string reason)
        {
            if (reason == failing)
            {
                return;
            }

            failing = reason;
            Log.Warn($"{LmsServerMod.LogPrefix} the casting booth is not taking the match: {reason}");
        }

        private void Recovered(string reason)
        {
            if (failing != null)
            {
                failing = null;
                Log.Info($"{LmsServerMod.LogPrefix} the casting booth is taking the match again: {reason}");
            }
        }
    }
}
