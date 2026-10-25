using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Audit;
using Lms.Core.Combat;
using Lms.Core.Match;
using Lms.Core.Membranes;
using Lms.Core.Spawning;
using Lms.Core.Spectating;
using Lms.Core.Telemetry;
using Lms.Core.Zones;

namespace Lms.Core.Configuration
{
    /// <summary>
    ///     The tournament rules, parsed and validated, in the shapes the rest of Core already uses.
    /// </summary>
    public sealed class TournamentConfig
    {
        internal TournamentConfig(MatchClockSettings clock, ZoneSchedule zone, ZonePolicy zonePolicy,
            ZoneColours colours, RingPolicy ring, PrawnPolicy prawn, BladePolicy blade,
            SpectatorPolicy spectator,
            SpawnPolicy spawn, TelemetryPolicy telemetry, AuditPolicy audit,
            DisconnectPolicy disconnect, TournamentFlags flags,
            ZoneSqueeze? squeeze = null, int destinationSeed = 0,
            IReadOnlyList<string>? ghosts = null, StartPolicy? start = null,
            MedKitPolicy? medKit = null,
            CasterPolicy? caster = null)
        {
            Clock = clock;
            Zone = zone;
            ZonePolicy = zonePolicy;
            Colours = colours;
            Ring = ring;
            Prawn = prawn;
            Blade = blade;
            Spectator = spectator;
            Spawn = spawn;
            Telemetry = telemetry;
            Audit = audit;
            Disconnect = disconnect;
            Flags = flags;
            Squeeze = squeeze;
            DestinationSeed = destinationSeed;
            Ghosts = new ReadOnlyCollection<string>(
                ghosts == null ? new List<string>() : new List<string>(ghosts));
            Start = start ?? StartPolicy.Default;
            MedKit = medKit ?? MedKitPolicy.Default;
            Caster = caster ?? CasterPolicy.Default;
        }

        public MatchClockSettings Clock { get; }

        /// <summary>The authored squeeze: an opening sphere and a phase per cycle.</summary>
        public ZoneSchedule Zone { get; }

        /// <summary>
        ///     The pool each group's squeeze is drawn from, or null when the phases carry their own
        ///     centres and every group finishes in the same place.
        /// </summary>
        /// <remarks>
        ///     <see cref="Zone"/> is group one's squeeze; every group draws its own from here.
        /// </remarks>
        public ZoneSqueeze? Squeeze { get; }

        /// <summary>The tournament's seed, which fixes the order the pool is drawn in.</summary>
        /// <remarks>
        ///     Written to the audit log so a disputed match can be reconstructed without the order
        ///     being knowable beforehand.
        /// </remarks>
        public int DestinationSeed { get; }

        /// <summary>
        ///     The squeeze a group runs: the pool's draw when there is one, otherwise the authored
        ///     schedule.
        /// </summary>
        public ZoneSchedule SqueezeFor(int group) =>
            Squeeze == null ? Zone : Squeeze.Toward(Squeeze.Draw(group - 1, DestinationSeed));

        /// <summary>Where this group finishes, or null when the file authored the centres.</summary>
        public ZoneDestination? DestinationFor(int group) =>
            Squeeze?.Draw(group - 1, DestinationSeed);

        /// <summary>How often being outside the wall bites, and how fast the screen says so.</summary>
        public ZonePolicy ZonePolicy { get; }

        /// <summary>The colours the wall and the next wall are drawn in. Cosmetic only.</summary>
        public ZoneColours Colours { get; }

        /// <summary>
        ///     The boxing ring: stasis field size and lifetime, and how far inside the wall a player
        ///     is put back. Applied to the game's own field, so the visible bubble is the one that holds.
        /// </summary>
        public RingPolicy Ring { get; }

        /// <summary>
        ///     What a prawn suit's arms do to a player: whether they hurt at all, what each arm costs,
        ///     and how far and how often the server believes a swing.
        /// </summary>
        /// <remarks>
        ///     Server-side only: the attacker's client reports the swing and applies nothing. Off when
        ///     absent, matching Nitrox, which keeps a remote player's <c>LiveMixin</c> invincible.
        /// </remarks>
        public PrawnPolicy Prawn { get; }

        /// <summary>
        ///     What a knife and a heat blade do to a player: whether they hurt at all, what each blade
        ///     costs, and how far and how often the server believes a swing.
        /// </summary>
        /// <remarks>
        ///     Server-side only, like <see cref="Prawn"/>. Nitrox's own knife path must also be
        ///     disabled by the PvP guard, or a swing is paid twice. Off when absent.
        /// </remarks>
        public BladePolicy Blade { get; }

        /// <summary>
        ///     What happens to an eliminated player: how long the death view holds, and whether they
        ///     fly afterwards or are seen out.
        /// </summary>
        /// <remarks>Client-side only.</remarks>
        public SpectatorPolicy Spectator { get; }

        /// <summary>Where players start a match: a pod each, and how far apart.</summary>
        /// <remarks>Server-side only: escape pods are assigned by the server.</remarks>
        public SpawnPolicy Spawn { get; }

        /// <summary>Where the casting booth is, and how often it is pushed to.</summary>
        /// <remarks>Server-side only: the pump needs the roster, the zone and the audit log.</remarks>
        public TelemetryPolicy Telemetry { get; }

        /// <summary>What the tournament records on a cadence rather than per event.</summary>
        /// <remarks>Server-side only: position samples are taken where the roster is.</remarks>
        public AuditPolicy Audit { get; }

        /// <summary>How long a disconnect is forgiven before the wall starts counting.</summary>
        /// <remarks>Server-side only: absences are measured where the roster and audit log are.</remarks>
        public DisconnectPolicy Disconnect { get; }

        public TournamentFlags Flags { get; }

        /// <summary>Players present on the server but not in the match.</summary>
        /// <remarks>
        ///     Server-side only: a client that could mark itself a ghost could make itself immortal.
        ///     <c>lms ghost &lt;player&gt;</c> covers anyone not listed here.
        /// </remarks>
        public IReadOnlyList<string> Ghosts { get; }

        /// <summary>How a round begins: the kit, the recipe grant and the hold.</summary>
        /// <remarks>
        ///     Separate from <see cref="Ring"/> because a stasis field's radius and lifetime share one
        ///     lerp, and the opening hold must not resize every combat ring.
        /// </remarks>
        public StartPolicy Start { get; }

        /// <summary>How often a first aid kit may be used.</summary>
        /// <remarks>Client-side, where healing happens. Off when the section is absent.</remarks>
        public MedKitPolicy MedKit { get; }

        /// <summary>What the people filming the match see.</summary>
        /// <remarks>
        ///     Client-side, where nameplates and the follow camera are drawn. On when the section is
        ///     absent; the caster feed only ever goes to a ghost.
        /// </remarks>
        public CasterPolicy Caster { get; }
    }
}
