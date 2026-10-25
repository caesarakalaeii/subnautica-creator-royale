using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Match;
using Lms.Core.Membranes;
using Lms.Core.Spawning;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Lms.Core.Zones;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lms.Core.Configuration
{
    /// <summary>
    ///     Reads the tournament rules from YAML, reporting every problem in one pass.
    /// </summary>
    /// <remarks>
    ///     Walks the YAML by hand so each problem names the field that caused it. Unknown fields
    ///     are errors, so a typo cannot silently keep a default.
    /// </remarks>
    public static class TournamentConfigLoader
    {
        private const double DefaultFullExposureAt = 150;
        private static readonly TimeSpan DefaultDamageInterval = TimeSpan.FromSeconds(2);

        public static TournamentConfigLoadResult Load(string yaml)
        {
            List<TournamentConfigProblem> problems = new List<TournamentConfigProblem>();

            YamlMappingNode? root = ParseRoot(yaml, problems);
            if (root == null)
            {
                return new TournamentConfigLoadResult(null, problems);
            }

            RejectUnknownKeys(root, string.Empty, problems,
                "match", "zone", "ring", "prawn", "blade", "spectator", "spawn", "telemetry", "audit",
                "disconnect", "flags", "ghosts", "start", "medkit", "caster");

            YamlMappingNode? match = Section(root, "match", string.Empty, problems, required: true);
            YamlMappingNode? zone = Section(root, "zone", string.Empty, problems, required: true);
            YamlMappingNode? ring = Section(root, "ring", string.Empty, problems, required: false);
            YamlMappingNode? prawn = Section(root, "prawn", string.Empty, problems, required: false);
            YamlMappingNode? blade = Section(root, "blade", string.Empty, problems, required: false);
            YamlMappingNode? spectator = Section(root, "spectator", string.Empty, problems, required: false);
            YamlMappingNode? spawn = Section(root, "spawn", string.Empty, problems, required: false);
            YamlMappingNode? telemetry = Section(root, "telemetry", string.Empty, problems, required: false);
            YamlMappingNode? audit = Section(root, "audit", string.Empty, problems, required: false);
            YamlMappingNode? disconnect = Section(root, "disconnect", string.Empty, problems, required: false);
            YamlMappingNode? flags = Section(root, "flags", string.Empty, problems, required: false);

            // A plain list of names at the root rather than a section.
            List<string>? ghosts = Names(root, "ghosts", problems);

            YamlMappingNode? start = Section(root, "start", string.Empty, problems, required: false);
            StartPolicy? startPolicy = Start(start, problems);

            YamlMappingNode? medkit = Section(root, "medkit", string.Empty, problems, required: false);
            MedKitPolicy? medKitPolicy = MedKit(medkit, problems);

            YamlMappingNode? caster = Section(root, "caster", string.Empty, problems, required: false);
            CasterPolicy? casterPolicy = Caster(caster, problems);

            if (match != null)
            {
                RejectUnknownKeys(match, "match", problems, "cycleLength", "warningLead", "victoryWindow",
                    "openingLength");
            }

            if (zone != null)
            {
                RejectUnknownKeys(zone, "zone", problems, "damageInterval", "fullExposureAt", "colours", "opening", "phases",
                    "destinations", "destinationSeed", "destinationOrder");
            }

            if (ring != null)
            {
                RejectUnknownKeys(ring, "ring", problems,
                    "minRadius", "maxRadius", "minLifetime", "maxLifetime", "placementMargin", "energyCost");
            }

            if (prawn != null)
            {
                RejectUnknownKeys(prawn, "prawn", problems,
                    "hurtsPlayers", "clawDamage", "drillDamage", "reach", "minInterval");
            }

            if (blade != null)
            {
                RejectUnknownKeys(blade, "blade", problems,
                    "hurtsPlayers", "knifeDamage", "heatBladeDamage", "reach", "minInterval",
                    "resistances");
            }

            if (spectator != null)
            {
                RejectUnknownKeys(spectator, "spectator", problems, "deathCam", "spectate", "flySpeed");
            }

            if (spawn != null)
            {
                RejectUnknownKeys(spawn, "spawn", problems,
                    "ownPod", "minSeparation", "groupSize", "circleRadius");
            }

            if (telemetry != null)
            {
                RejectUnknownKeys(telemetry, "telemetry", problems, "url", "interval", "timeout",
                    "acceptsCommands");
            }

            if (audit != null)
            {
                RejectUnknownKeys(audit, "audit", problems, "positionInterval");
            }

            if (disconnect != null)
            {
                RejectUnknownKeys(disconnect, "disconnect", problems, "grace");
            }

            if (start != null)
            {
                RejectUnknownKeys(start, "start", problems,
                    "hold", "fieldRadius", "kit", "lockedRecipes", "recipes", "vitals");
            }

            if (medkit != null)
            {
                RejectUnknownKeys(medkit, "medkit", problems, "cooldown");
            }

            if (caster != null)
            {
                RejectUnknownKeys(caster, "caster", problems, "nameplates", "followDistance",
                    "followHeight", "followClosest", "followFurthest");
            }

            if (flags != null)
            {
                RejectUnknownKeys(flags, "flags", problems, "enforceDamage", "enforceContainment",
                    "hideBeacons", "showDestination");
            }

            TimeSpan? cycleLength = Duration(match, "cycleLength", "match", problems);
            TimeSpan? warningLead = Duration(match, "warningLead", "match", problems);

            // Absent means "the same as the cycle"; passed on as null because cycleLength may not
            // have parsed.
            TimeSpan? openingLength = Duration(match, "openingLength", "match", problems, required: false);

            // Optional: a broadcast beat rather than a rule.
            TimeSpan victoryWindow = Duration(match, "victoryWindow", "match", problems, required: false)
                                     ?? MatchClockSettings.DefaultVictoryWindow;

            // Feel knobs rather than rules, so they default instead of failing the load.
            TimeSpan damageInterval = Duration(zone, "damageInterval", "zone", problems, required: false) ?? DefaultDamageInterval;
            double fullExposureAt = PositiveNumber(zone, "fullExposureAt", "zone", problems, required: false) ?? DefaultFullExposureAt;

            // Cosmetic, so absent means the default pair rather than an error.
            ZoneColours colours = Colours(zone, problems);

            (ZoneSphere Sphere, double Damage)? opening = Opening(zone, problems);

            // Without destinations the phases carry their own centres. With them, where a match
            // finishes is drawn per group, the phase centres are derived and a phase `centre` is an error.
            List<ZoneDestination>? destinations = Destinations(zone, problems);
            int? destinationSeed = WholeNumber(zone, "destinationSeed", "zone", problems);
            bool listed = DestinationOrder(zone, destinations != null, destinationSeed != null, problems);

            List<ZonePhase>? phases = destinations == null ? Phases(zone, problems) : null;
            List<ZonePhaseShape>? shapes = destinations == null ? null : PhaseShapes(zone, problems);

            // Optional as a whole and field by field; absent means the game's stock stasis rifle.
            RingPolicy? ringPolicy = Ring(ring, problems);

            // Off when absent or unstated: these enable damage between players.
            PrawnPolicy? prawnPolicy = Prawn(prawn, problems);
            BladePolicy? bladePolicy = Blade(blade, problems);

            // Optional; absent means the game's own death camera.
            SpectatorPolicy? spectatorPolicy = Spectator(spectator, problems);

            // Off when absent, leaving the join path untouched.
            SpawnPolicy? spawnPolicy = Spawn(spawn, problems);

            // Off when absent: a match must not need a caster booth to start.
            TelemetryPolicy? telemetryPolicy = Telemetry(telemetry, problems);

            // On when absent: position samples cannot be recovered after the fact.
            AuditPolicy? auditPolicy = Audit(audit, problems);

            // On when absent, so disconnecting is never a free escape. Whether it costs health is
            // still decided by `flags.enforceDamage`.
            DisconnectPolicy? disconnectPolicy = Disconnect(disconnect, problems);

            if (problems.Count > 0 || cycleLength == null || warningLead == null || opening == null
                || (phases == null && shapes == null) || ringPolicy == null || prawnPolicy == null
                || bladePolicy == null
                || spectatorPolicy == null || spawnPolicy == null || telemetryPolicy == null
                || auditPolicy == null || disconnectPolicy == null)
            {
                return new TournamentConfigLoadResult(null, problems);
            }

            return Build(cycleLength.Value, warningLead.Value, victoryWindow, openingLength, damageInterval, fullExposureAt,
                colours, opening.Value, phases, shapes, destinations, destinationSeed, listed, ghosts, startPolicy,
                medKitPolicy, casterPolicy,
                ringPolicy, prawnPolicy, bladePolicy, spectatorPolicy, spawnPolicy,
                telemetryPolicy, auditPolicy, disconnectPolicy,
                new TournamentFlags(
                    Flag(flags, "enforceDamage", "flags", problems) ?? false,
                    // On when omitted: being held is recoverable, being dead is not.
                    Flag(flags, "enforceContainment", "flags", problems) ?? true,
                    // On when omitted: a leaked position cannot be taken back.
                    Flag(flags, "hideBeacons", "flags", problems) ?? true,
                    // Off when omitted: revealing the endgame cannot be taken back.
                    Flag(flags, "showDestination", "flags", problems) ?? false),
                problems);
        }

        private static TournamentConfigLoadResult Build(
            TimeSpan cycleLength, TimeSpan warningLead, TimeSpan victoryWindow, TimeSpan? openingLength,
            TimeSpan damageInterval, double fullExposureAt,
            ZoneColours colours, (ZoneSphere Sphere, double Damage) opening,
            IReadOnlyList<ZonePhase>? phases,
            IReadOnlyList<ZonePhaseShape>? shapes, IReadOnlyList<ZoneDestination>? destinations, int? destinationSeed,
            bool listed,
            IReadOnlyList<string>? ghosts, StartPolicy? start, MedKitPolicy? medKit,
            CasterPolicy? caster,
            RingPolicy ring, PrawnPolicy prawn, BladePolicy blade, SpectatorPolicy spectator,
            SpawnPolicy spawn,
            TelemetryPolicy telemetry, AuditPolicy audit, DisconnectPolicy disconnect, TournamentFlags flags,
            List<TournamentConfigProblem> problems)
        {
            MatchClockSettings? clock = null;
            try
            {
                clock = new MatchClockSettings(cycleLength, warningLead, victoryWindow, openingLength);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // The settings type owns the domain rules; report its refusal against the field.
                problems.Add(new TournamentConfigProblem("match." + ex.ParamName, ex.Message));
            }

            ZonePolicy? policy = null;
            try
            {
                policy = new ZonePolicy(damageInterval, fullExposureAt);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("zone." + ex.ParamName, ex.Message));
            }

            ZoneSqueeze? squeeze = null;
            ZoneSchedule? schedule = null;
            try
            {
                if (destinations != null && shapes != null)
                {
                    squeeze = new ZoneSqueeze(opening.Sphere, opening.Damage, shapes, destinations, listed);

                    // The carried schedule is group one's; later groups draw their own. Its opening
                    // sphere and phase count are the same for every destination.
                    schedule = squeeze.Toward(squeeze.Draw(0, destinationSeed ?? 0));
                }
                else if (phases != null)
                {
                    schedule = new ZoneSchedule(opening.Sphere, opening.Damage, phases);
                }
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem(
                    ex.ParamName == "pool" ? "zone.destinations" : "zone.phases", ex.Message));
            }

            if (clock == null || policy == null || schedule == null)
            {
                return new TournamentConfigLoadResult(null, problems);
            }

            // With a pool, validate every destination so a bad draw fails at load, not mid-event.
            foreach (ZoneSchedule candidate in squeeze == null
                         ? new[] { schedule }
                         : Enumerable.Select(squeeze.Destinations, squeeze.Toward))
            {
                foreach (ZoneScheduleProblem problem in ZoneScheduleValidator.Validate(candidate, cycleLength).Problems)
                {
                    problems.Add(new TournamentConfigProblem($"zone.phases[{problem.PhaseNumber - 1}]", problem.Message));
                }

                if (problems.Count > 0)
                {
                    break;
                }
            }

            if (problems.Count > 0)
            {
                return new TournamentConfigLoadResult(null, problems);
            }

            return new TournamentConfigLoadResult(
                new TournamentConfig(clock, schedule, policy, colours, ring, prawn, blade, spectator, spawn,
                    telemetry, audit, disconnect, flags, squeeze, destinationSeed ?? 0, ghosts, start,
                    medKit, caster),
                problems);
        }

        /// <summary>The <c>telemetry:</c> section; off when absent or when it names no url.</summary>
        /// <remarks>
        ///     The ingest token is not read here but from <c>LMS_INGEST_TOKEN</c> in the server's
        ///     environment, because this file is distributed to players.
        /// </remarks>
        private static TelemetryPolicy? Telemetry(YamlMappingNode? telemetry, List<TournamentConfigProblem> problems)
        {
            if (telemetry == null)
            {
                return TelemetryPolicy.Off;
            }

            string? url = Scalar(telemetry, "url", "telemetry", problems, required: false);
            TimeSpan interval = Duration(telemetry, "interval", "telemetry", problems, required: false)
                                ?? TelemetryPolicy.Off.Interval;
            TimeSpan timeout = Duration(telemetry, "timeout", "telemetry", problems, required: false)
                               ?? TelemetryPolicy.Off.Timeout;

            // On when absent: naming a url is already the opt-in to being driven by it.
            bool acceptsCommands = Flag(telemetry, "acceptsCommands", "telemetry", problems) ?? true;

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                return new TelemetryPolicy(url, interval, timeout, acceptsCommands);
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem("telemetry." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The <c>audit:</c> section; on with defaults when absent.</summary>
        private static AuditPolicy? Audit(YamlMappingNode? audit, List<TournamentConfigProblem> problems)
        {
            if (audit == null)
            {
                return AuditPolicy.Default;
            }

            TimeSpan interval = Duration(audit, "positionInterval", "audit", problems, required: false)
                                ?? AuditPolicy.Default.PositionInterval;

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                return new AuditPolicy(interval);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("audit." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The <c>disconnect:</c> section; on with defaults when absent.</summary>
        /// <remarks>
        ///     Kills are applied client-side, so without this a disconnect escapes the zone for free.
        ///     A grace longer than a match effectively disables the rule.
        /// </remarks>
        private static DisconnectPolicy? Disconnect(YamlMappingNode? disconnect, List<TournamentConfigProblem> problems)
        {
            if (disconnect == null)
            {
                return DisconnectPolicy.Default;
            }

            TimeSpan grace = Duration(disconnect, "grace", "disconnect", problems, required: false)
                             ?? DisconnectPolicy.Default.Grace;

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                return new DisconnectPolicy(grace);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("disconnect." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>
        ///     How a round begins, or the conservative default (hold, empty kit, vehicles locked).
        /// </summary>
        private static StartPolicy? Start(YamlMappingNode? start, List<TournamentConfigProblem> problems)
        {
            if (start == null)
            {
                return StartPolicy.Default;
            }

            TimeSpan hold = Duration(start, "hold", "start", problems, required: false)
                            ?? StartPolicy.Default.Hold;
            double radius = PositiveNumber(start, "fieldRadius", "start", problems, required: false)
                            ?? StartPolicy.Default.FieldRadius;

            List<string>? kit = Names(start, "kit", problems, "start.kit");
            List<string>? locked = Names(start, "lockedRecipes", problems, "start.lockedRecipes");
            List<string>? recipes = Names(start, "recipes", problems, "start.recipes");
            RoundVitals? vitals = Vitals(start, problems);

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                // `lockedRecipes: []` means withhold nothing, unlike an absent key. With no `recipes`,
                // locks subtract from the full grant; with `recipes: []` (vanilla) there is nothing
                // to subtract from, so locks are an error.
                if (recipes == null && locked != null)
                {
                    recipes = new List<string> { StartPolicy.Everything };
                }

                if (recipes != null && recipes.Count == 0 && locked != null)
                {
                    problems.Add(new TournamentConfigProblem("start.lockedRecipes",
                        "These lock recipes away from a grant the file does not write. An explicit " +
                        "empty `recipes` is the vanilla baseline and there is nothing to subtract " +
                        "from, so write a `recipes` floor for the locks to come out of, or take " +
                        "them out."));
                    return null;
                }

                return new StartPolicy(hold, radius,
                    kit,
                    locked ?? StartPolicy.Default.LockedRecipes,
                    recipes,
                    vitals);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("start." + ex.ParamName, ex.Message));
                return null;
            }
            catch (ArgumentException ex)
            {
                // e.g. `recipes: [all, Knife]`, which is ambiguous.
                problems.Add(new TournamentConfigProblem("start." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The vitals a round starts a contestant with, or null when the file says nothing.</summary>
        /// <remarks>
        ///     Each value is a percent from 1 to 100; zero would be dead or starving on arrival.
        /// </remarks>
        private static RoundVitals? Vitals(YamlMappingNode start, List<TournamentConfigProblem> problems)
        {
            if (start == null || !start.Children.TryGetValue(new YamlScalarNode("vitals"), out YamlNode node))
            {
                return null;
            }

            if (node is not YamlMappingNode vitals)
            {
                problems.Add(new TournamentConfigProblem("start.vitals",
                    "Expected the vitals to be a mapping of health, food and water."));
                return null;
            }

            int? health = Percent(vitals, "health", problems);
            int? food = Percent(vitals, "food", problems);
            int? water = Percent(vitals, "water", problems);

            if (health == null || food == null || water == null)
            {
                return null;
            }

            try
            {
                return new RoundVitals(health.Value, food.Value, water.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Percent already reported the out-of-range key.
                return null;
            }
        }

        /// <summary>One vital as a percent, or null when it is not written or not one.</summary>
        private static int? Percent(YamlMappingNode vitals, string key, List<TournamentConfigProblem> problems)
        {
            if (!vitals.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
            {
                return null;
            }

            string? text = (node as YamlScalarNode)?.Value;
            if (text == null || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed < RoundVitals.Minimum || parsed > RoundVitals.Maximum)
            {
                problems.Add(new TournamentConfigProblem("start.vitals." + key,
                    $"Expected '{key}' to be a whole percentage between {RoundVitals.Minimum} and " +
                    $"{RoundVitals.Maximum}."));
                return null;
            }

            return parsed;
        }

        /// <summary>What casters see, or the defaults when absent. Each field defaults on its own.</summary>
        private static CasterPolicy? Caster(YamlMappingNode? caster, List<TournamentConfigProblem> problems)
        {
            if (caster == null)
            {
                return CasterPolicy.Default;
            }

            bool nameplates = Flag(caster, "nameplates", "caster", problems)
                              ?? CasterPolicy.Default.Nameplates;
            double followDistance = PositiveNumber(caster, "followDistance", "caster", problems,
                                        required: false)
                                    ?? CasterPolicy.Default.FollowDistance;
            // May be negative, to film from below.
            double followHeight = Number(caster, "followHeight", "caster", problems, required: false)
                                  ?? CasterPolicy.Default.FollowHeight;
            double followClosest = PositiveNumber(caster, "followClosest", "caster", problems,
                                       required: false)
                                   ?? CasterPolicy.Default.FollowClosest;
            double followFurthest = PositiveNumber(caster, "followFurthest", "caster", problems,
                                        required: false)
                                    ?? CasterPolicy.Default.FollowFurthest;

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                return new CasterPolicy(nameplates, followDistance, followHeight, followClosest,
                    followFurthest);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("caster." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>How often a first aid kit may be used, or off when absent.</summary>
        private static MedKitPolicy? MedKit(YamlMappingNode? medkit, List<TournamentConfigProblem> problems)
        {
            if (medkit == null)
            {
                return MedKitPolicy.Default;
            }

            TimeSpan cooldown = Duration(medkit, "cooldown", "medkit", problems, required: false)
                                ?? MedKitPolicy.Default.Cooldown;

            if (problems.Count > 0)
            {
                return null;
            }

            try
            {
                return new MedKitPolicy(cooldown);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("medkit." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The <c>spawn:</c> section; stock Nitrox spawning when absent.</summary>
        private static SpawnPolicy? Spawn(YamlMappingNode? spawn, List<TournamentConfigProblem> problems)
        {
            if (spawn == null)
            {
                return SpawnPolicy.Default;
            }

            bool ownPod = Flag(spawn, "ownPod", "spawn", problems) ?? SpawnPolicy.Default.OwnPod;
            double minSeparation = NonNegativeNumber(spawn, "minSeparation", "spawn", problems)
                                   ?? SpawnPolicy.Default.MinSeparation;

            // Sizes the spawn layout; it does not cap the number of players.
            double groupSize = PositiveNumber(spawn, "groupSize", "spawn", problems, required: false)
                               ?? SpawnPolicy.Default.GroupSize;

            // Zero uses the game's own start points; otherwise pods go on a circle of this radius
            // around the origin. The server cannot tell whether a spot is over land.
            double circleRadius = NonNegativeNumber(spawn, "circleRadius", "spawn", problems)
                                  ?? SpawnPolicy.Default.CircleRadius;

            try
            {
                return new SpawnPolicy(ownPod, minSeparation, (int)groupSize, circleRadius);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("spawn." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The <c>spectator:</c> section; each field defaults on its own.</summary>
        private static SpectatorPolicy? Spectator(YamlMappingNode? spectator, List<TournamentConfigProblem> problems)
        {
            if (spectator == null)
            {
                return SpectatorPolicy.Default;
            }

            TimeSpan deathCam = Duration(spectator, "deathCam", "spectator", problems, required: false)
                                ?? SpectatorPolicy.Default.DeathCam;
            bool spectate = Flag(spectator, "spectate", "spectator", problems) ?? SpectatorPolicy.Default.Spectate;
            double flySpeed = PositiveNumber(spectator, "flySpeed", "spectator", problems, required: false)
                              ?? SpectatorPolicy.Default.FlySpeed;

            try
            {
                return new SpectatorPolicy(deathCam, spectate, flySpeed);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                problems.Add(new TournamentConfigProblem("spectator." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>The <c>ring:</c> section; each field defaults on its own.</summary>
        private static RingPolicy? Ring(YamlMappingNode? ring, List<TournamentConfigProblem> problems)
        {
            if (ring == null)
            {
                return RingPolicy.Default;
            }

            StasisFieldLimits fallback = RingPolicy.Default.Limits;

            double minRadius = PositiveNumber(ring, "minRadius", "ring", problems, required: false) ?? fallback.MinRadius;
            double maxRadius = PositiveNumber(ring, "maxRadius", "ring", problems, required: false) ?? fallback.MaxRadius;
            TimeSpan minLifetime = Duration(ring, "minLifetime", "ring", problems, required: false) ?? fallback.MinLifetime;
            TimeSpan maxLifetime = Duration(ring, "maxLifetime", "ring", problems, required: false) ?? fallback.MaxLifetime;
            double margin = NonNegativeNumber(ring, "placementMargin", "ring", problems) ?? RingPolicy.Default.PlacementMargin;
            double energyCost = PositiveNumber(ring, "energyCost", "ring", problems, required: false) ?? RingPolicy.Default.EnergyCost;

            if (problems.Count > 0)
            {
                // Avoid reporting a bad field twice.
                return null;
            }

            StasisFieldLimits? limits = null;
            try
            {
                limits = new StasisFieldLimits(minRadius, maxRadius, minLifetime, maxLifetime);
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem("ring." + ex.ParamName, ex.Message));
                return null;
            }

            try
            {
                return new RingPolicy(limits, margin, energyCost);
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem("ring." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>
        ///     The <c>prawn:</c> section; off unless <c>hurtsPlayers</c> is set. Each field defaults on its own.
        /// </summary>
        private static PrawnPolicy? Prawn(YamlMappingNode? prawn, List<TournamentConfigProblem> problems)
        {
            if (prawn == null)
            {
                return PrawnPolicy.Default;
            }

            PrawnPolicy fallback = PrawnPolicy.Default;

            bool hurtsPlayers = Flag(prawn, "hurtsPlayers", "prawn", problems) ?? fallback.HurtsPlayers;
            double clawDamage = NonNegativeNumber(prawn, "clawDamage", "prawn", problems, "health") ?? fallback.ClawDamage;
            double drillDamage = NonNegativeNumber(prawn, "drillDamage", "prawn", problems, "health") ?? fallback.DrillDamage;
            double reach = PositiveNumber(prawn, "reach", "prawn", problems, required: false) ?? fallback.Reach;
            TimeSpan minInterval = Duration(prawn, "minInterval", "prawn", problems, required: false)
                                   ?? fallback.MinInterval;

            if (problems.Count > 0)
            {
                // Avoid reporting a bad field twice.
                return null;
            }

            try
            {
                return new PrawnPolicy(hurtsPlayers, clawDamage, drillDamage, reach, minInterval);
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem("prawn." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>
        ///     The <c>blade:</c> section, or blades off when absent. Same shape as <see cref="Prawn"/>.
        /// </summary>
        private static BladePolicy? Blade(YamlMappingNode? blade, List<TournamentConfigProblem> problems)
        {
            if (blade == null)
            {
                return BladePolicy.Default;
            }

            BladePolicy fallback = BladePolicy.Default;

            bool hurtsPlayers = Flag(blade, "hurtsPlayers", "blade", problems) ?? fallback.HurtsPlayers;
            double knifeDamage = NonNegativeNumber(blade, "knifeDamage", "blade", problems, "health") ?? fallback.KnifeDamage;
            double heatBladeDamage = NonNegativeNumber(blade, "heatBladeDamage", "blade", problems, "health") ?? fallback.HeatBladeDamage;
            double reach = PositiveNumber(blade, "reach", "blade", problems, required: false) ?? fallback.Reach;
            TimeSpan minInterval = Duration(blade, "minInterval", "blade", problems, required: false)
                                   ?? fallback.MinInterval;

            if (problems.Count > 0)
            {
                // Avoid reporting a bad field twice.
                return null;
            }

            try
            {
                return new BladePolicy(hurtsPlayers, knifeDamage, heatBladeDamage, reach, minInterval,
                    Resistances(blade, problems));
            }
            catch (ArgumentException ex)
            {
                problems.Add(new TournamentConfigProblem("blade." + ex.ParamName, ex.Message));
                return null;
            }
        }

        /// <summary>
        ///     Blade damage multipliers per victim equipment item, or null to keep the default.
        /// </summary>
        /// <remarks>
        ///     Each entry is validated separately and must be in (0, 1].
        /// </remarks>
        private static IReadOnlyDictionary<string, double>? Resistances(YamlMappingNode blade,
            List<TournamentConfigProblem> problems)
        {
            if (!blade.Children.TryGetValue(new YamlScalarNode("resistances"), out YamlNode node))
            {
                return null;
            }

            const string path = "blade.resistances";

            if (!(node is YamlMappingNode resistances))
            {
                problems.Add(new TournamentConfigProblem(path,
                    "This has to be a map of equipment names to damage multipliers."));
                return null;
            }

            Dictionary<string, double> parsed = new Dictionary<string, double>();

            foreach (KeyValuePair<YamlNode, YamlNode> entry in resistances)
            {
                string name = (entry.Key as YamlScalarNode)?.Value ?? string.Empty;

                if (name.Length == 0)
                {
                    problems.Add(new TournamentConfigProblem(path + ".",
                        "This has to name a piece of equipment the way the game does."));
                    continue;
                }

                if (!(entry.Value is YamlScalarNode scalar) || scalar.Value == null
                    || !double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double multiplier))
                {
                    problems.Add(new TournamentConfigProblem(Join(path, name),
                        "This has to be a multiplier on the blade's damage."));
                    continue;
                }

                if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || !(multiplier > 0)
                    || multiplier > 1)
                {
                    problems.Add(new TournamentConfigProblem(Join(path, name),
                        "This has to be a multiplier in (0, 1]. Zero would land a hit for nothing " +
                        "and above one would heal the wearer, and there is no tournament decision " +
                        "behind either."));
                    continue;
                }

                parsed[name] = multiplier;
            }

            return parsed.Count > 0 ? parsed : null;
        }

        /// <summary>The zone colours; each defaults on its own.</summary>
        private static ZoneColours Colours(YamlMappingNode? zone, List<TournamentConfigProblem> problems)
        {
            YamlMappingNode? colours = zone == null ? null : Section(zone, "colours", "zone", problems, required: false);
            if (colours == null)
            {
                return ZoneColours.Default;
            }

            RejectUnknownKeys(colours, "zone.colours", problems, "now", "next");

            return new ZoneColours(
                Colour(colours, "now", "zone.colours", problems) ?? ZoneColours.Default.Now,
                Colour(colours, "next", "zone.colours", problems) ?? ZoneColours.Default.Next);
        }

        /// <remarks>
        ///     Unquoted, <c>now: #FFD65C</c> is an empty value followed by a YAML comment, so the
        ///     message mentions quotes.
        /// </remarks>
        private static ZoneColour? Colour(YamlMappingNode parent, string key, string path, List<TournamentConfigProblem> problems)
        {
            string? written = Scalar(parent, key, path, problems, required: false);
            if (written == null)
            {
                return null;
            }

            if (!ZoneColour.TryParse(written, out ZoneColour colour))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key),
                    $"'{written}' is not a colour. Write it as \"#RRGGBB\" or \"#RRGGBBAA\", " +
                    "in quotes -- unquoted, YAML reads the # as the start of a comment."));
                return null;
            }

            return colour;
        }

        private static (ZoneSphere, double)? Opening(YamlMappingNode? zone, List<TournamentConfigProblem> problems)
        {
            YamlMappingNode? opening = zone == null ? null : Section(zone, "opening", "zone", problems, required: true);
            if (opening == null)
            {
                return null;
            }

            RejectUnknownKeys(opening, "zone.opening", problems, "centre", "radius", "damagePerTick");

            WorldPoint? centre = Centre(opening, "zone.opening", problems);
            double? radius = PositiveNumber(opening, "radius", "zone.opening", problems);
            double? damage = PositiveNumber(opening, "damagePerTick", "zone.opening", problems);

            if (centre == null || radius == null || damage == null)
            {
                return null;
            }

            return (new ZoneSphere(centre.Value, radius.Value), damage.Value);
        }

        private static List<ZonePhase>? Phases(YamlMappingNode? zone, List<TournamentConfigProblem> problems)
        {
            if (zone == null)
            {
                return null;
            }

            if (!zone.Children.TryGetValue(new YamlScalarNode("phases"), out YamlNode node))
            {
                problems.Add(new TournamentConfigProblem("zone.phases", "This field is missing."));
                return null;
            }

            if (!(node is YamlSequenceNode sequence))
            {
                problems.Add(new TournamentConfigProblem("zone.phases", "This has to be a list of phases, widest first."));
                return null;
            }

            List<ZonePhase> phases = new List<ZonePhase>();
            for (int index = 0; index < sequence.Children.Count; index++)
            {
                string path = $"zone.phases[{index}]";
                if (!(sequence.Children[index] is YamlMappingNode phase))
                {
                    problems.Add(new TournamentConfigProblem(path, "This has to be a section: centre, radius, shrinkFor, damagePerTick."));
                    continue;
                }

                RejectUnknownKeys(phase, path, problems, "centre", "radius", "shrinkFor", "damagePerTick");

                WorldPoint? centre = Centre(phase, path, problems);
                double? radius = PositiveNumber(phase, "radius", path, problems);
                TimeSpan? shrinkFor = Duration(phase, "shrinkFor", path, problems);
                double? damage = PositiveNumber(phase, "damagePerTick", path, problems);

                if (centre != null && radius != null && shrinkFor != null && damage != null)
                {
                    // Backstop: keeps Load returning problems if ZonePhase gains a new guard.
                    try
                    {
                        phases.Add(new ZonePhase(phases.Count + 1,
                            new ZoneSphere(centre.Value, radius.Value), shrinkFor.Value, damage.Value));
                    }
                    catch (ArgumentException ex)
                    {
                        problems.Add(new TournamentConfigProblem(
                            ex.ParamName == null ? path : Join(path, ex.ParamName), ex.Message));
                    }
                }
            }

            return phases;
        }

        /// <summary>
        ///     A list of plain names, or null when the key is absent. Blank entries are errors.
        /// </summary>
        private static List<string>? Names(YamlMappingNode root, string field,
            List<TournamentConfigProblem> problems, string? reportedAs = null)
        {
            string reported = reportedAs ?? field;

            if (!root.Children.TryGetValue(new YamlScalarNode(field), out YamlNode node))
            {
                return null;
            }

            if (!(node is YamlSequenceNode sequence))
            {
                problems.Add(new TournamentConfigProblem(reported, "This has to be a list of names."));
                return null;
            }

            List<string> names = new List<string>();
            for (int index = 0; index < sequence.Children.Count; index++)
            {
                string path = $"{reported}[{index}]";

                if (!(sequence.Children[index] is YamlScalarNode scalar)
                    || string.IsNullOrWhiteSpace(scalar.Value))
                {
                    problems.Add(new TournamentConfigProblem(path,
                        "This has to be a player's name, as their game client reports it."));
                    continue;
                }

                names.Add(scalar.Value!.Trim());
            }

            return names;
        }

        /// <summary>
        ///     The pool of places a match may finish, or null when the phases carry their own centres.
        /// </summary>
        private static List<ZoneDestination>? Destinations(YamlMappingNode? zone, List<TournamentConfigProblem> problems)
        {
            if (zone == null || !zone.Children.TryGetValue(new YamlScalarNode("destinations"), out YamlNode node))
            {
                return null;
            }

            if (!(node is YamlSequenceNode sequence))
            {
                problems.Add(new TournamentConfigProblem("zone.destinations",
                    "This has to be a list of places, each with a name and a centre."));
                return null;
            }

            // Refused explicitly: an empty list would otherwise read as "no pool".
            if (sequence.Children.Count == 0)
            {
                problems.Add(new TournamentConfigProblem("zone.destinations",
                    "This is empty. Author a destination, or take the section out and give each phase its own centre."));
                return null;
            }

            List<ZoneDestination> destinations = new List<ZoneDestination>();
            for (int index = 0; index < sequence.Children.Count; index++)
            {
                string path = $"zone.destinations[{index}]";
                if (!(sequence.Children[index] is YamlMappingNode destination))
                {
                    problems.Add(new TournamentConfigProblem(path, "This has to be a section: name, centre."));
                    continue;
                }

                RejectUnknownKeys(destination, path, problems, "name", "centre");

                string? name = Text(destination, "name", path, problems);
                WorldPoint? centre = Centre(destination, path, problems);

                if (name != null && centre != null)
                {
                    destinations.Add(new ZoneDestination(name, centre.Value));
                }
            }

            return destinations;
        }

        /// <summary>
        ///     The phases when a destination pool decides where they go: everything but the centre.
        /// </summary>
        private static List<ZonePhaseShape>? PhaseShapes(YamlMappingNode? zone, List<TournamentConfigProblem> problems)
        {
            if (zone == null)
            {
                return null;
            }

            if (!zone.Children.TryGetValue(new YamlScalarNode("phases"), out YamlNode node))
            {
                problems.Add(new TournamentConfigProblem("zone.phases", "This field is missing."));
                return null;
            }

            if (!(node is YamlSequenceNode sequence))
            {
                problems.Add(new TournamentConfigProblem("zone.phases", "This has to be a list of phases, widest first."));
                return null;
            }

            List<ZonePhaseShape> shapes = new List<ZonePhaseShape>();
            for (int index = 0; index < sequence.Children.Count; index++)
            {
                string path = $"zone.phases[{index}]";
                if (!(sequence.Children[index] is YamlMappingNode phase))
                {
                    problems.Add(new TournamentConfigProblem(path, "This has to be a section: radius, shrinkFor, damagePerTick."));
                    continue;
                }

                // With a pool the centres are derived, so an authored centre is an error, not ignored.
                RejectUnknownKeys(phase, path, problems, "radius", "shrinkFor", "damagePerTick");
                if (phase.Children.ContainsKey(new YamlScalarNode("centre")))
                {
                    problems.Add(new TournamentConfigProblem(Join(path, "centre"),
                        "zone.destinations decides where the phases go, so a phase cannot have its own centre. Take this out, or take the pool out."));
                }

                double? radius = PositiveNumber(phase, "radius", path, problems);
                TimeSpan? shrinkFor = Duration(phase, "shrinkFor", path, problems);
                double? damage = PositiveNumber(phase, "damagePerTick", path, problems);

                if (radius != null && shrinkFor != null && damage != null)
                {
                    // Same backstop as in Phases: ZonePhaseShape carries ZonePhase's guards.
                    try
                    {
                        shapes.Add(new ZonePhaseShape(radius.Value, shrinkFor.Value, damage.Value));
                    }
                    catch (ArgumentException ex)
                    {
                        problems.Add(new TournamentConfigProblem(
                            ex.ParamName == null ? path : Join(path, ex.ParamName), ex.Message));
                    }
                }
            }

            return shapes;
        }

        private static string? Text(YamlMappingNode parent, string key, string path, List<TournamentConfigProblem> problems)
        {
            string field = Join(path, key);

            if (!parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
            {
                problems.Add(new TournamentConfigProblem(field, "This field is missing."));
                return null;
            }

            if (!(node is YamlScalarNode scalar) || string.IsNullOrWhiteSpace(scalar.Value))
            {
                problems.Add(new TournamentConfigProblem(field, "This has to be some text."));
                return null;
            }

            return scalar.Value!.Trim();
        }

        /// <summary>
        ///     Whether the pool is played in the order the file lists it rather than drawn from.
        /// </summary>
        /// <remarks>
        ///     <c>drawn</c> (default) shuffles the pool; <c>listed</c> has group N play entry N.
        ///     A seed beside a listed order is an error rather than ignored.
        /// </remarks>
        private static bool DestinationOrder(YamlMappingNode? zone, bool hasPool, bool hasSeed,
            List<TournamentConfigProblem> problems)
        {
            string? order = Scalar(zone, "destinationOrder", "zone", problems, required: false)?.Trim();
            if (order == null || order == "drawn")
            {
                return false;
            }

            if (order != "listed")
            {
                problems.Add(new TournamentConfigProblem("zone.destinationOrder",
                    $"This is either drawn or listed, not \"{order}\"."));
                return false;
            }

            if (!hasPool)
            {
                problems.Add(new TournamentConfigProblem("zone.destinationOrder",
                    "A listed order needs destinations to list. Author zone.destinations, or leave this out."));
            }
            else if (hasSeed)
            {
                problems.Add(new TournamentConfigProblem("zone.destinationOrder",
                    "A listed order plays the file top to bottom, so a destinationSeed has nothing to shuffle. Remove one of them."));
            }

            return true;
        }

        /// <summary>An optional whole number, for a seed. Negative is fine; a seed is not a quantity.</summary>
        private static int? WholeNumber(YamlMappingNode? parent, string key, string path, List<TournamentConfigProblem> problems)
        {
            if (parent == null || !parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
            {
                return null;
            }

            if (!(node is YamlScalarNode scalar) || scalar.Value == null
                || !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), "This has to be a whole number."));
                return null;
            }

            return value;
        }

        /// <summary>
        ///     A <c>centre: [x, y, z]</c> in metres. Y is height, so underwater is negative.
        /// </summary>
        private static WorldPoint? Centre(YamlMappingNode parent, string path, List<TournamentConfigProblem> problems)
        {
            string field = Join(path, "centre");

            if (!parent.Children.TryGetValue(new YamlScalarNode("centre"), out YamlNode node))
            {
                problems.Add(new TournamentConfigProblem(field, "This field is missing."));
                return null;
            }

            if (!(node is YamlSequenceNode sequence) || sequence.Children.Count != 3)
            {
                problems.Add(new TournamentConfigProblem(field,
                    "This has to be three numbers: [x, y, z] in metres, y negative for depth."));
                return null;
            }

            double[] parts = new double[3];
            for (int index = 0; index < 3; index++)
            {
                if (!(sequence.Children[index] is YamlScalarNode scalar) || scalar.Value == null
                    || !double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parts[index]))
                {
                    problems.Add(new TournamentConfigProblem($"{field}[{index}]", "This has to be a number in metres."));
                    return null;
                }
            }

            return new WorldPoint(parts[0], parts[1], parts[2]);
        }

        private static YamlMappingNode? ParseRoot(string yaml, List<TournamentConfigProblem> problems)
        {
            if (string.IsNullOrWhiteSpace(yaml))
            {
                problems.Add(new TournamentConfigProblem(string.Empty, "The config file is empty."));
                return null;
            }

            YamlStream stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(yaml));
            }
            catch (YamlException ex)
            {
                problems.Add(new TournamentConfigProblem(string.Empty, $"This is not valid YAML: {ex.Message}"));
                return null;
            }

            if (stream.Documents.Count == 0)
            {
                problems.Add(new TournamentConfigProblem(string.Empty, "The config file is empty."));
                return null;
            }

            if (!(stream.Documents[0].RootNode is YamlMappingNode root))
            {
                problems.Add(new TournamentConfigProblem(string.Empty, "The config file has to be a mapping of sections: match, zone, flags."));
                return null;
            }

            return root;
        }

        private static void RejectUnknownKeys(YamlMappingNode mapping, string path, List<TournamentConfigProblem> problems, params string[] known)
        {
            foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping)
            {
                string key = (entry.Key as YamlScalarNode)?.Value ?? string.Empty;
                if (Array.IndexOf(known, key) < 0)
                {
                    problems.Add(new TournamentConfigProblem(Join(path, key),
                        $"Unknown field. This section takes: {string.Join(", ", known)}."));
                }
            }
        }

        private static YamlMappingNode? Section(YamlMappingNode parent, string key, string path, List<TournamentConfigProblem> problems, bool required)
        {
            if (!parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
            {
                if (required)
                {
                    problems.Add(new TournamentConfigProblem(Join(path, key), "This section is missing."));
                }

                return null;
            }

            if (!(node is YamlMappingNode mapping))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), "This has to be a section of fields."));
                return null;
            }

            return mapping;
        }

        private static string? Scalar(YamlMappingNode? parent, string key, string path, List<TournamentConfigProblem> problems, bool required = true)
        {
            if (parent == null)
            {
                return null;
            }

            if (!parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode node))
            {
                if (required)
                {
                    problems.Add(new TournamentConfigProblem(Join(path, key), "This field is missing."));
                }

                return null;
            }

            if (!(node is YamlScalarNode scalar) || scalar.Value == null)
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), "This has to be a single value."));
                return null;
            }

            return scalar.Value;
        }

        private static TimeSpan? Duration(YamlMappingNode? parent, string key, string path, List<TournamentConfigProblem> problems, bool required = true)
        {
            string? written = Scalar(parent, key, path, problems, required);
            if (written == null)
            {
                return null;
            }

            TimeSpan? parsed = ParseDuration(written.Trim());
            if (parsed == null)
            {
                problems.Add(new TournamentConfigProblem(Join(path, key),
                    $"'{written}' is not a length of time. Write it as 800ms, 90s, 5m, 1h or 00:05:00."));
            }

            // Refuse negatives here for every duration, so Load reports them instead of throwing.
            if (parsed < TimeSpan.Zero)
            {
                problems.Add(new TournamentConfigProblem(Join(path, key),
                    $"'{written}' is a negative length of time, and nothing in a tournament runs backwards."));
                return null;
            }

            return parsed;
        }

        /// <summary>Parses <c>800ms</c>, <c>90s</c>, <c>5m</c>, <c>1h</c> or <c>00:05:00</c>.</summary>
        /// <remarks>
        ///     <c>ms</c> is checked first so <c>800ms</c> is not read as minutes. It lets sub-second
        ///     values avoid a locale-sensitive decimal point.
        /// </remarks>
        private static TimeSpan? ParseDuration(string written)
        {
            if (written.Length > 2 && written.EndsWith("ms", StringComparison.Ordinal)
                && double.TryParse(written.Substring(0, written.Length - 2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double milliseconds))
            {
                return Length(milliseconds, 0.001);
            }

            if (written.Length > 1)
            {
                char unit = written[written.Length - 1];
                string number = written.Substring(0, written.Length - 1);
                if ((unit == 's' || unit == 'm' || unit == 'h')
                    && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    switch (unit)
                    {
                        case 's': return Length(value, 1);
                        case 'm': return Length(value, 60);
                        default: return Length(value, 3600);
                    }
                }
            }

            if (TimeSpan.TryParse(written, CultureInfo.InvariantCulture, out TimeSpan clockForm))
            {
                return clockForm;
            }

            return null;
        }

        /// <summary>
        ///     A number of units as a length of time, or null when it is not one.
        /// </summary>
        /// <remarks>
        ///     <c>TimeSpan.From*</c> throws on NaN and out-of-range values, which
        ///     <c>NumberStyles.Float</c> can produce (<c>NaNs</c>, <c>1e30s</c>); this returns null instead.
        /// </remarks>
        private static TimeSpan? Length(double value, double secondsPerUnit)
        {
            // `double.IsFinite` is not available on .NET Standard 2.0.
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }

            double seconds = value * secondsPerUnit;
            if (seconds < TimeSpan.MinValue.TotalSeconds || seconds > TimeSpan.MaxValue.TotalSeconds)
            {
                return null;
            }

            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>Like <see cref="PositiveNumber"/>, but zero is allowed. <c>unit</c> is named in the message.</summary>
        private static double? NonNegativeNumber(YamlMappingNode? parent, string key, string path,
            List<TournamentConfigProblem> problems, string unit = "metres")
        {
            string? written = Scalar(parent, key, path, problems, required: false);
            if (written == null)
            {
                return null;
            }

            if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !(value >= 0))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), $"'{written}' has to be a number of {unit}, zero or more."));
                return null;
            }

            return value;
        }

        private static double? PositiveNumber(YamlMappingNode? parent, string key, string path, List<TournamentConfigProblem> problems, bool required = true)
        {
            string? written = Scalar(parent, key, path, problems, required);
            if (written == null)
            {
                return null;
            }

            if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !(value > 0))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), $"'{written}' has to be a number greater than zero."));
                return null;
            }

            return value;
        }

        /// <summary>
        ///     Like <see cref="PositiveNumber"/>, but the number may be zero or negative.
        /// </summary>
        private static double? Number(YamlMappingNode? parent, string key, string path,
            List<TournamentConfigProblem> problems, bool required = true)
        {
            string? written = Scalar(parent, key, path, problems, required);
            if (written == null)
            {
                return null;
            }

            if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), $"'{written}' has to be a number."));
                return null;
            }

            return value;
        }

        private static bool? Flag(YamlMappingNode? parent, string key, string path, List<TournamentConfigProblem> problems)
        {
            string? written = Scalar(parent, key, path, problems, required: false);
            if (written == null)
            {
                return null;
            }

            if (!bool.TryParse(written, out bool value))
            {
                problems.Add(new TournamentConfigProblem(Join(path, key), $"'{written}' has to be true or false."));
                return null;
            }

            return value;
        }

        private static string Join(string path, string key) => path.Length == 0 ? key : path + "." + key;
    }
}
