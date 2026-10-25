using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Puts the match on the wire and the operator's commands into it; the rules live in
    ///     <see cref="MatchHost"/>.
    /// </summary>
    /// <remarks>
    ///     Locked because ticks, player <c>/lms</c> packets and the server console arrive on three threads.
    /// </remarks>
    internal sealed class TournamentHost : ILmsTournamentHost
    {
        /// <summary>
        ///     How often an unchanged zone is re-sent. A backstop: clients derive the wall from the last
        ///     line and the shared clock.
        /// </summary>
        private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

        /// <summary>Minimum gap between "your swing did nothing" messages to one attacker.</summary>
        private static readonly TimeSpan RefusalInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     How many unjudged swings are held before new ones are dropped. Sized for 25 players at the
        ///     game's cap of two blade swings a second.
        /// </summary>
        private const int MaxQueuedStrikes = 256;

        /// <summary>
        ///     The client's name for a prawn suit: <c>TechType.Exosuit.AsString()</c>.
        /// </summary>
        /// <remarks>
        ///     The vehicle is self-reported, so this only stops swings from someone not in a prawn suit.
        ///     Reports lag by up to a second, so a swing right after boarding can be refused.
        /// </remarks>
        private const string PrawnVehicle = "Exosuit";

        /// <summary>Prefix of every line the mod puts on the report envelope.</summary>
        private const string LmsPrefix = "lms-";

        /// <summary>
        ///     The client's names for the blades: <c>TechType.Knife</c> and <c>TechType.HeatBlade</c>, as
        ///     <c>AsString()</c> writes them.
        /// </summary>
        /// <remarks>
        ///     Self-reported like <see cref="PrawnVehicle"/>, with the same one-second report lag.
        /// </remarks>
        private const string KnifeItem = "Knife";

        /// <inheritdoc cref="KnifeItem"/>
        private const string HeatBladeItem = "HeatBlade";

        private readonly object gate = new object();

        /// <summary>
        ///     The match. <see cref="MatchHost.Rules"/> is the live config, so no separate copy is kept here.
        /// </summary>
        private readonly MatchHost host;
        private readonly Action<string> broadcast;
        private readonly Action<ushort, string> sendTo;

        /// <summary>Shared so a push with no pods allocates nothing.</summary>
        private static readonly IReadOnlyList<PodTelemetry> NoPods = new PodTelemetry[0];
        private readonly TelemetryPump pump;
        private readonly TelemetryPush? push;

        /// <summary>
        ///     The player table: what the server sees, plus what each client reports about itself.
        /// </summary>
        /// <remarks>Has its own lock rather than <see cref="gate"/>: reports arrive on the packet thread.</remarks>
        private readonly PlayerRoster roster = new PlayerRoster();

        /// <summary>This run's audit log file, or null when one could not be opened.</summary>
        private readonly AuditFile? file;

        /// <summary>How many audit records have been written to <see cref="file"/>.</summary>
        private int written;

        /// <summary>A settings-tab command waiting for the next tick, or null.</summary>
        private AdminCommand? requested;

        /// <summary>The last button that ran, so a double-click runs once.</summary>
        private readonly ButtonPresses presses = new ButtonPresses();

        /// <summary>
        ///     Commands the casting booth has queued for this server.
        /// </summary>
        /// <remarks>
        ///     A queue, unlike <see cref="requested"/>: each booth command has its own id and is owed an
        ///     answer. Guarded by <see cref="gate"/>, since push replies land on HTTP continuation threads.
        /// </remarks>
        private readonly BoothCommandQueue commands;

        /// <summary>
        ///     Paces reports to the operator's console: who is connected and what this build can do.
        /// </summary>
        /// <remarks>Separate from the telemetry pump so a slow console report cannot delay the match feed.</remarks>
        private readonly ConsolePump consolePump;

        /// <summary>
        ///     The tournament config the booth has sent, and which revision this server is running.
        /// </summary>
        private readonly BoothRules rules;

        /// <summary>
        ///     A booth config that parsed but cannot be adopted until the lobby. Parsed once, on arrival.
        /// </summary>
        private TournamentConfig? waiting;

        private long waitingRevision;
        private string? waitingYaml;
        private string? waitingBy;

        /// <summary>
        ///     The booth-supplied config text being run, sent to joining clients. Null until the booth
        ///     replaces the file; kept verbatim so clients parse exactly what the operator wrote.
        /// </summary>
        private string? adoptedYaml;

        /// <summary>
        ///     The config in force, verbatim: the file at startup, then whatever was last adopted. A
        ///     revision written to disk but not yet adopted is not included.
        /// </summary>
        private string runningYaml;

        /// <summary>Where this server's own config file is, so a config from the booth can be kept.</summary>
        private readonly string directory;

        /// <summary>
        ///     Swings clients have reported, waiting to be judged on the next tick.
        /// </summary>
        /// <remarks>
        ///     Capped at <see cref="MaxQueuedStrikes"/> because a modified client can flood it and draining
        ///     it holds the match lock. Overflow is logged, not dropped silently.
        /// </remarks>
        private readonly List<PendingStrike> strikes = new List<PendingStrike>();

        /// <summary>
        ///     A monotonic clock, started with this host, that each swing's arrival is stamped against.
        /// </summary>
        /// <remarks>
        ///     Swings drained in one tick share one <c>worldNow</c>, so cooldowns shorter than a tick have to
        ///     be measured on arrival time. A wall clock is not used because it can go backwards.
        /// </remarks>
        private readonly Stopwatch arrival = Stopwatch.StartNew();

        /// <summary>
        ///     When each attacker last landed a hit, on <see cref="arrival"/>'s clock, for
        ///     <c>minInterval</c>.
        /// </summary>
        /// <remarks>
        ///     Landed hits only, so refusals cannot start a cooldown. One cooldown per attacker across all
        ///     weapons.
        /// </remarks>
        private readonly Dictionary<ushort, TimeSpan> lastLanded = new Dictionary<ushort, TimeSpan>();

        /// <summary>When each attacker may next be told why a swing did nothing.</summary>
        private readonly Dictionary<ushort, TimeSpan> nextRefusal = new Dictionary<ushort, TimeSpan>();

        /// <summary>
        ///     Players already told, in the log and on screen, that their reports cannot be read.
        /// </summary>
        /// <remarks>Never cleared: a build mismatch lasts the whole session.</remarks>
        private readonly HashSet<ushort> unreadable = new HashSet<ushort>();

        /// <summary>
        ///     The role each client was last told, so the role flag is sent only on change.
        /// </summary>
        /// <remarks>
        ///     Never cleared, and Nitrox reuses player ids (a returning name gets its old id back, a freed id
        ///     goes to the next connection), so the greeting bypasses it via <see cref="TellRoleOutright"/>.
        /// </remarks>
        private readonly Dictionary<ushort, bool> toldTheirRole = new Dictionary<ushort, bool>();

        /// <summary>
        ///     The last list of players not to draw, so it is broadcast only on change.
        /// </summary>
        /// <remarks>The whole list each time, so a client that missed one is corrected by the next.</remarks>
        private HiddenBroadcast toldWhoNotToDraw = new HiddenBroadcast(null);

        /// <summary>
        ///     Wires the match to the server. <paramref name="running"/> is the loaded config text, which the
        ///     console prefills.
        /// </summary>
        internal TournamentHost(TournamentConfig config, string directory, Action<string> broadcast,
            Action<ushort, string> sendTo, string? running = null)
        {
            runningYaml = running ?? string.Empty;
            host = new MatchHost(config, Heartbeat);

            // Server-side handlers for Core's `lms podspawn` and `lms podclear`.
            host.SeatsTheGroup = new PodSpawnRun(config.Spawn, config.Zone.Opening, Contestants).Run;

            // One PodClearRun behind both the derelict sweep and `podclear all`, on separate hooks.
            PodClearRun clearing = new PodClearRun(config.Spawn, config.Zone.Opening, Contestants);
            host.ClearsTheDerelicts = clearing.Run;
            host.ClearsEveryPod = clearing.RunAll;

            // `lms round` reuses the sweep and layout runs above rather than a second path to either.
            RoundRun round = new RoundRun(config.Start, ModeSweep, MakeSurvival, clearing.Run,
                new PodSpawnRun(config.Spawn, config.Zone.Opening, Contestants).Run,
                Instruct, () => host.Group, () => host.Generation);

            host.Connected = () => Readings(LmsRoster.Read());
            host.PreparesTheRound = round.Run;
            host.PointsTheCameras = PointCameras;
            host.HoldsTheCameras = HoldCameras;

            // Before this run's file exists, so only earlier runs are resumed.
            IReadOnlyList<IReadOnlyList<AuditRecord>> earlier = AuditFile.Earlier(directory);
            host.Resume(earlier);
            Log.Info(earlier.Count == 0
                ? $"{LmsServerMod.LogPrefix} no earlier audit log here, so the tournament starts at group 1."
                : $"{LmsServerMod.LogPrefix} picked the tournament up from {earlier.Count} earlier run(s) in " +
                  $"lms-audit: waiting at group {host.Group}, with their finished games on the table. To start " +
                  "again from group 1, move those files out before the server starts.");

            file = AuditFile.Open(directory, DateTimeOffset.UtcNow);
            this.broadcast = broadcast;
            this.sendTo = sendTo;
            pump = new TelemetryPump(config.Telemetry);
            push = TelemetryPush.Open(config.Telemetry);
            commands = new BoothCommandQueue(config.Telemetry.AcceptsCommands);
            rules = new BoothRules(config.Telemetry.AcceptsCommands);
            consolePump = new ConsolePump(config.Telemetry.Timeout);
            this.directory = directory;

            // Logged either way, so an operator can tell a booth that cannot drive the match from one whose
            // commands are being dropped.
            if (push != null)
            {
                Log.Info(config.Telemetry.AcceptsCommands
                    ? $"{LmsServerMod.LogPrefix} the booth may run tournament commands here. " +
                      "Set telemetry.acceptsCommands: false to refuse them."
                    : $"{LmsServerMod.LogPrefix} telemetry.acceptsCommands is false, so the booth is " +
                      "fed the match and cannot run anything here.");
            }

            // Never unsubscribed: the host lives as long as the server process, and only one is built.
            LmsPlayerReportRelay.Received += Reported;
        }

        /// <summary>
        ///     Handles one line a client sent on the report envelope: a self-report, an event batch, an
        ///     admin request or a swing.
        /// </summary>
        /// <remarks>
        ///     An event batch that dropped events is logged, since it leaves a gap in the audit log.
        /// </remarks>
        private void Reported(ushort player, bool isAdmin, string line)
        {
            if (PlayerReport.TryDecode(line, out PlayerReport report))
            {
                roster.Reported(player, report);
                return;
            }

            if (PlayerEventBatch.TryDecode(line, out PlayerEventBatch batch))
            {
                if (batch.Lost > 0)
                {
                    Log.Warn($"{LmsServerMod.LogPrefix} player {player} dropped {batch.Lost} event(s) " +
                             "before reporting them; the audit log has a gap.");
                }

                roster.Reported(player, batch);
                return;
            }

            if (AdminRequest.IsRequest(line))
            {
                Requested(player, isAdmin, line);
                return;
            }

            if (StrikeReport.IsStrike(line))
            {
                Struck(player, line);
                return;
            }

            // Unrecognised mod line: warn once per player, since a client on a different build may send one
            // every second.
            if (line.StartsWith(LmsPrefix, StringComparison.Ordinal) && unreadable.Add(player))
            {
                Log.Warn($"{LmsServerMod.LogPrefix} player {player} is reporting something this " +
                         $"build has no reader for: '{line}'. Are the two builds the same? This is " +
                         "said once for this player.");
            }
        }

        /// <summary>
        ///     Takes a settings-tab button press.
        /// </summary>
        /// <remarks>
        ///     Refused unless Nitrox says the sender is an admin: authority comes from the connection, never
        ///     the message. Queued for the next tick because this runs on the packet thread, without a world
        ///     clock. One pending slot, because two quick presses are one intention.
        /// </remarks>
        private void Requested(ushort player, bool isAdmin, string line)
        {
            if (!AdminRequest.TryDecode(line, out AdminCommand command))
            {
                Log.Warn($"{LmsServerMod.LogPrefix} player {player} asked for something this build " +
                         $"does not recognise: '{line}'. Are the two builds the same?");
                return;
            }

            if (!isAdmin)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} refused '{AdminCommands.Word(command)}' from player " +
                         $"{player}: not an admin. /login first, every session.");
                return;
            }

            lock (gate)
            {
                requested = command;
            }
        }

        /// <summary>
        ///     Takes one swing a client says it landed and queues it for the next tick.
        /// </summary>
        /// <remarks>
        ///     The attacker is <paramref name="player"/>, never anything in the message, and the damage comes
        ///     from the config. Judged on the tick because the rule reads the roster the tick writes.
        /// </remarks>
        private void Struck(ushort player, string line)
        {
            // Stamped before the lock, so this is arrival time rather than processing time.
            TimeSpan arrivedAt = arrival.Elapsed;

            if (!StrikeReport.TryDecode(line, out ushort victim, out StrikeWeapon weapon))
            {
                Log.Warn($"{LmsServerMod.LogPrefix} player {player} reported a swing this build " +
                         $"cannot read: '{line}'. Are the two builds the same?");

                // Tell the player once. Not routed through Refuse: there is no verdict or victim, and a build
                // mismatch lasts the session.
                if (unreadable.Add(player))
                {
                    sendTo(player, new StrikeRefusedBroadcast(StrikeRefusal.TheBuildsDiffer).Encode());
                }

                return;
            }

            // Both positions now: Nitrox updates a position on every movement packet, while the tick's
            // reading can be up to a second stale, which `reach` would otherwise have to cover.
            // Runs on the packet thread, as Greet's roster reads do.
            Snapshot(player, victim, out WorldPoint? attackerWas, out WorldPoint? victimWas);

            lock (gate)
            {
                if (strikes.Count >= MaxQueuedStrikes)
                {
                    // Logged: a swing that never reached the rule would otherwise look like a refused one.
                    Log.Warn($"{LmsServerMod.LogPrefix} dropped a swing from player {player}: " +
                             $"{MaxQueuedStrikes} are already waiting to be judged, which is more " +
                             "than a tick's worth of real ones.");
                    return;
                }

                strikes.Add(new PendingStrike(player, victim, weapon, arrivedAt,
                    attackerWas, victimWas));
            }
        }

        /// <summary>Runs the pending settings-tab command, if any.</summary>
        /// <remarks>
        ///     Inside <see cref="Tick"/>'s lock, through the same path as <c>/lms</c>. Exceptions are caught
        ///     because a throw out of <c>Tick</c> makes the server unload the tournament until a restart.
        /// </remarks>
        private void RunRequestedVerb(TimeSpan worldNow)
        {
            if (requested == null)
            {
                return;
            }

            AdminCommand command = requested.Value;

            // Cleared before running, so a verb that throws is not retried every tick.
            requested = null;

            // Double-click guard: two presses rarely land inside one quarter-second tick.
            if (!presses.Admit(command, worldNow))
            {
                Log.Info($"{LmsServerMod.LogPrefix} lms {AdminCommands.Word(command)} (from the settings " +
                         "tab) pressed again within a second: run once.");
                return;
            }

            try
            {
                string message = host.Command(AdminCommands.Word(command), worldNow);

                Log.Info($"{LmsServerMod.LogPrefix} lms {AdminCommands.Word(command)} (from the settings " +
                         $"tab): {message}");
            }
            catch (Exception ex)
            {
                // The full exception, since a line here is a defect. The settings tab expects no reply.
                Log.Error($"{LmsServerMod.LogPrefix} lms {AdminCommands.Word(command)} (from the " +
                          $"settings tab) threw and the match is unchanged: {ex}");
            }
        }

        /// <summary>
        ///     Runs one queued booth command, if there is one.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="MatchHost.Command"/> like the console, so the booth gets the same checks and
        ///     refusals. One per tick, so two verbs never share one world reading.
        /// </remarks>
        private void RunBoothCommand(TimeSpan worldNow)
        {
            if (!commands.TryTake(out BoothCommand command))
            {
                return;
            }

            string message;
            try
            {
                message = host.Command(command.Verb, worldNow);
            }
            catch (Exception ex)
            {
                // Still answered: `TryTake` does not advance the watermark, so without an answer the booth
                // would resend this verb until its TTL expires.
                Log.Error($"{LmsServerMod.LogPrefix} lms {command.Verb} (from the booth, command " +
                          $"#{command.Id}) threw: {ex}");

                message = "that verb threw on this server and did not finish; the server log says why.";
            }

            // The answer goes back on the console channel, keeping HTTP off the match tick.
            int dropped = commands.Answered(command.Id, command.Verb, message);

            Log.Info($"{LmsServerMod.LogPrefix} lms {command.Verb} (from the booth, command " +
                     $"#{command.Id}): {message}");

            if (dropped > 0)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} dropped {dropped} older answer(s) on the way back " +
                         "to the booth; it has not been taking the console channel.");
            }
        }

        /// <summary>
        ///     Takes a config the booth sent and, once in the lobby, adopts it and tells the clients.
        /// </summary>
        /// <remarks>
        ///     Order matters: validate with the same loader as the file; write it to disk before adopting, so
        ///     a crash cannot revert the rules; adopt only in the lobby (<see cref="MatchHost.Adopt"/>, retried
        ///     every tick until then); then update the world and the clients. Every step is guarded because
        ///     a throw out of the tick unloads the tournament.
        /// </remarks>
        private void AdoptBoothRules(TimeSpan worldNow)
        {
            if (rules.TryTake(out long revision, out string yaml, out string by))
            {
                try
                {
                    Arrived(revision, yaml, by);
                }
                catch (Exception ex)
                {
                    // Settle it: `TryTake` moved the revision to `Holding` and only `Settled` clears it, so
                    // otherwise it would stay "waiting" and could never be offered again.
                    rules.Settled(revision, adopted: false,
                        "that config could not be read on this server and was not adopted; the " +
                        "server log says why.");

                    Log.Error($"{LmsServerMod.LogPrefix} the booth's rules revision {revision} threw " +
                              $"while it was being read; nothing was changed: {ex}");
                }
            }

            if (waiting == null)
            {
                return;
            }

            long offered = waitingRevision;
            bool takenByTheMatch = false;

            try
            {
                if (!host.Adopt(waiting, waitingRevision, waitingBy, out string said))
                {
                    // Said once, when it arrives, rather than on every tick it cannot be taken.
                    return;
                }

                takenByTheMatch = true;

                TournamentConfig adopted = waiting;
                long adoptedRevision = waitingRevision;

                // The operator's text, not re-serialised: this is what the clients will parse.
                adoptedYaml = waitingYaml;

                // Moves at adoption, not at the write: until then the old rules are in force.
                runningYaml = adoptedYaml ?? runningYaml;

                waiting = null;
                waitingYaml = null;

                // Player start positions come from the config.
                LmsServerMod.PlacePlayers(adopted);

                // Rebuild everything that captured the old `spawn:` and `zone.opening`.
                Rebuild(adopted);

                TellTheRules(adoptedRevision, adoptedYaml ?? string.Empty);

                // The zone line now, so clients do not draw the old opening sphere until the next heartbeat.
                Send(worldNow);
                Record();
                Report(worldNow);

                rules.Settled(adoptedRevision, adopted: true, said);

                Log.Info($"{LmsServerMod.LogPrefix} adopted tournament rules revision {adoptedRevision}: {said}");
            }
            catch (Exception ex)
            {
                // Dropped, since `waiting` would otherwise be retried, and throw, on every tick.
                waiting = null;
                waitingYaml = null;

                // Once `host.Adopt` has returned true the match is on the new rules; only the placement,
                // rebuild or broadcast is incomplete.
                rules.Settled(offered, adopted: takenByTheMatch,
                    takenByTheMatch
                        ? "the match took that revision but this server could not finish applying " +
                          "it, so the world and the clients may still be on the previous rules; the " +
                          "server log says why."
                        : "that config threw while it was being adopted and the server is still on " +
                          "the previous rules; the server log says why.");

                Log.Error($"{LmsServerMod.LogPrefix} adopting the booth's rules revision {offered} " +
                          $"threw: {ex}");
            }
        }

        /// <summary>
        ///     Validates a config that has just arrived, writes it to disk and holds it for adoption.
        /// </summary>
        private void Arrived(long revision, string yaml, string? by)
        {
            TournamentConfigLoadResult result = TournamentConfigLoader.Load(yaml);

            if (!result.IsValid)
            {
                // Every problem with its field, as loading the file would report it.
                List<string> problems = new List<string>(result.Problems.Count);
                foreach (TournamentConfigProblem problem in result.Problems)
                {
                    problems.Add(problem.ToString());
                }

                rules.Settled(revision, adopted: false,
                    $"that config has {problems.Count} problem(s) and was not written or adopted.",
                    problems);

                Log.Warn($"{LmsServerMod.LogPrefix} the booth sent a config with " +
                         $"{problems.Count} problem(s); nothing was changed.");

                return;
            }

            if (!TournamentConfigFile.Save(directory, yaml, out string reason))
            {
                // Not adopted: rules that are not on disk would change on restart.
                rules.Settled(revision, adopted: false,
                    $"that config is valid but could not be written to disk, so it was not adopted: {reason}");

                return;
            }

            waiting = result.Config;
            waitingRevision = revision;
            waitingYaml = yaml;
            waitingBy = by;

            // Tell the operator now that it is written and waiting.
            if (!host.WouldAdopt)
            {
                rules.Note(revision,
                    "written to this server's own config file. A match is running, so it will be " +
                    "taken up at the next lobby.");
            }
        }

        /// <summary>
        ///     Rebuilds the pod layout, derelict sweep and round, which each captured <c>spawn:</c> and
        ///     <c>zone.opening</c>.
        /// </summary>
        private void Rebuild(TournamentConfig adopted)
        {
            host.SeatsTheGroup = new PodSpawnRun(adopted.Spawn, adopted.Zone.Opening, Contestants).Run;

            PodClearRun clearing = new PodClearRun(adopted.Spawn, adopted.Zone.Opening, Contestants);
            host.ClearsTheDerelicts = clearing.Run;
            host.ClearsEveryPod = clearing.RunAll;

            RoundRun round = new RoundRun(adopted.Start, ModeSweep, MakeSurvival, clearing.Run,
                new PodSpawnRun(adopted.Spawn, adopted.Zone.Opening, Contestants).Run,
                Instruct, () => host.Group, () => host.Generation);

            host.PreparesTheRound = round.Run;
        }

        /// <summary>
        ///     Broadcasts the whole adopted config, comment-stripped; each client takes the half it needs.
        /// </summary>
        /// <remarks>
        ///     A document that cannot be encoded (only possible with a tab, which the loader also rejects) is
        ///     logged rather than dropped silently.
        /// </remarks>
        private void TellTheRules(long revision, string yaml)
        {
            try
            {
                broadcast(new RulesBroadcast(revision, yaml).Encode());
            }
            catch (ArgumentException ex)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} the clients were not sent revision {revision}: " +
                         $"{ex.Message}");
            }
        }

        public void Tick(TimeSpan worldNow)
        {
            lock (gate)
            {
                // Settings-tab button first, so the rest of this tick reports its effect.
                RunRequestedVerb(worldNow);

                // Then one booth command, so e.g. a `ghost` takes effect before the wall closes this tick.
                RunBoothCommand(worldNow);

                // After the command, so a verb runs under the rules in force when it was issued. Adoption
                // only happens in the lobby, where nothing is time-critical.
                AdoptBoothRules(worldNow);

                // Read every tick, booth or not: arrivals, departures and eliminations go to the audit log.
                // The raw reading is kept because the strike rule needs player ids; rows are keyed by name.
                IReadOnlyList<LmsPlayerReading> reading = LmsRoster.Read();

                // Everybody connected, then contestants only. Everything below takes the contestants, so
                // ghosts are out of the roster, audit, samples, alive count and booth table in one place.
                IReadOnlyList<PlayerReading> everybody = Readings(reading);
                IReadOnlyList<PlayerReading> contestants = host.Ghosts.Contestants(everybody);

                // Before the roster update: a rejoining eliminated player is in Nitrox's roster well before
                // they are greeted.
                TellTheRosterWhoIsOut(contestants, worldNow);

                RosterUpdate update = roster.Update(contestants,
                    host.Zone(worldNow).Sphere, host.Generation);

                // Before advancing, so a new ghost stops taking damage before the wall closes this tick.
                TellRoles(everybody);
                TellWhoNotToDraw();

                // Before advancing, so a player eliminated this tick is already out when the wall closes,
                // which decides whether a winner is named.
                host.Record(update.Changes);

                // After the changes, so swings at a player already out are refused; before the sample, so it
                // records positions after any hit.
                RunQueuedStrikes(worldNow, reading, update.Rows);

                // After the changes and before the send. Samples only while live and when the cadence is due.
                host.Sample(worldNow, update.Rows);

                Send(worldNow, update.Rows);
                Record();
                Report(worldNow);
                Feed(worldNow, update.Rows);

                // Caster feed off the same rows as the booth, so nameplates and dashboard agree. After Send,
                // so it reflects the wall this tick closed on.
                TellTheCasters(update.Rows);

                // Last, and off the raw reading: the console is the one channel that lists ghosts.
                Console(reading);
            }
        }

        /// <summary>Appends audit records written since the last call to this run's file.</summary>
        /// <remarks>Inside the lock, so the file's order matches the log's.</remarks>
        private void Record()
        {
            if (file == null)
            {
                return;
            }

            IReadOnlyList<AuditRecord> records = host.Audit.Records;

            while (written < records.Count)
            {
                file.Append(records[written]);
                written++;
            }
        }

        public string Command(string? verb, TimeSpan worldNow)
        {
            lock (gate)
            {
                string message = host.Command(verb, worldNow);

                // Sent now rather than on the next tick, so `ghost`/`unghost` reach clients at once.
                TellRoles(Readings(LmsRoster.Read()));
                TellWhoNotToDraw();

                Send(worldNow);
                Record();
                Report(worldNow);

                Log.Info($"{LmsServerMod.LogPrefix} lms {verb ?? "status"}: {message}");

                return message;
            }
        }

        public void Greet(ushort playerId, TimeSpan worldNow)
        {
            lock (gate)
            {
                sendTo(playerId, host.Line(worldNow));

                // Outright rather than on change, because Nitrox reuses player ids.
                TellRoleOutright(playerId, Named(playerId));

                // Before the rules line, which can throw: Nitrox catches around the whole greeting, so
                // anything after a throw is skipped.
                Reconcile(playerId, worldNow);

                // Their game mode: the round's sweep ran at prep, before a rejoin.
                CorrectTheirMode(playerId);

                // The hidden list outright, so a client that joined between changes does not draw ghosts.
                sendTo(playerId, new HiddenBroadcast(host.Ghosts.Named).Encode());

                // The current camera follow, to ghosts only: a contestant must not learn who is watched.
                TellTheirFollow(playerId, Named(playerId));

                // The adopted rules, once the booth has replaced the file; until then the client's own copy
                // is the same file.
                if (rules.Revision > 0 && adoptedYaml != null)
                {
                    sendTo(playerId, new RulesBroadcast(rules.Revision, adoptedYaml).Encode());
                }

                Settle(playerId, worldNow);
            }
        }

        /// <summary>Tells each client whose role has changed what it is now.</summary>
        /// <remarks>
        ///     The server decides: a client that could set its own ghost flag could make itself immune to the
        ///     wall and to drowning.
        /// </remarks>
        private void TellRoles(IReadOnlyList<PlayerReading> everybody)
        {
            foreach (PlayerReading reading in everybody)
            {
                TellRole(reading.Id, reading.Name);
            }
        }

        private void TellRole(ushort playerId, string? name) => TellRole(playerId, name, false);

        /// <summary>
        ///     Tells a joining client its role regardless of <see cref="toldTheirRole"/>.
        /// </summary>
        /// <remarks>
        ///     Nitrox gives a returning player their old id, so the memo would say they already know, while a
        ///     fresh client defaults to contestant. Pruning the memo on departure would not help: ghosts are
        ///     filtered out before the roster, so a departing ghost produces no <c>PlayerLeft</c>.
        /// </remarks>
        private void TellRoleOutright(ushort playerId, string? name) => TellRole(playerId, name, true);

        /// <summary>
        ///     Tells one joining ghost which contestant the cameras are following, if any.
        /// </summary>
        /// <remarks>
        ///     Ghosts only, checked here. Nothing is sent when nobody is followed: that is the client's default.
        /// </remarks>
        private void TellTheirFollow(ushort playerId, string? name)
        {
            if (name == null || !host.Followed.Following || !host.Ghosts.IsGhost(name))
            {
                return;
            }

            sendTo(playerId, host.Followed.Encode());
        }

        /// <summary>
        ///     Sends a client its role; unless <paramref name="outright"/>, only when it has changed.
        /// </summary>
        private void TellRole(ushort playerId, string? name, bool outright)
        {
            if (name == null)
            {
                return;
            }

            bool isGhost = host.Ghosts.IsGhost(name);

            if (!outright && toldTheirRole.TryGetValue(playerId, out bool told) && told == isGhost)
            {
                return;
            }

            toldTheirRole[playerId] = isGhost;
            sendTo(playerId, new GhostBroadcast(isGhost).Encode());

            // Logged because a ghost is otherwise invisible: not on the dashboard, in the alive count or on
            // contestants' screens.
            Log.Info($"{LmsServerMod.LogPrefix} {name} is " +
                     (isGhost ? "a ghost: the match cannot see them" : "in the match"));
        }

        /// <summary>
        ///     Every escape pod in the world, for the caster's map; empty when the seam is missing.
        /// </summary>
        /// <remarks>Read fresh on every push: <c>lms round</c> and <c>podclear</c> move pods between pushes.</remarks>
        private static IReadOnlyList<PodTelemetry> Pods()
        {
            IReadOnlyList<LmsPod>? pods = LmsPods.Seating?.Pods;

            if (pods == null || pods.Count == 0)
            {
                return NoPods;
            }

            List<PodTelemetry> told = new List<PodTelemetry>(pods.Count);

            foreach (LmsPod pod in pods)
            {
                told.Add(new PodTelemetry(pod.X, pod.Y, pod.Z, pod.Players.Count));
            }

            return told;
        }

        /// <summary>
        ///     Points every connected ghost's camera at one contestant (<c>lms look</c>); returns how many.
        /// </summary>
        /// <remarks>Counts connected ghosts, not named ones: a name stays ghosted across a disconnect.</remarks>
        private int PointCameras(LookBroadcast look)
        {
            int told = ToEveryGhost(look.Encode());

            if (told > 0)
            {
                Log.Info($"{LmsServerMod.LogPrefix} {told} camera(s) sent to {look.Name} at " +
                         $"{look.Where}.");
            }

            return told;
        }

        /// <summary>
        ///     Tells every connected ghost's camera whom to follow, or to let go (<c>lms follow</c>,
        ///     <c>lms unfollow</c>).
        /// </summary>
        /// <remarks>
        ///     Counted like <see cref="PointCameras"/>. A let-go is always sent, correcting a client that
        ///     missed the last message.
        /// </remarks>
        private int HoldCameras(FollowBroadcast follow)
        {
            int told = ToEveryGhost(follow.Encode());

            if (told > 0)
            {
                Log.Info($"{LmsServerMod.LogPrefix} {told} camera(s) told to {follow}.");
            }

            return told;
        }

        /// <summary>
        ///     Sends one line to every connected ghost and returns how many that was.
        /// </summary>
        /// <remarks>The only place the caster channel is addressed, so contestants never receive it.</remarks>
        private int ToEveryGhost(string line)
        {
            int told = 0;

            foreach (PlayerReading reading in Readings(LmsRoster.Read()))
            {
                if (!host.Ghosts.IsGhost(reading.Name))
                {
                    continue;
                }

                sendTo(reading.Id, line);
                told++;
            }

            return told;
        }

        /// <summary>
        ///     Sends the casters every contestant's position and status, every tick.
        /// </summary>
        /// <remarks>
        ///     The tick is <c>LmsTournament.TickIntervalSeconds</c>, which <see cref="CasterTrack.MaxLeadSeconds"/>
        ///     must equal; clients smooth the steps through <see cref="CasterTrack"/>. Uses the booth's merged
        ///     rows, so plates match the dashboard. Skipped when there are no ghosts.
        /// </remarks>
        private void TellTheCasters(IReadOnlyList<PlayerTelemetry> rows)
        {
            if (host.Ghosts.Count == 0)
            {
                return;
            }

            List<CasterSighting> seen = new List<CasterSighting>(rows.Count);

            foreach (PlayerTelemetry row in rows)
            {
                seen.Add(new CasterSighting(row.Name, row.Position, row.Health, row.Oxygen, row.Alive));
            }

            ToEveryGhost(new CasterFeed(seen).Encode());
        }

        /// <summary>
        ///     Broadcasts which players (the ghosts) not to draw, when the list changes.
        /// </summary>
        /// <remarks>Goes to every client by necessity: remote players are rendered on each observer's machine.</remarks>
        private void TellWhoNotToDraw()
        {
            HiddenBroadcast hidden = new HiddenBroadcast(host.Ghosts.Named);

            if (hidden.Equals(toldWhoNotToDraw))
            {
                return;
            }

            toldWhoNotToDraw = hidden;
            broadcast(hidden.Encode());

            Log.Info($"{LmsServerMod.LogPrefix} told every client {hidden}");
        }

        /// <summary>
        ///     Sends each named contestant what their round starts with.
        /// </summary>
        /// <remarks>
        ///     Point to point because the instruction empties an inventory, which ghosts and players outside
        ///     the group must not receive. Names come from the server's roster, never from a message.
        /// </remarks>
        private void Instruct(RoundBroadcast instruction, IReadOnlyList<string> contestants)
        {
            HashSet<string> named = new HashSet<string>(contestants, StringComparer.Ordinal);
            string line = instruction.Encode();

            foreach (LmsPlayerReading reading in LmsRoster.Read())
            {
                if (named.Contains(reading.Name))
                {
                    sendTo(reading.Id, line);
                }
            }
        }

        /// <summary>Connected non-ghost players, in Nitrox's shape, for the pod runs.</summary>
        private IReadOnlyList<LmsPlayerReading> Contestants()
        {
            IReadOnlyList<LmsPlayerReading> present = LmsRoster.Read();

            if (host.Ghosts.Count == 0)
            {
                return present;
            }

            List<LmsPlayerReading> contestants = new List<LmsPlayerReading>(present.Count);
            foreach (LmsPlayerReading reading in present)
            {
                if (!host.Ghosts.IsGhost(reading.Name))
                {
                    contestants.Add(reading);
                }
            }

            return contestants;
        }

        /// <summary>
        ///     Keeps players the log has already eliminated out of the roster's alive count.
        /// </summary>
        /// <remarks>
        ///     Every tick, not just in <see cref="Greet"/>: a joining player is in Nitrox's roster from the
        ///     start of <c>SendInitialSync</c> but greeted only after loading, seconds later, and reads as alive
        ///     in between. Counting them could name the wrong winner.
        /// </remarks>
        private void TellTheRosterWhoIsOut(IReadOnlyList<PlayerReading> contestants, TimeSpan worldNow)
        {
            foreach (PlayerReading contestant in contestants)
            {
                if (host.IsOut(contestant.Name, worldNow))
                {
                    roster.Out(contestant.Name, host.Generation);
                }
            }
        }

        /// <summary>
        ///     Tells a returning player the match has already eliminated them.
        /// </summary>
        /// <remarks>
        ///     <c>PlayerRoster.Departed</c> forgets a departed player because Nitrox reuses ids, so without this
        ///     a reconnect would undo an elimination. Ghosts are skipped: they were never in the match.
        /// </remarks>
        private void Reconcile(ushort playerId, TimeSpan worldNow)
        {
            string? name = Named(playerId);
            if (name == null || host.Ghosts.IsGhost(name))
            {
                return;
            }

            if (!host.IsOut(name, worldNow))
            {
                return;
            }

            sendTo(playerId, new EliminatedBroadcast(host.Group, host.Generation).Encode());

            // Logged so a returning player who finds themselves spectating can be told why.
            Log.Info($"{LmsServerMod.LogPrefix} {name} rejoined group {host.Group}.{host.Generation}, " +
                     "which is already finished with them: told to hand their body to the camera.");
        }

        /// <summary>
        ///     Puts a joining non-ghost player back into survival if the save gave them another mode.
        /// </summary>
        /// <remarks>
        ///     Nitrox persists game mode per player and <c>lms round</c> only sweeps players present at prep;
        ///     a creative body takes no damage. Done here because the initial sync sets the mode last.
        ///     Eliminated players are corrected too, in case of a <c>reset</c>.
        /// </remarks>
        private void CorrectTheirMode(ushort playerId)
        {
            string? name = Named(playerId);
            if (name == null || host.Ghosts.IsGhost(name))
            {
                return;
            }

            IReadOnlyList<PlayerMode>? modes = Modes();
            if (modes == null)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} {name} joined and this server cannot read a game " +
                         "mode, so they were not checked. A creative body takes no damage from the " +
                         "wall, and `lms round` will refuse until the seam is there.");
                return;
            }

            GameModeSweep sweep = GameModeGuard.Plan(new[] { name }, modes);

            if (sweep.Refusal != null)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} {name} joined and {sweep.Refusal}.");
                return;
            }

            if (sweep.Wrong.Count == 0)
            {
                return;
            }

            PlayerMode player = sweep.Wrong[0];

            if (!MakeSurvival(player))
            {
                Log.Warn($"{LmsServerMod.LogPrefix} {name} joined in {player.Mode} and could not be " +
                         "put back into survival. THEY MAY BE IMMORTAL FOR THIS MATCH.");
                return;
            }

            // Audit record first, then the log line.
            host.Corrected(name, player.Mode);
            Record();

            Log.Info($"{LmsServerMod.LogPrefix} {name} joined in {player.Mode} and is now in " +
                     "survival. A creative body takes no damage from the wall.");
        }

        /// <summary>
        ///     Charges a returning player the zone damage owed for their time away.
        /// </summary>
        /// <remarks>
        ///     The name comes from Nitrox, not the client, so nobody can be handed another player's bill.
        ///     Honours <c>flags.enforceDamage</c> like <c>ZoneEnforcer</c>, and is logged either way.
        /// </remarks>
        private void Settle(ushort playerId, TimeSpan worldNow)
        {
            string? name = Named(playerId);
            if (name == null)
            {
                return;
            }

            // Ghosts are not in the roster the bill is folded from, so they owe nothing.
            if (host.Ghosts.IsGhost(name))
            {
                return;
            }

            ZoneDebt? owed = host.Arrears(name, worldNow);
            if (owed == null)
            {
                return;
            }

            ZoneDebt debt = owed.Value;
            string away = Readable.Line(
                $"{name} was away {debt.Absence.TotalSeconds:0}s of match time outside the wall");

            // The match's live rules, so a config pushed between groups applies.
            if (!host.Rules.Flags.EnforceDamage)
            {
                Log.Info(Readable.Line(
                    $"{LmsServerMod.LogPrefix} {away}; the zone would have taken ",
                    $"{debt.Damage:0.#} over {debt.Ticks} tick(s) (damage disabled)."));
                return;
            }

            host.Charged(name, debt);
            Record();

            // After the record, so the evidence is written before the client is asked to act on it.
            sendTo(playerId, new ArrearsBroadcast(host.Group, host.Generation, debt).Encode());

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} {away}; charged {debt.Damage:0.#} over ",
                $"{debt.Ticks} tick(s), leaving {debt.HealthLeft:0.#}",
                $"{(debt.IsFatal ? " -- the wall would have got them" : string.Empty)}."));
        }

        /// <summary>
        ///     Judges the queued swings and answers each one.
        /// </summary>
        /// <remarks>
        ///     Inside <see cref="Tick"/>'s lock; <see cref="StrikeRule.Judge"/> decides. A hit goes to the
        ///     victim, whose client applies it, and a refusal to the attacker, whose client animates the swing
        ///     regardless. The queue is emptied first and each swing judged in its own <c>try</c>, so one bad
        ///     swing cannot stop the tick or repeat every tick.
        /// </remarks>
        private void RunQueuedStrikes(TimeSpan worldNow, IReadOnlyList<LmsPlayerReading> reading,
            IReadOnlyList<PlayerTelemetry> rows)
        {
            if (strikes.Count == 0)
            {
                return;
            }

            List<PendingStrike> waiting = new List<PendingStrike>(strikes);
            strikes.Clear();

            foreach (PendingStrike strike in waiting)
            {
                try
                {
                    Judge(worldNow, reading, rows, strike);
                }
                catch (Exception ex)
                {
                    Log.Error($"{LmsServerMod.LogPrefix} could not judge a swing from player " +
                              $"{strike.Attacker} at player {strike.Victim}: {ex}");
                }
            }
        }

        private void Judge(TimeSpan worldNow, IReadOnlyList<LmsPlayerReading> reading,
            IReadOnlyList<PlayerTelemetry> rows, PendingStrike strike)
        {
            LmsPlayerReading? attacker = Read(reading, strike.Attacker);
            if (attacker == null)
            {
                // Disconnected since the packet: nothing to do.
                return;
            }

            // No real client can send this (victim ids exist only on remote bodies), so it is forged.
            if (strike.Victim == strike.Attacker)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} dropped a swing player {strike.Attacker} " +
                         "reported against themselves; no prawn can do that.");
                return;
            }

            LmsPlayerReading? victim = Read(reading, strike.Victim);
            if (victim == null)
            {
                // The victim has left the server; answered as "already out".
                Refuse(worldNow, strike.Attacker, attacker.Value.Name, StrikeRefusal.TheyAreAlreadyOut);
                return;
            }

            PlayerTelemetry? attackerRow = Row(rows, attacker.Value.Name);
            PlayerTelemetry? victimRow = Row(rows, victim.Value.Name);

            WeaponFamily family = StrikeWeapons.Family(strike.Weapon);

            StrikeVerdict verdict = StrikeRule.Judge(
                // The match's live rules, picked by weapon family. The client applies this damage verbatim,
                // and `Adopt` only runs in the lobby, so the rules cannot change under a swing.
                Policy(family),
                strike.Weapon,
                // Positions at arrival, falling back to this tick's reading.
                strike.AttackerWas ?? new WorldPoint(attacker.Value.X, attacker.Value.Y, attacker.Value.Z),
                strike.VictimWas ?? new WorldPoint(victim.Value.X, victim.Value.Y, victim.Value.Z),
                attackerIsHeld: host.IsHeld(attacker.Value.Name),

                // True for every claw, which the prawn policy ignores. For a blade it is a forgery check:
                // boarding a vehicle holsters what you were holding.
                attackerIsInAVehicle: attackerRow != null && attackerRow.Vehicle != null,
                attackerIsArmed: Armed(strike.Weapon, attackerRow),
                matchIsLive: host.IsLive,

                // The roster's alive flag, not Nitrox's: the match is done with a player when their client
                // says so, and a corpse is still connected.
                victimIsInTheMatch: victimRow != null && victimRow.Alive,
                sinceLastStrike: Since(strike.Attacker, strike.ArrivedAt),

                // Passed explicitly: ghosts are filtered out of `rows`, so `Armed` alone would refuse them.
                attackerIsAGhost: host.Ghosts.IsGhost(attacker.Value.Name));

            if (!verdict.Landed)
            {
                Refuse(worldNow, strike.Attacker, attacker.Value.Name, verdict.Refusal);
                return;
            }

            lastLanded[strike.Attacker] = strike.ArrivedAt;

            // Audit record before the damage message, as in Settle.
            host.Hurt(victim.Value.Name, attacker.Value.Name, strike.Weapon, verdict.Damage,
                victim.Value.Health, new WorldPoint(victim.Value.X, victim.Value.Y, victim.Value.Z),
                victimRow?.Biome);
            Record();

            sendTo(strike.Victim, new HurtBroadcast(verdict.Damage, attacker.Value.Name).Encode());

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} {attacker.Value.Name} hit {victim.Value.Name} with the ",
                $"{StrikeWeapons.Word(strike.Weapon)} for {verdict.Damage:0.#}, leaving them on ",
                $"{Left(victim.Value.Health, verdict.Damage):0.#}."));
        }

        /// <summary>
        ///     Tells one attacker why their swing did nothing, at most once per <see cref="RefusalInterval"/>.
        /// </summary>
        /// <remarks>
        ///     Only the message is rate-limited, not the rule. <paramref name="name"/> comes from the roster
        ///     reading the swing was judged against.
        /// </remarks>
        private void Refuse(TimeSpan worldNow, ushort attacker, string name, StrikeRefusal why)
        {
            TimeSpan next;
            if (nextRefusal.TryGetValue(attacker, out next) && worldNow < next)
            {
                return;
            }

            nextRefusal[attacker] = worldNow + RefusalInterval;

            sendTo(attacker, new StrikeRefusedBroadcast(why).Encode());

            // Info, not Warn: a swing out of range is a misjudged fight, not a fault.
            Log.Info($"{LmsServerMod.LogPrefix} refused a swing from {name}: " +
                     $"{StrikeRefusals.Sentence(why)}.");
        }

        /// <summary>
        ///     Where two players are now, for a swing judged on the next tick: both or neither.
        /// </summary>
        private static void Snapshot(ushort attacker, ushort victim, out WorldPoint? attackerWas,
            out WorldPoint? victimWas)
        {
            attackerWas = null;
            victimWas = null;

            IReadOnlyList<LmsPlayerReading> now = LmsRoster.Read();
            LmsPlayerReading? one = Read(now, attacker);
            LmsPlayerReading? other = Read(now, victim);

            if (one == null || other == null)
            {
                return;
            }

            attackerWas = new WorldPoint(one.Value.X, one.Value.Y, one.Value.Z);
            victimWas = new WorldPoint(other.Value.X, other.Value.Y, other.Value.Z);
        }

        /// <summary>The live config section that judges this weapon family.</summary>
        /// <remarks>Throws for an unknown family rather than falling back, which would record wrong damage.</remarks>
        private StrikePolicy Policy(WeaponFamily family)
        {
            switch (family)
            {
                case WeaponFamily.PrawnArm:
                    return host.Rules.Prawn;

                case WeaponFamily.Blade:
                    return host.Rules.Blade;

                default:
                    throw new ArgumentOutOfRangeException(nameof(family), family,
                        "No config section judges that family.");
            }
        }

        /// <summary>
        ///     Whether the roster shows this attacker holding, or sitting in, what they say they swung.
        /// </summary>
        /// <remarks>A missing row means not armed: no report is nothing to trust.</remarks>
        private static bool Armed(StrikeWeapon weapon, PlayerTelemetry? row)
        {
            if (row == null)
            {
                return false;
            }

            switch (weapon)
            {
                case StrikeWeapon.Claw:
                case StrikeWeapon.Drill:
                    return string.Equals(row.Vehicle, PrawnVehicle, StringComparison.Ordinal);

                case StrikeWeapon.Knife:
                    return string.Equals(row.HeldItem, KnifeItem, StringComparison.Ordinal);

                case StrikeWeapon.HeatBlade:
                    return string.Equals(row.HeldItem, HeatBladeItem, StringComparison.Ordinal);

                default:
                    throw new ArgumentOutOfRangeException(nameof(weapon), weapon,
                        "Nothing on a roster row says whether somebody is holding that.");
            }
        }

        /// <summary>
        ///     Time from this attacker's last landed hit to this swing's arrival, or
        ///     <see cref="TimeSpan.MaxValue"/> for their first.
        /// </summary>
        private TimeSpan Since(ushort attacker, TimeSpan arrivedAt)
        {
            TimeSpan last;

            return lastLanded.TryGetValue(attacker, out last) ? arrivedAt - last : TimeSpan.MaxValue;
        }

        /// <summary>What a hit leaves somebody on, floored at nothing.</summary>
        private static double Left(double health, double damage) =>
            health - damage > 0 ? health - damage : 0;

        private static LmsPlayerReading? Read(IReadOnlyList<LmsPlayerReading> reading, ushort id)
        {
            foreach (LmsPlayerReading row in reading)
            {
                if (row.Id == id)
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>The merged row for a name; the rows and the audit log are both keyed by name.</summary>
        private static PlayerTelemetry? Row(IReadOnlyList<PlayerTelemetry> rows, string name)
        {
            foreach (PlayerTelemetry row in rows)
            {
                if (string.Equals(row.Name, name, StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>The name the log identifies a player by, from Nitrox's own roster.</summary>
        private static string? Named(ushort playerId)
        {
            foreach (LmsPlayerReading reading in LmsRoster.Read())
            {
                if (reading.Id == playerId)
                {
                    return reading.Name;
                }
            }

            return null;
        }

        /// <summary>
        ///     Pushes the whole match to the casting booth, if one is configured and a push is due.
        /// </summary>
        /// <remarks>
        ///     The server owns the wall, so it works out who is outside it rather than asking clients.
        ///     <see cref="TelemetryPush.Send"/> does not block; <see cref="Answered"/> runs on the reply's thread.
        /// </remarks>
        private void Feed(TimeSpan worldNow, IReadOnlyList<PlayerTelemetry> rows)
        {
            // Real time: the world clock stops while nobody is connected, and the booth should still show
            // the empty lobby. Match time travels in the payload.
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (push == null || !pump.IsDue(now))
            {
                return;
            }

            ZoneState zone = host.Zone(worldNow);

            TelemetrySnapshot snapshot = pump.Take(now,
                host.Telemetry(worldNow, PlayerRoster.Alive(rows)), ZoneTelemetry.Of(zone), rows,
                host.Audit, host.Standings(worldNow), Pods());

            push.Send(TelemetryJson.Write(snapshot), commands.Cursor, rules.Cursor, Answered);
        }

        /// <summary>
        ///     Who among the contestants is not in survival.
        /// </summary>
        /// <remarks>
        ///     Contestants only, since survival would drown a caster. A missing seam reports everyone as
        ///     unread, not as fine.
        /// </remarks>
        private GameModeSweep ModeSweep()
        {
            List<string> contestants = new List<string>();

            foreach (LmsPlayerReading player in Contestants())
            {
                contestants.Add(player.Name);
            }

            IReadOnlyList<PlayerMode>? modes = Modes();

            if (modes == null)
            {
                Log.Warn($"{LmsServerMod.LogPrefix} round: this server cannot read a game mode, so " +
                         "nobody was checked. A creative body takes no damage from the wall.");
            }

            return GameModeGuard.Plan(contestants, modes!);
        }

        /// <summary>
        ///     Every player's game mode as the server has it, disconnected ones included, or null without
        ///     the seam.
        /// </summary>
        private static IReadOnlyList<PlayerMode>? Modes()
        {
            ILmsGameMode? seam = LmsGameModes.Modes;
            if (seam == null)
            {
                return null;
            }

            List<PlayerMode> modes = new List<PlayerMode>();
            foreach (LmsPlayerMode player in seam.Modes)
            {
                modes.Add(new PlayerMode(player.Id, player.Name, player.Mode));
            }

            return modes;
        }

        /// <summary>Puts one contestant back into survival, or says the server no longer has them.</summary>
        private bool MakeSurvival(PlayerMode player)
        {
            ILmsGameMode? seam = LmsGameModes.Modes;
            return seam != null && seam.MakeSurvival(player.Id);
        }

        /// <summary>
        ///     Reports to the operator's console who is connected, this build's verbs, and command results.
        /// </summary>
        /// <remarks>
        ///     A separate channel from the telemetry <c>players[]</c>, so overlays never learn who is a ghost.
        ///     Built every tick, sent on change or on a slow heartbeat.
        /// </remarks>
        private void Console(IReadOnlyList<LmsPlayerReading> reading)
        {
            if (push == null)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            List<ConsolePresence> connected = new List<ConsolePresence>(reading.Count);
            foreach (LmsPlayerReading player in reading)
            {
                connected.Add(new ConsolePresence(player.Name, !host.Ghosts.IsGhost(player.Name)));
            }

            // Copies: the pump holds the report until it is accepted, and later answers must not be
            // counted as sent.
            List<BoothCommandAnswer> owed = new List<BoothCommandAnswer>(commands.Answers);
            List<BoothRulesOutcome> aboutTheRules = new List<BoothRulesOutcome>(rules.Outcomes);

            // The queue's own flag, not the adopted config: `commands` and `rules` keep the startup value.
            ConsoleReport report = new ConsoleReport(now, connected, ConsoleJson.Vocabulary(),
                owed, commands.Ran, commands.Accepts,
                rules.Revision, aboutTheRules, runningYaml);

            if (!consolePump.IsDue(now, report))
            {
                return;
            }

            consolePump.Take(now, report);
            push.SendConsole(ConsoleJson.Write(report), ConsoleAnswered);
        }

        /// <summary>Handles the console push result.</summary>
        /// <remarks>Takes the lock: runs on the reply's continuation thread.</remarks>
        private void ConsoleAnswered(bool accepted)
        {
            lock (gate)
            {
                if (!accepted)
                {
                    // Nothing is forgotten. A report that did not land did not happen, so every
                    // answer in it is still owed to the console.
                    consolePump.Refused();
                    return;
                }

                // Each queue trims by its own count, so a verb's answer cannot trim a config outcome.
                ConsoleCarried carried = consolePump.Accepted();
                commands.Forget(carried.Answers);
                rules.Forget(carried.Rules);
            }
        }

        /// <summary>
        ///     Handles the telemetry push result and takes any commands or rules the booth sent back.
        /// </summary>
        /// <remarks>Takes the lock: runs on the reply's continuation thread, never the tick's.</remarks>
        private void Answered(bool accepted, IReadOnlyList<BoothCommand> queued, BoothRulesOffer? sent)
        {
            lock (gate)
            {
                if (accepted)
                {
                    pump.Accepted();
                }
                else
                {
                    pump.Refused();
                }

                // Taken even on a refusal: the booth has still said what it wants run.
                int taken = commands.Offer(queued);

                if (taken > 0)
                {
                    Log.Info($"{LmsServerMod.LogPrefix} the booth asked for {taken} command(s); " +
                             "they run one a tick.");
                }

                if (sent != null && rules.Offer(sent.Value.Revision, sent.Value.Yaml, sent.Value.By))
                {
                    Log.Info($"{LmsServerMod.LogPrefix} the booth sent tournament rules, revision " +
                             $"{sent.Value.Revision}. They are read on the next tick.");
                }
            }
        }

        /// <summary>
        ///     Contestants still alive, for the moment the match ends; null for a caller without rows (a
        ///     command, which cannot end a match).
        /// </summary>
        private static IReadOnlyList<string>? StillIn(IReadOnlyList<PlayerTelemetry>? rows)
        {
            if (rows == null)
            {
                return null;
            }

            List<string> alive = new List<string>();

            foreach (PlayerTelemetry row in rows)
            {
                if (row.Alive)
                {
                    alive.Add(row.Name);
                }
            }

            return alive;
        }

        /// <summary>Nitrox's reading of each player, in the shape Core merges reports into.</summary>
        private static IReadOnlyList<PlayerReading> Readings(IReadOnlyList<LmsPlayerReading> roster)
        {
            List<PlayerReading> readings = new List<PlayerReading>(roster.Count);

            foreach (LmsPlayerReading reading in roster)
            {
                readings.Add(new PlayerReading(reading.Id, reading.Name,
                    new WorldPoint(reading.X, reading.Y, reading.Z), reading.Alive,
                    reading.Health, reading.Oxygen, reading.Food, reading.Water));
            }

            return readings;
        }

        private void Send(TimeSpan worldNow, IReadOnlyList<PlayerTelemetry>? rows = null)
        {
            // The same alive count the booth overlay gets, from the same rows, so they cannot disagree on
            // camera. Null without rows.
            string? line = host.Advance(worldNow, StillIn(rows),
                rows == null ? (int?)null : PlayerRoster.Alive(rows));

            if (line != null)
            {
                broadcast(line);
            }
        }

        /// <summary>Logs a status line when <see cref="MatchHost.Announce"/> says something changed.</summary>
        private void Report(TimeSpan worldNow)
        {
            string? line = host.Announce(worldNow);

            if (line != null)
            {
                Log.Info($"{LmsServerMod.LogPrefix} {line}");
            }
        }

        /// <summary>
        ///     One queued swing: who swung, whom they named, which weapon, and when and where it arrived.
        /// </summary>
        /// <remarks>No damage on it: that comes from the config when the swing is judged.</remarks>
        private readonly struct PendingStrike
        {
            internal PendingStrike(ushort attacker, ushort victim, StrikeWeapon weapon,
                TimeSpan arrivedAt, WorldPoint? attackerWas, WorldPoint? victimWas)
            {
                Attacker = attacker;
                Victim = victim;
                Weapon = weapon;
                ArrivedAt = arrivedAt;
                AttackerWas = attackerWas;
                VictimWas = victimWas;
            }

            /// <summary>The sender's connection, never anything in the message.</summary>
            internal ushort Attacker { get; }

            internal ushort Victim { get; }

            internal StrikeWeapon Weapon { get; }

            /// <summary>When this swing reached the server, on the host's monotonic clock.</summary>
            internal TimeSpan ArrivedAt { get; }

            /// <summary>
            ///     Where the player was when the swing arrived, or null. Set for both players or neither: a
            ///     distance between two different moments is meaningless.
            /// </summary>
            internal WorldPoint? AttackerWas { get; }

            /// <inheritdoc cref="AttackerWas"/>
            internal WorldPoint? VictimWas { get; }
        }
    }
}
