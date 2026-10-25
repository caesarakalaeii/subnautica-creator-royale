using System;
using System.Collections.Generic;
using System.Linq;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Spawning;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Results;
using Lms.Core.Telemetry;
using Lms.Core.Zones;

namespace Lms.Core.Match
{
    /// <summary>
    ///     The server side of one match: drives the director, decides when clients need telling, and
    ///     writes the audit log.
    /// </summary>
    /// <remarks>
    ///     The Nitrox server adapter is plumbing around this: a timer calls <see cref="Advance"/>, the
    ///     <c>lms</c> command calls <see cref="Command"/>. Lines are sent on change plus a slow
    ///     heartbeat. Core does no file I/O; the host appends <see cref="AuditFormat.Write"/>'s lines.
    /// </remarks>
    public sealed class MatchHost
    {
        private readonly MatchDirector director;
        private AuditPolicy audit;
        private TournamentConfig config;

        /// <summary>
        ///     Who has reported their world loaded, by name since Nitrox ids do not survive a reconnect.
        ///     Cleared when a reset or next replaces the match.
        /// </summary>
        private readonly HashSet<string> ready = new HashSet<string>(StringComparer.Ordinal);
        private readonly TimeSpan heartbeat;
        private readonly Func<DateTimeOffset> clock;

        /// <summary>
        ///     The audit log folded back into match state, caught up on demand, so derived answers such
        ///     as <see cref="Arrears"/> come from the same records as the evidence.
        /// </summary>
        private readonly AuditReplay replay = new AuditReplay();

        private int folded;

        /// <summary>
        ///     The last standings table folded, and the cursor it was folded at, so an unchanged log is not
        ///     refolded.
        /// </summary>
        private StandingsTelemetry? table;
        private int tableFoldedAt = -1;

        /// <summary>Every match the earlier runs of this server played, oldest first.</summary>
        private readonly List<MatchAudit> earlier = new List<MatchAudit>();

        /// <summary>
        ///     Match time the last position sample was taken at, and which match it was. Keyed on the
        ///     anchor so a reset re-arms it.
        /// </summary>
        private (int Group, int Generation) sampled;
        private TimeSpan sampledAt;

        private string? lastSent;
        private TimeSpan lastSentAt;

        /// <summary>
        ///     Which match the end records have already been written for; keyed on the anchor so a reset re-
        ///     arms it.
        /// </summary>
        private (int Group, int Generation) ended;
        private (int Group, int Generation) closed;

        /// <summary>
        ///     The lobby a restarted server waits in when the game it was on had already finished, so
        ///     <c>round</c>/<c>start</c> cannot replay it and void its result.
        /// </summary>
        private (int Group, int Generation) finishedBeforeRestart;

        /// <summary>
        ///     World time the wall finished closing on the match <see cref="closed"/> names. Players can
        ///     survive a while in a zero-radius wall, so the close starts a window rather than ending the match.
        /// </summary>
        private TimeSpan closedAt;

        /// <summary>The shape of the match as last announced, so an unchanged one stays quiet.</summary>
        private string? announced;

        /// <param name="config">
        ///     The tournament rules. The server uses the <c>match</c> and <c>zone</c> sections.
        /// </param>
        /// <param name="heartbeat">How often an unchanged line is sent again.</param>
        public MatchHost(TournamentConfig config, TimeSpan heartbeat)
            : this(config, heartbeat, () => DateTimeOffset.UtcNow)
        {
        }

        /// <param name="config">The tournament rules.</param>
        /// <param name="heartbeat">How often an unchanged line is sent again.</param>
        /// <param name="clock">Wall clock for stamping records, injectable for tests.</param>
        public MatchHost(TournamentConfig config, TimeSpan heartbeat, Func<DateTimeOffset> clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (heartbeat <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(heartbeat), heartbeat,
                    "A heartbeat that never beats is not a backstop; use a long interval instead.");
            }

            this.heartbeat = heartbeat;
            audit = config.Audit;
            this.config = config;
            director = new MatchDirector(config.Clock, config.SqueezeFor);
            Ghosts = new GhostRoster(config.Ghosts);
        }

        /// <summary>
        ///     The tournament's evidence: everything the match has done, in order. Several readers each
        ///     keep their own cursor over <see cref="AuditLog.Records"/>.
        /// </summary>
        public AuditLog Audit { get; } = new AuditLog();

        /// <summary>
        ///     Picks the tournament up where earlier runs of this server left it: their finished games go
        ///     on the table, and the match waits in the lobby at the game the last of them was on.
        /// </summary>
        /// <param name="runs">Each earlier run's records, oldest run first, as its log file holds them.</param>
        /// <remarks>
        ///     The game comes back as a new attempt, as <c>reset</c> makes one: the earlier attempt
        ///     stays on the table unless this one is played.
        /// </remarks>
        public void Resume(IEnumerable<IReadOnlyList<AuditRecord>> runs)
        {
            if (runs == null)
            {
                throw new ArgumentNullException(nameof(runs));
            }

            if (Audit.Records.Count > 0)
            {
                throw new InvalidOperationException("A tournament is picked up before this run writes anything.");
            }

            AuditRecord? last = null;
            foreach (IReadOnlyList<AuditRecord> run in runs)
            {
                if (run.Count == 0)
                {
                    continue;
                }

                earlier.AddRange(AuditReplay.Of(run).Matches);
                last = run[run.Count - 1];
            }

            if (last.HasValue)
            {
                int group = last.Value.Group;
                director.ResumeAt(group, last.Value.Generation + 1);

                // The attempt that counts at this group, by the table's own rule (Standings).
                MatchAudit? counted = null;
                foreach (MatchAudit match in earlier)
                {
                    if (match.Group == group && (counted == null || match.Supersedes))
                    {
                        counted = match;
                    }
                }

                if (counted != null && counted.HasEnded)
                {
                    finishedBeforeRestart = (director.Group, director.Generation);
                }
            }
        }

        /// <summary>
        ///     Who is present but not in the match. Everything downstream (roster, pod layout, alive
        ///     count, standings) filters through <see cref="GhostRoster.Contestants"/>.
        /// </summary>
        public GhostRoster Ghosts { get; }

        /// <summary>
        ///     Who has announced their world loaded and precached, for the <c>lms round</c> readiness check.
        /// </summary>
        public IReadOnlyList<string> Ready => new List<string>(ready);

        /// <summary>Records a client's precache-done announcement. Idempotent.</summary>
        public void AnnounceReady(string player)
        {
            ready.Add(player);
        }

        /// <summary>
        ///     Everybody connected, for <c>lms round</c> to count. Supplied by the adapter; null means no
        ///     world, and the verb answers rather than throws.
        /// </summary>
        public Func<IReadOnlyList<PlayerReading>>? Connected { get; set; }

        /// <summary>
        ///     Sets a round up in the world: the sweep, the layout, the kit, the grant and the hold.
        ///     Supplied by the server adapter.
        /// </summary>
        public Func<RoundPlan, RoundOutcome>? PreparesTheRound { get; set; }

        /// <summary>
        ///     When the hold ends and the match starts itself, or null when nothing is armed. The clock
        ///     starts when the hold ends, not when it goes up.
        /// </summary>
        public TimeSpan? StartsAt { get; private set; }

        /// <summary>Which group is playing, counting from one.</summary>
        public int Group => director.Group;

        /// <summary>
        ///     Puts the group on their pods (<c>lms podspawn</c>). Supplied by the server adapter; null
        ///     answers the verb rather than throwing. <see cref="PodSeating"/> holds the rule.
        /// </summary>
        public Func<PodCommandOutcome>? SeatsTheGroup { get; set; }

        /// <summary>
        ///     Removes the derelict pods (<c>lms podclear</c>). <see cref="PodClearing"/> decides which
        ///     pods are derelicts.
        /// </summary>
        public Func<PodCommandOutcome>? ClearsTheDerelicts { get; set; }

        /// <summary>
        ///     Removes every escape pod (<c>lms podclear all</c>). A separate hook so the routine verb can
        ///     never reach this one; <see cref="PodClearing.PlanAll"/> holds the rule.
        /// </summary>
        public Func<PodCommandOutcome>? ClearsEveryPod { get; set; }

        /// <summary>
        ///     Moves every ghost's camera (<c>lms look &lt;player&gt;</c>). Returns how many cameras were
        ///     told; zero just means no caster is connected.
        /// </summary>
        public Func<LookBroadcast, int>? PointsTheCameras { get; set; }

        /// <summary>
        ///     Tells every ghost's camera who to stay on (<c>lms follow</c>, <c>lms unfollow</c>). Returns
        ///     how many cameras were told.
        /// </summary>
        public Func<FollowBroadcast, int>? HoldsTheCameras { get; set; }

        /// <summary>
        ///     Tells one ghost's camera who to stay on (<c>lms followOne</c>), given the addressed
        ///     broadcast. Returns whether that camera was reached.
        /// </summary>
        public Func<FollowBroadcast, bool>? CamerasFollow { get; set; }

        /// <summary>
        ///     Who the cameras are staying on, sent to a caster who joins later. A refused follow does
        ///     not change it.
        /// </summary>
        public FollowBroadcast Followed { get; private set; } = FollowBroadcast.LetGo;

        /// <summary>
        ///     Which attempt at this group's match is running; client-announced events are checked
        ///     against it.
        /// </summary>
        public int Generation => director.Generation;

        /// <summary>
        ///     Whether a match is actually running, as the clock was last advanced. Read by the prawn rule
        ///     so nobody is hurt in a lobby or during a pause.
        /// </summary>
        public bool IsLive => director.IsLive;

        /// <summary>
        ///     Whether <see cref="Adopt"/> would take new rules right now, asked without side effects.
        /// </summary>
        public bool WouldAdopt => !StartsAt.HasValue && director.State == MatchClockState.NotStarted;

        /// <summary>
        ///     Whether the audit log says a ring is holding this player right now, folded from the
        ///     cage and release the player's own client reports.
        /// </summary>
        public bool IsHeld(string? player)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                return false;
            }

            Fold();

            return replay.Match?.Player(player!)?.IsCaged ?? false;
        }

        /// <summary>
        ///     Where the match is, in one line for whoever typed a command or reads the log. Includes the
        ///     ghost count, which appears nowhere else.
        /// </summary>
        public string Status(TimeSpan worldNow)
        {
            Advanced(worldNow);

            string status = director.Status;

            // During the hold the match reads as a lobby, so say it is about to start itself.
            if (StartsAt != null)
            {
                double left = Math.Max(0, (StartsAt.Value - worldNow).TotalSeconds);
                status = Readable.Line($"{status} · held, starting in {left:0.#}s");
            }

            return Ghosts.Count == 0
                ? status
                : $"{status} · {Ghosts.Count} ghost(s): {string.Join(", ", Ghosts.Named)}";
        }

        /// <summary>
        ///     The line for the server's own log, or null when the match's shape (group, attempt, state,
        ///     phases fired) has not changed. The countdown alone does not count as a change.
        /// </summary>
        public string? Announce(TimeSpan worldNow)
        {
            Advanced(worldNow);

            string shape = $"{director.Group}.{director.Generation} {director.State} {director.CompletedPhases}";
            if (shape == announced)
            {
                return null;
            }

            announced = shape;

            return director.Status;
        }

        /// <summary>Where the wall is, for the server's own log.</summary>
        public ZoneState Zone(TimeSpan worldNow)
        {
            Advanced(worldNow);

            return director.Zone;
        }

        /// <summary>The match as the backend is told it.</summary>
        /// <param name="worldNow">The server's world clock.</param>
        /// <param name="alive">How many players are still in the match, from the caller's roster.</param>
        /// <remarks>
        ///     The countdown is null in the lobby, during a pause and after the last phase, so an overlay
        ///     never counts down to nothing.
        /// </remarks>
        public MatchTelemetry Telemetry(TimeSpan worldNow, int alive)
        {
            Advanced(worldNow);

            bool counting = director.State == MatchClockState.Running && !director.AllPhasesFired;

            return new MatchTelemetry(director.Group, director.Generation, director.State, director.Elapsed,
                director.CompletedPhases, director.PhaseCount,
                counting ? director.UntilNextPhase : (TimeSpan?)null, alive);
        }

        /// <summary>
        ///     The match as a line for the wire, whether or not it has changed, for a client that has just
        ///     finished syncing. A null <c>alive</c> shows as a dash until the next broadcast.
        /// </summary>
        public string Line(TimeSpan worldNow, int? alive = null)
        {
            Advanced(worldNow);

            return Broadcast(alive).Encode();
        }

        /// <summary>
        ///     Drives the match forward and hands back the line to send, or null when there is nothing
        ///     anybody needs to hear.
        /// </summary>
        /// <param name="worldNow">The server's world clock.</param>
        /// <param name="stillIn">
        ///     Who is still in the match. Exactly one left names a winner; null never decides the match.
        /// </param>
        /// <param name="alive">How many contestants are still in, for the client HUD.</param>
        public string? Advance(TimeSpan worldNow, IReadOnlyList<string>? stillIn = null,
            int? alive = null)
        {
            Advanced(worldNow);

            // Decide before Finish checks for an end, since deciding is what stops the wall. Absentees
            // within grace come from the log, as the roster forgets them on disconnect.
            director.Standing(stillIn, stillIn == null ? 0 : AwayWithinGrace(), worldNow);
            Finish(worldNow);

            string line = Broadcast(alive).Encode();
            bool changed = line != lastSent;
            bool due = lastSent == null || worldNow - lastSentAt >= heartbeat;

            if (!changed && !due)
            {
                return null;
            }

            lastSent = line;
            lastSentAt = worldNow;

            return line;
        }

        /// <summary>The rules this match is running.</summary>
        public TournamentConfig Rules => config;

        /// <summary>Takes a new tournament config, in the lobby only.</summary>
        /// <param name="rules">The new rules, already parsed and validated by the loader.</param>
        /// <param name="revision">The backend's revision number, for the record.</param>
        /// <param name="by">Whoever wrote it, for the record.</param>
        /// <param name="adopted">The sentence to say, whether or not it was taken.</param>
        /// <returns>False when a match is running, paused or armed, in which case nothing changed.</returns>
        /// <remarks>
        ///     The audit log, group and attempt survive. <see cref="Ghosts"/> is not reseeded from
        ///     <c>ghosts:</c>: that list only seeds a fresh server.
        /// </remarks>
        public bool Adopt(TournamentConfig rules, long revision, string? by, out string adopted)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (StartsAt.HasValue)
            {
                adopted = "a round is armed and about to start, so the rules were not changed. " +
                          "Run `lms reset` first, or wait for the match to finish.";

                return false;
            }

            if (!director.Adopt(rules.Clock, rules.SqueezeFor))
            {
                adopted = "a match is running, so the rules were not changed. They will be taken up " +
                          "at the next lobby.";

                return false;
            }

            config = rules;
            audit = rules.Audit;

            // A short summary of the numbers that matter, not a diff.
            string shape = Readable.Line(
                $"{Schedule.AuthoredPhaseCount} phase(s) of {rules.Clock.CycleLength.TotalSeconds:0.#}s, ",
                $"{Opened(rules.Clock)}",
                $"opening radius {Schedule.Opening.Radius:0.#} m");

            Write(AuditRecordKind.RulesAdopted,
                amount: revision,
                detail: $"revision {revision} from {Nobody(by)}: {shape}");

            adopted = $"the rules are revision {revision}: {shape}. " +
                      "Ghosts are unchanged -- the booth owns those.";

            return true;
        }

        /// <summary>The squeeze the group now playing runs, for a line that says what changed.</summary>
        private ZoneSchedule Schedule => config.SqueezeFor(director.Group);

        /// <summary>Whoever wrote it, or a word that is not an empty string in a record.</summary>
        private static string Nobody(string? by) => string.IsNullOrWhiteSpace(by) ? "the booth" : by!.Trim();

        /// <summary>
        ///     Runs one <c>lms</c> verb and hands back the line to say to whoever typed it. Callers should
        ///     call <see cref="Advance"/> right after so the change reaches clients promptly.
        /// </summary>
        public string Command(string? verb, TimeSpan worldNow)
        {
            if (!AdminCommands.TryParse(verb, out AdminCommand command, out string subject,
                out string addressee))
            {
                return $"'{verb}' is not a command. Try: lms {AdminCommands.Usage}";
            }

            // Catch up first so phases that fell due before the verb are logged ahead of it. After the
            // parse, so an unknown word stays inert; this also starts a round whose hold has expired.
            Advanced(worldNow);

            if (command == AdminCommand.Look)
            {
                return RunLookVerb(subject);
            }

            if (command == AdminCommand.Follow)
            {
                return RunFollowVerb(subject);
            }

            if (command == AdminCommand.FollowOne)
            {
                return RunFollowOneVerb(addressee, subject);
            }
            if (command == AdminCommand.Unfollow)
            {
                return RunUnfollowVerb();
            }

            if (AdminCommands.TakesASubject(command))
            {
                return RunGhostVerb(command, subject);
            }

            if ((command == AdminCommand.Round || command == AdminCommand.RoundForce
                    || command == AdminCommand.Start)
                && (director.Group, director.Generation) == finishedBeforeRestart)
            {
                return $"group {director.Group} finished before the server restarted, so it is not started again. " +
                       $"`lms next` moves on to group {director.Group + 1}; `lms reset` plays group {director.Group} again.";
            }

            if (command == AdminCommand.Round || command == AdminCommand.RoundForce)
            {
                return RunRoundVerb(command == AdminCommand.RoundForce, worldNow);
            }

            if (command == AdminCommand.PodSpawn)
            {
                return RunPodVerb(SeatsTheGroup, AuditRecordKind.PodsPlaced, "podspawn");
            }

            if (command == AdminCommand.PodClear)
            {
                return RunPodVerb(ClearsTheDerelicts, AuditRecordKind.PodsCleared, "podclear");
            }

            if (command == AdminCommand.PodClearAll)
            {
                return RunPodVerb(ClearsEveryPod, AuditRecordKind.PodsCleared, "podclear all");
            }

            if (command == AdminCommand.Start)
            {
            // Shares the armed start's logging path.
                AdminCommandResult attempt = director.Execute(AdminCommand.Start, worldNow);
                if (!attempt.Applied)
                {
                    return attempt.Message;
                }

                Write(Verb(AdminCommand.Start));
                Destination();
                Record(attempt.Events);
                StartsAt = null;

                return attempt.Message;
            }

            (int Group, int Generation) before = (director.Group, director.Generation);
            AdminCommandResult result = director.Execute(command, worldNow);

            if (result.Applied)
            {
                // The verb first, then what it made happen: a skip fires its phase inside the command.
                Write(Verb(command));
                Record(result.Events);

                // A reset or next replaced the match, so any armed hold belongs to the discarded attempt.
                if ((director.Group, director.Generation) != before)
                {
                    // Read before clearing, so the warning names who was still loading.
                    string warned = StillLoading();

                    // Readiness belongs to the discarded attempt too.
                    StartsAt = null;
                    ready.Clear();

                    if (warned.Length > 0)
                    {
                        result = new AdminCommandResult(result.Applied,
                            $"{result.Message} {warned}", result.Events);
                    }
                }
            }

            return result.Message;
        }

        /// <summary>
        ///     A warning naming clients still loading, for a reset or next; empty when there is nobody to
        ///     name. A warning rather than a refusal, since a reset must always work.
        /// </summary>
        private string StillLoading()
        {
            // As in RoundPreparation: with no reports at all, loading and mod-less clients look alike, so say nothing.
            if (Connected == null || ready.Count == 0)
            {
                return string.Empty;
            }

            List<string> unready = new List<string>();
            foreach (PlayerReading reading in Connected())
            {
                if (!Ghosts.IsGhost(reading.Name) && !ready.Contains(reading.Name))
                {
                    unready.Add(reading.Name);
                }
            }

            if (unready.Count == 0)
            {
                return string.Empty;
            }

            unready.Sort(StringComparer.Ordinal);
            return $"warning: {string.Join(", ", unready)} ha" +
                   (unready.Count == 1 ? "s" : "ve") +
                   " not reported ready; the next `lms round` will wait for them.";
        }

        /// <summary>
        ///     Runs <c>lms round</c>: decides, hands the world work to the server, records it, and arms
        ///     the start. Nothing is recorded unless the server prepared the round, and the record is
        ///     written before the start is armed.
        /// </summary>
        private string RunRoundVerb(bool forced, TimeSpan worldNow)
        {
            if (PreparesTheRound == null || Connected == null)
            {
                return "there is no server here to do it, so round did nothing.";
            }

            // Seating is a teleport, and the game freezes a player's stats per teleport but thaws them
            // once however many overlapped, so a group seated twice never loses air, food or water.
            // Force does not override it, since the freeze has nothing to do with the count.
            if (StartsAt.HasValue)
            {
                double left = Math.Max(0, (StartsAt.Value - worldNow).TotalSeconds);
                return Readable.Line(
                    $"a round is already armed and starts in {left:0.#}s, so it was not prepared again: ",
                    $"seating everybody twice freezes their air, food and water. `lms reset` first to seat them again.");
            }

            RoundPlan plan = RoundPreparation.Plan(director.State, Connected(), Ghosts,
                config.Spawn.GroupSize, forced, Ready);

            if (plan.IsRefused)
            {
                return plan.Message;
            }

            RoundOutcome outcome = PreparesTheRound(plan);
            if (!outcome.IsPrepared)
            {
                return outcome.Message;
            }

            // One record for the group, naming contestants and kit; the config file can change between groups.
            Write(AuditRecordKind.RoundPrepared,
                amount: config.Start.Hold.TotalSeconds,
                detail: Prepared(plan, outcome));

            // After RoundPrepared, one record per corrected player so each can see their mode was changed.
            foreach (PlayerMode corrected in outcome.Corrected)
            {
                Write(AuditRecordKind.GameModeCorrected,
                    player: corrected.Name,
                    detail: $"was in {corrected.Mode}, put back into survival before the round");
            }

            StartsAt = config.Start.Holds ? worldNow + config.Start.Hold : (TimeSpan?)null;

            if (!config.Start.Holds)
            {
                // No hold configured: start immediately.
                Started(worldNow);
            }

            string held = config.Start.Holds
                ? Readable.Line($"held {config.Start.Hold.TotalSeconds:0.#}s; the match starts as the fields go")
                : "no hold configured, so the match has started";

            string against = plan.AgainstTheCount
                ? $" A group is {config.Spawn.GroupSize} and this is {plan.Contestants.Count}, forced."
                : string.Empty;

            return $"{outcome.Message} {held}.{against}";
        }

        /// <summary>What a <c>RoundPrepared</c> record says, in the log's own one-line shape.</summary>
        private string Prepared(RoundPlan plan, RoundOutcome outcome)
        {
            string kit = config.Start.Kit.Count == 0
                ? "nothing"
                : string.Join("+", config.Start.Kit);

            string forced = plan.AgainstTheCount ? " forced" : string.Empty;

            return $"{string.Join("+", plan.Contestants)} kit={kit}{forced} {outcome.Detail}".Trim();
        }

        /// <summary>
        ///     Starts the match and writes what a start owes the log, including the drawn destination.
        ///     Shared by the operator's <c>start</c> and the armed start at the end of a hold.
        /// </summary>
        private void Started(TimeSpan worldNow)
        {
            AdminCommandResult result = director.Execute(AdminCommand.Start, worldNow);
            if (!result.Applied)
            {
                return;
            }

            Write(Verb(AdminCommand.Start));
            Destination();
            Record(result.Events);
        }

        /// <summary>The director's broadcast, plus the alive count and destination only this host knows.</summary>
        private MatchBroadcast Broadcast(int? alive) =>
            director.Broadcast().Saying(alive, ShownDestination());

        /// <summary>
        ///     This group's destination by name, or null when withheld. Read per broadcast because the
        ///     flag can change when new rules are adopted.
        /// </summary>
        private string? ShownDestination() =>
            config.Flags.ShowDestination ? config.DestinationFor(director.Group)?.Name : null;

        /// <summary>
        ///     Records which of the pool's places this group is squeezed into. Written on start, so a
        ///     match that never started leaves no claim in the log.
        /// </summary>
        private void Destination()
        {
            ZoneDestination? destination = config.DestinationFor(director.Group);
            if (destination == null)
            {
                return;
            }

            Write(AuditRecordKind.ZoneDestinationDrawn,
                detail: destination.Name, amount: config.DestinationSeed,
                position: destination.Centre);
        }

        /// <summary>
        ///     Runs <c>lms ghost</c> or <c>lms unghost</c>, and says what it did. Nothing is written to
        ///     the audit log.
        /// </summary>
        private string RunGhostVerb(AdminCommand command, string subject)
        {
            string word = AdminCommands.Word(command);

            if (string.IsNullOrWhiteSpace(subject))
            {
                return $"{word} needs a name: lms {word} <player>.";
            }

            string name = subject.Trim();

            if (command == AdminCommand.Unghost)
            {
                return Ghosts.Remove(name)
                    ? $"{name} is in the match again. {Counted()}."
                    : $"{name} was not a ghost, so nothing changed. {Counted()}.";
            }

            return Ghosts.Add(name)
                ? $"{name} is a ghost: no pod, no wall, no ring, not on the board, and the camera is " +
                  $"theirs. {Counted()}."
                : $"{name} was already a ghost. {Counted()}.";
        }

        /// <summary>
        ///     Puts every ghost's camera on one contestant, or says why it did not.
        /// </summary>
        /// <remarks>
        ///     Refuses a missing name, a name nobody is connected as, and a ghost (whose camera would land
        ///     on nothing). Names match case-insensitively.
        /// </remarks>
        private string RunLookVerb(string subject)
        {
            string word = AdminCommands.Word(AdminCommand.Look);

            if (!TryContestant(subject, word, "no camera moved", out PlayerReading found,
                    out string refusal))
            {
                return refusal;
            }

            if (PointsTheCameras == null)
            {
                return $"this end cannot move a camera, so {word} did nothing.";
            }

            int told = PointsTheCameras(new LookBroadcast(found.Name, found.Position));

            return told == 0
                ? $"nobody is a ghost, so there was no camera to put on {found.Name}."
                : $"{told} camera(s) on {found.Name}.";
        }

        /// <summary>
        ///     Keeps every ghost's camera on one contestant, or says why it did not. Same refusals as
        ///     <see cref="RunLookVerb"/>; only the name is sent. Nothing is recorded.
        /// </summary>
        private string RunFollowVerb(string subject)
        {
            string word = AdminCommands.Word(AdminCommand.Follow);

            if (!TryContestant(subject, word, "no camera moved", out PlayerReading found,
                    out string refusal))
            {
                return refusal;
            }

            if (HoldsTheCameras == null)
            {
                return $"this end cannot move a camera, so {word} did nothing.";
            }

            FollowBroadcast follow = new FollowBroadcast(found.Name);

            // Set only after the refusals, so a typo is never what a later-joining caster is told.
            Followed = follow;

            int told = HoldsTheCameras(follow);
            Deliver(follow);
            return told == 0
                ? $"nobody is a ghost, so there was no camera to keep on {found.Name}."
                : $"{told} camera(s) staying on {found.Name}.";
        }

        /// <summary>
        ///     Sends one addressed follow to each connected ghost, alongside the unaddressed broadcast so
        ///     older clients keep working. Does nothing unless <see cref="CamerasFollow"/> is set.
        /// </summary>
        private void Deliver(FollowBroadcast follow)
        {
            if (CamerasFollow == null || Connected == null)
            {
                return;
            }

            IReadOnlyList<PlayerReading> ghosts = Ghosts.Ghosts(Connected());
            foreach (PlayerReading ghost in ghosts)
            {
                CamerasFollow(new FollowBroadcast(ghost.Name, follow.Name));
            }
        }

        /// <summary>
        ///     Points one ghost's camera at one contestant (<c>lms followOne</c>). Checks the target like
        ///     <see cref="RunFollowVerb"/>, then that the addressee is a connected ghost.
        /// </summary>
        /// <remarks>
        ///     Calls only <see cref="CamerasFollow"/>, never <see cref="HoldsTheCameras"/>. Nothing is
        ///     recorded.
        /// </remarks>
        private string RunFollowOneVerb(string addressee, string subject)
        {
            string word = AdminCommands.Word(AdminCommand.FollowOne);

            if (string.IsNullOrWhiteSpace(addressee))
            {
                return $"{word} needs a ghost and a target: lms {word} <ghost> <target>.";
            }

            bool letting = string.IsNullOrWhiteSpace(subject);
            PlayerReading found = default;

            if (!letting && !TryContestant(subject, word, "no camera moved", out found,
                    out string refusal))
            {
                return refusal;
            }

            if (CamerasFollow == null)
            {
                return $"this end cannot move a camera, so {word} did nothing.";
            }

            string ghost = addressee.Trim();
            if (!Ghosts.IsGhost(ghost) || !IsConnected(ghost))
            {
                return $"{ghost} is not connected as a ghost, so no camera moved. Who is filming: " +
                       (Ghosts.Count > 0 ? string.Join(", ", Ghosts.Named) : "nobody") + ".";
            }

            FollowBroadcast follow = letting
                ? new FollowBroadcast(ghost, null)
                : new FollowBroadcast(ghost, found.Name);

            if (!CamerasFollow(follow))
            {
                return $"the camera for {ghost} could not be told, so nothing moved.";
            }

            Followed = follow;

            return letting
                ? $"{ghost}'s camera set free: they fly it again."
                : $"{ghost}'s camera staying on {found.Name}.";
        }

        private bool IsConnected(string name)
        {
            if (Connected == null)
            {
                return false;
            }

            foreach (PlayerReading reading in Connected())
            {
                if (string.Equals(reading.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Gives the cameras back to the people flying them (<c>lms unfollow</c>). Never refuses and
        ///     always sends, so a client that missed the last message is corrected.
        /// </summary>
        private string RunUnfollowVerb()
        {
            string word = AdminCommands.Word(AdminCommand.Unfollow);

            Followed = FollowBroadcast.LetGo;

            if (HoldsTheCameras == null)
            {
                return $"this end cannot move a camera, so {word} did nothing.";
            }

            int told = HoldsTheCameras(FollowBroadcast.LetGo);

            return told == 0
                ? "nobody is a ghost, so there was no camera to let go; a caster who joins now will " +
                  "fly their own."
                : $"{told} camera(s) let go: the casters fly them again.";
        }

        /// <summary>
        ///     Turns a name an operator typed into the contestant it means, or into the refusal. Shared by
        ///     the camera verbs.
        /// </summary>
        /// <remarks>
        ///     Matches case-insensitively, and returns the server's spelling of the name for the wire.
        /// </remarks>
        private bool TryContestant(string subject, string word, string cost, out PlayerReading found,
            out string refusal)
        {
            found = default;

            if (string.IsNullOrWhiteSpace(subject))
            {
                refusal = $"{word} needs a name: lms {word} <player>.";
                return false;
            }

            string name = subject.Trim();

            if (Connected == null)
            {
                refusal = $"this end cannot see who is connected, so {word} has nobody to point at.";
                return false;
            }

            bool any = false;
            foreach (PlayerReading reading in Connected())
            {
                if (string.Equals(reading.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = reading;
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                refusal = $"{name} is not in the match, so {cost}.";
                return false;
            }

            if (Ghosts.IsGhost(found.Name))
            {
                refusal = $"{found.Name} is a ghost, not a contestant: a camera sent there sees " +
                          $"nothing. No camera moved.";
                return false;
            }

            refusal = string.Empty;

            return true;
        }

        /// <summary>How many ghosts there are, as the end of a sentence.</summary>
        private string Counted() =>
            Ghosts.Count == 0 ? "nobody is a ghost now" : $"{Ghosts.Count} ghost(s)";

        /// <summary>
        ///     Runs one of the pod verbs via its adapter hook, and records it only if something changed.
        ///     A missing hook answers the verb rather than throwing.
        /// </summary>
        private string RunPodVerb(Func<PodCommandOutcome>? handler, AuditRecordKind kind, string verb)
        {
            if (handler == null)
            {
                return $"there is no server here to do it, so {verb} did nothing.";
            }

            PodCommandOutcome outcome = handler();

            if (outcome.Changed > 0)
            {
                Write(kind, detail: outcome.Detail);
            }

            return outcome.Message;
        }

        /// <summary>The audit record kind for an applied verb. Refused commands write nothing.</summary>
        private static AuditRecordKind Verb(AdminCommand command)
        {
            switch (command)
            {
                case AdminCommand.PodSpawn: return AuditRecordKind.PodsPlaced;
                case AdminCommand.PodClear: return AuditRecordKind.PodsCleared;
                case AdminCommand.PodClearAll: return AuditRecordKind.PodsCleared;
                case AdminCommand.Start: return AuditRecordKind.MatchStarted;
                case AdminCommand.Pause: return AuditRecordKind.MatchPaused;
                case AdminCommand.Resume: return AuditRecordKind.MatchResumed;
                case AdminCommand.Skip: return AuditRecordKind.MatchSkipped;
                case AdminCommand.Reset: return AuditRecordKind.MatchReset;
                default: return AuditRecordKind.GroupAdvanced;
            }
        }

        /// <summary>Records what happened to the players, stamped with the match it happened in.</summary>
        /// <remarks>
        ///     A client-announced change carries its generation and is dropped if that attempt has been
        ///     replaced; a server-derived change carries none and is never dropped.
        /// </remarks>
        public void Record(IReadOnlyList<RosterChange> changes)
        {
            foreach (RosterChange change in changes)
            {
                if (change.Generation.HasValue && change.Generation.Value != director.Generation)
                {
                    continue;
                }

                Write(change.Kind, player: change.Player, position: change.Position,
                    biome: change.Biome, health: change.Health, elapsed: change.Elapsed,
                    amount: change.Amount, phase: change.Phase, detail: change.Detail);
            }
        }

        /// <summary>
        ///     What the wall took off a returning player while they were away, or null when they owe
        ///     nothing.
        /// </summary>
        /// <param name="player">Their name, from the server's own reading of who is connected.</param>
        /// <param name="worldNow">The shared world clock, so the match's own clock advances first.</param>
        /// <remarks>
        ///     Read-only; <see cref="Charged"/> records it. Owes nothing if the log never saw them leave,
        ///     they left inside the wall, they were already eliminated, the match was replaced since, or
        ///     this absence was already charged.
        /// </remarks>
        public ZoneDebt? Arrears(string player, TimeSpan worldNow)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                return null;
            }

            Advanced(worldNow);
            Fold();

            MatchAudit? match = replay.Match;
            if (match == null || match.Group != director.Group || match.Generation != director.Generation)
            {
                return null;
            }

            PlayerAudit? was = match.Player(player);
            if (was == null || !was.IsAlive || !was.LeftAt.HasValue
                || !was.LastPosition.HasValue || !was.Health.HasValue)
            {
                return null;
            }

            // A charge after the departure settled it; a later departure is a new, unpaid absence.
            if (was.ArrearsAt.HasValue && was.ArrearsAt.Value >= was.LeftAt.Value)
            {
                return null;
            }

            ZoneDebt debt = Arrears().Of(was.LastPosition.Value, was.LeftAt.Value, director.Elapsed,
                was.Health.Value);

            return debt.Owed ? debt : (ZoneDebt?)null;
        }

        /// <summary>
        ///     Records that a joining player's body was put back into survival, for anyone who arrived
        ///     after <c>lms round</c> swept the group.
        /// </summary>
        /// <param name="player">The name the log identifies them by.</param>
        /// <param name="wasMode">The mode the server found them in, in its own spelling.</param>
        /// <remarks>
        ///     Recorded every time it happens. The detail says <em>joined</em> so it can be told apart
        ///     from the round's group-wide sweep.
        /// </remarks>
        public void Corrected(string player, string wasMode)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                throw new ArgumentException(
                    "A correction is about a player, so it needs a player name.", nameof(player));
            }

            Write(AuditRecordKind.GameModeCorrected,
                player: player,
                detail: $"joined in {Named(wasMode)}, put back into survival");
        }

        /// <summary>The mode as the server spelled it, or a word for not having been told.</summary>
        private static string Named(string? mode) =>
            string.IsNullOrWhiteSpace(mode) ? "a mode nobody named" : mode!.Trim();

        /// <summary>
        ///     Whether this match is already finished with a player, so a reconnect cannot undo an
        ///     elimination.
        /// </summary>
        /// <param name="player">The name the log identifies them by.</param>
        /// <param name="worldNow">Now, on the clock every machine shares.</param>
        /// <remarks>
        ///     Read from the audit log, by name, since the roster forgets departed players and Nitrox ids
        ///     do not survive a reconnect. <see cref="replay"/> holds one match, so reset and next clear it.
        /// </remarks>
        public bool IsOut(string? player, TimeSpan worldNow)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                return false;
            }

            Advanced(worldNow);
            Fold();

            MatchAudit? match = replay.Match;
            if (match == null || match.Group != director.Group || match.Generation != director.Generation)
            {
                return false;
            }

            PlayerAudit? was = match.Player(player!);

            return was != null && !was.IsAlive;
        }

        /// <summary>
        ///     How many players the match is not finished with are disconnected but still inside
        ///     <c>disconnect.grace</c>.
        /// </summary>
        /// <remarks>
        ///     Read from the audit log, since the roster drops departed players. Counts alive, currently
        ///     left, non-ghost players of this attempt; measured in match time, so a pause extends the wait.
        /// </remarks>
        private int AwayWithinGrace()
        {
            TimeSpan grace = config.Disconnect.Grace;
            if (grace <= TimeSpan.Zero)
            {
                return 0;
            }

            Fold();

            MatchAudit? match = replay.Match;
            if (match == null || match.Group != director.Group || match.Generation != director.Generation)
            {
                return 0;
            }

            int waiting = 0;
            foreach (PlayerAudit player in match.Players)
            {
                if (player.IsAlive && player.HasLeft && player.LeftAt.HasValue
                    && !Ghosts.IsGhost(player.Name)
                    && director.Elapsed - player.LeftAt.Value < grace)
                {
                    waiting++;
                }
            }

            return waiting;
        }

        /// <summary>
        ///     What a disconnected player owes, against the wall the group they left was under. Built per
        ///     call from the rules in force, since a lobby can adopt new rules.
        /// </summary>
        private ZoneArrears Arrears() =>
            new ZoneArrears(config.SqueezeFor(director.Group), config.ZonePolicy,
                config.Clock.CycleLength, config.Clock.OpeningLength, config.Disconnect);

        /// <summary>
        ///     Records that a returning player was charged what <see cref="Arrears"/> said. The server
        ///     writes it; the client applies the damage. It also marks the absence as settled.
        /// </summary>
        public void Charged(string player, ZoneDebt debt)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                throw new ArgumentException("A charge is about a player, so it needs a player name.", nameof(player));
            }

            Fold();

            WorldPoint? where = replay.Match?.Player(player)?.LastPosition;
            string? biome = replay.Match?.Player(player)?.LastBiome;

            Write(AuditRecordKind.ZoneArrears, player: player, position: where, biome: biome,
                amount: debt.Damage, health: debt.HealthLeft,
                detail: Readable.Line($"away {debt.Absence.TotalSeconds:0}s"));
        }

        /// <summary>"after a 600s opening, " when the opening differs from the cycle, otherwise empty.</summary>
        private static string Opened(MatchClockSettings clock) =>
            clock.OpeningLength == clock.CycleLength
                ? string.Empty
                : Readable.Line($"after a {clock.OpeningLength.TotalSeconds:0.#}s opening, ");

        /// <summary>Records that one player took health off another.</summary>
        /// <param name="victim">Who was hurt, by the name the log identifies them by.</param>
        /// <param name="attacker">Who did it, from the server's own roster and never from a message.</param>
        /// <param name="arm">Which arm, which is what <c>detail</c> says.</param>
        /// <param name="damage">What it cost, as configured. Must be positive.</param>
        /// <param name="healthBefore">What the victim was on, as the server last read it.</param>
        /// <param name="where">Where the victim was, from the roster.</param>
        /// <param name="biome">What the victim's client called the place.</param>
        /// <remarks>
        ///     Written by the server because it names another player, which clients may not report. The
        ///     client still applies the damage; the recorded health is the server's estimate.
        /// </remarks>
        public void Hurt(string victim, string attacker, StrikeWeapon arm, double damage,
            double healthBefore, WorldPoint? where = null, string? biome = null)
        {
            if (string.IsNullOrWhiteSpace(victim))
            {
                throw new ArgumentException("A hit is about a player, so it needs a player name.", nameof(victim));
            }

            if (string.IsNullOrWhiteSpace(attacker))
            {
                throw new ArgumentException(
                    "A hit needs the name of whoever landed it: that is the field this record exists " +
                    "for, and the log is the artifact somebody argues with afterwards.", nameof(attacker));
            }

            if (!(damage > 0) || double.IsInfinity(damage))
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage,
                    "A hit worth nothing is a refusal, and a refusal writes no record.");
            }

            // Floored at zero.
            double left = healthBefore - damage;

            Write(AuditRecordKind.PlayerHurt, player: victim, actor: attacker, position: where,
                biome: biome, amount: damage, health: left > 0 ? left : 0,
                detail: StrikeWeapons.Word(arm));
        }

        /// <summary>
        ///     The tournament table, or null until a match has finished. Folded from this host's audit
        ///     log, finished matches only, scored by <c>ScoringRules.Default</c>.
        /// </summary>
        /// <param name="worldNow">The shared world clock, so the match's own clock advances first.</param>
        public StandingsTelemetry? Standings(TimeSpan worldNow)
        {
            Advanced(worldNow);
            Fold();

            if (tableFoldedAt != folded)
            {
                tableFoldedAt = folded;
                table = StandingsTelemetry.Of(
                    Lms.Core.Results.Standings.OfFinished(earlier.Concat(replay.Matches), ScoringRules.Default),
                    ScoringRules.Default);
            }

            return table;
        }

        /// <summary>Writes down where everybody stood, if a sample is due.</summary>
        /// <param name="worldNow">The shared world clock, so the match's own clock advances first.</param>
        /// <param name="rows">Everybody in the world, as the roster built them.</param>
        /// <remarks>
        ///     Only while the match is live, only players still in it, and on a match-time cadence so a
        ///     pause stops it.
        /// </remarks>
        public void Sample(TimeSpan worldNow, IReadOnlyList<PlayerTelemetry> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            Advanced(worldNow);

            if (!audit.SamplesPositions || !director.IsLive)
            {
                return;
            }

            (int, int) match = (director.Group, director.Generation);
            TimeSpan elapsed = director.Elapsed;

            if (sampled == match && elapsed - sampledAt < audit.PositionInterval)
            {
                return;
            }

            // Use the tick's time, so a slow tick writes one late sample rather than catching up.
            sampled = match;
            sampledAt = elapsed;

            foreach (PlayerTelemetry row in rows)
            {
                if (row.Alive)
                {
                    Write(AuditRecordKind.PlayerPosition, player: row.Name, position: row.Position,
                        biome: row.Biome, health: row.Health);
                }
            }
        }

        /// <summary>
        ///     Drives the match to a moment and records whatever fell due on the way. Every public method
        ///     goes through here so no advance ever skips the log.
        /// </summary>
        private void Advanced(TimeSpan worldNow)
        {
            Record(director.AdvanceTo(worldNow));
            Freed(worldNow);
        }

        /// <summary>
        ///     Starts the match when a round's hold has run out, checked on every <see cref="Advanced"/>.
        ///     A match already out of the lobby disarms it.
        /// </summary>
        /// <remarks>
        ///     Reset and next leave the clock in the lobby too, so <see cref="Command"/> disarms the hold
        ///     for those.
        /// </remarks>
        private void Freed(TimeSpan worldNow)
        {
            if (StartsAt == null)
            {
                return;
            }

            if (director.State != MatchClockState.NotStarted)
            {
                StartsAt = null;
                return;
            }

            if (worldNow < StartsAt.Value)
            {
                return;
            }

            StartsAt = null;
            Started(worldNow);
        }

        /// <summary>Brings the fold up to date with the log, using its own cursor.</summary>
        private void Fold()
        {
            IReadOnlyList<AuditRecord> records = Audit.Records;

            while (folded < records.Count)
            {
                replay.Apply(records[folded]);
                folded++;
            }
        }

        private void Record(IReadOnlyList<MatchClockEvent> events)
        {
            foreach (MatchClockEvent happened in events)
            {
                // The phase's scheduled time, not the tick that noticed it, so replays match.
                Write(happened.Kind == MatchClockEventKind.PhaseWarning
                        ? AuditRecordKind.PhaseWarned
                        : AuditRecordKind.PhaseStarted,
                    phase: happened.PhaseNumber, elapsed: happened.AtElapsed);
            }
        }

        /// <summary>Writes the end of a match, once per attempt.</summary>
        /// <remarks>
        ///     <c>ZoneClosed</c> and <c>MatchEnded</c> are separate facts: a match won before the collapse
        ///     has no <c>ZoneClosed</c>, and one won inside it has both. The close opens a
        ///     <c>match.victoryWindow</c> in which a winner can still emerge.
        /// </remarks>
        private void Finish(TimeSpan worldNow)
        {
            (int, int) match = (director.Group, director.Generation);

            if (director.IsClosed && closed != match)
            {
                closed = match;
                closedAt = worldNow;
                Write(AuditRecordKind.ZoneClosed);
            }

            if (ended == match)
            {
                return;
            }

            // Check for a winner first: the closed branch below latches `ended`.
            if (director.IsSettled(worldNow))
            {
                ended = match;
                Write(AuditRecordKind.MatchEnded, player: director.Winner);

                return;
            }

            // Nobody won and the wall has been shut for a whole victory window: the collapse took
            // everybody. Bounded so a server with enforceDamage off still ends the match; not while
            // anyone is within grace, since they could still be the winner.
            if (director.IsClosed && !director.IsDecided
                && worldNow - closedAt >= config.Clock.VictoryWindow
                && AwayWithinGrace() == 0)
            {
                ended = match;
                Write(AuditRecordKind.MatchEnded);
            }
        }

        /// <summary>
        ///     Appends one audit record, stamped with the match as it stands after whatever caused it, so
        ///     a reset lands in the generation it created.
        /// </summary>
        private void Write(AuditRecordKind kind, string? player = null, string? actor = null,
            int? phase = null, TimeSpan? elapsed = null, WorldPoint? position = null,
            string? biome = null, double? health = null, double? amount = null, string? detail = null)
        {
            Audit.Append(clock(),
                new MatchStamp(director.Group, director.Generation, elapsed ?? director.Elapsed),
                kind, player: player, actor: actor, phase: phase, position: position, biome: biome,
                health: health, amount: amount, detail: detail);
        }
    }
}
