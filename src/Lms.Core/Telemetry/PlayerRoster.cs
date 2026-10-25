using System;
using System.Collections.Generic;
using Lms.Core.Audit;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Merges the server's player readings with each client's self-report into one row per player,
    ///     and derives what changed since the last look (joins, departures, eliminations, client events).
    /// </summary>
    /// <remarks>
    ///     A player counts as out if either Nitrox or their own client says so, or the server has
    ///     recorded them out. Nitrox ids are reused, so a departing player's report and events are
    ///     dropped and departures are recorded before arrivals. A new match generation re-announces
    ///     every arrival, since <c>lms reset</c> involves no reconnects. Client events get the name
    ///     from the server's own reading, never from the client. Locked: reports arrive on the packet
    ///     pump while pushes are built on the match timer.
    /// </remarks>
    public sealed class PlayerRoster
    {
        private static readonly RosterChange[] Nothing = new RosterChange[0];

        private readonly object gate = new object();
        private readonly Dictionary<ushort, PlayerReport> reports = new Dictionary<ushort, PlayerReport>();

        /// <summary>
        ///     Events each client has announced and this roster has not yet handed over. Appended to,
        ///     unlike <see cref="reports"/>, because events do not supersede each other.
        /// </summary>
        private readonly Dictionary<ushort, Queue<RosterChange>> announced =
            new Dictionary<ushort, Queue<RosterChange>>();

        /// <summary>How many announced events are held per player before the oldest falls off.</summary>
        private readonly int eventsPerPlayer;

        /// <summary>The last row written for each player, which a departure is reported from.</summary>
        private readonly Dictionary<ushort, PlayerTelemetry> seen = new Dictionary<ushort, PlayerTelemetry>();

        /// <summary>
        ///     Who has been recorded as out and is still out, so an elimination is written once per
        ///     occurrence. Cleared when the player is alive again (reset or next group).
        /// </summary>
        private readonly HashSet<ushort> gone = new HashSet<ushort>();

        /// <summary>
        ///     Names the server has said are out, with the match generation each verdict belongs to.
        /// </summary>
        /// <remarks>
        ///     Keyed by name (ordinal, like <c>MatchAudit</c>) because Nitrox ids change on reconnect.
        ///     The generation is stored per entry because a verdict can arrive on the packet pump before
        ///     the match tick sees the new generation.
        /// </remarks>
        private readonly Dictionary<string, int> told = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        ///     The match generation arrivals were last announced for. <c>lms reset</c> has no reconnects,
        ///     so without re-announcing, <c>MatchLedger</c> would not know who was in the new match.
        /// </summary>
        private int announcedFor = -1;

        /// <param name="eventsPerPlayer">
        ///     How many announced events are held per player before the oldest is dropped, bounding memory.
        /// </param>
        public PlayerRoster(int eventsPerPlayer = DefaultEventsPerPlayer)
        {
            if (eventsPerPlayer < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(eventsPerPlayer), eventsPerPlayer,
                    "A queue that holds nothing loses everything.");
            }

            this.eventsPerPlayer = eventsPerPlayer;
        }

        /// <summary>
        ///     Twice the client's own queue capacity.
        /// </summary>
        public const int DefaultEventsPerPlayer = 400;

        /// <summary>
        ///     Stores one client's self-report, replacing the previous one. <paramref name="player"/>
        ///     comes from the authenticated connection, never the message.
        /// </summary>
        public void Reported(ushort player, PlayerReport report)
        {
            lock (gate)
            {
                reports[player] = report ?? PlayerReport.Empty;
            }
        }

        /// <summary>
        ///     Queues one client's events to be recorded on the next <see cref="Update"/>.
        ///     <paramref name="player"/> comes from the authenticated connection.
        /// </summary>
        /// <remarks>
        ///     Held because this runs on the packet pump while the log is written on the match tick, and
        ///     the player's name comes from the server's reading on that tick.
        ///     <see cref="PlayerEventBatch.Lost"/> is logged by the server, not recorded here.
        /// </remarks>
        public void Reported(ushort player, PlayerEventBatch batch)
        {
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            lock (gate)
            {
                if (!announced.TryGetValue(player, out Queue<RosterChange> queue))
                {
                    queue = new Queue<RosterChange>();
                    announced[player] = queue;
                }

                foreach (PlayerEvent happened in batch.Events)
                {
                    if (queue.Count == eventsPerPlayer)
                    {
                        queue.Dequeue();
                    }

                    // Name is filled in on the tick from the server's own reading.
                    queue.Enqueue(new RosterChange(happened.Kind, string.Empty, happened.Position,
                        happened.Biome, happened.Health, happened.Elapsed, batch.Generation,
                        happened.Amount, happened.Phase, happened.Detail));
                }
            }
        }

        /// <summary>
        ///     Records the server's verdict that this player is already out of the match.
        /// </summary>
        /// <param name="player">The name the audit log identifies them by.</param>
        /// <param name="generation">
        ///     The match generation the verdict belongs to; a reset or new group invalidates it.
        /// </param>
        /// <remarks>
        ///     A reconnecting client truthfully reports itself not eliminated, so without this a player
        ///     could rejoin to undo their elimination. No second elimination record is written, as that
        ///     would double-count in the standings.
        /// </remarks>
        public void Out(string player, int generation)
        {
            if (string.IsNullOrWhiteSpace(player))
            {
                throw new ArgumentException(
                    "A verdict is about a player, so it needs the name the log knows them by.",
                    nameof(player));
            }

            lock (gate)
            {
                told[player] = generation;
            }
        }

        /// <summary>
        ///     One row per player in the world, with what they have said about themselves folded in,
        ///     and everything that changed since the last look.
        /// </summary>
        /// <param name="readings">Everybody the server can see, which is everybody connected.</param>
        /// <param name="wall">
        ///     The zone this instant; inside/outside is computed here because the server owns the wall.
        /// </param>
        /// <param name="generation">
        ///     The match generation, so a replaced match gets its own arrivals. Zero when there is no match.
        /// </param>
        public RosterUpdate Update(IReadOnlyList<PlayerReading> readings, ZoneSphere wall,
            int generation = 0)
        {
            List<PlayerTelemetry> rows = new List<PlayerTelemetry>(readings.Count);
            List<RosterChange>? changes = null;

            lock (gate)
            {
                // Detected before departures so a disconnect on the same tick as a reset is still recorded.
                bool replaced = generation != announcedFor;
                announcedFor = generation;

                if (replaced && told.Count > 0)
                {
                    // Housekeeping only; the per-entry generation check below is the actual rule.
                    Forget(generation);
                }

                Departed(readings, ref changes);

                foreach (PlayerReading reading in readings)
                {
                    if (!reports.TryGetValue(reading.Id, out PlayerReport report))
                    {
                        report = PlayerReport.Empty;
                    }

                    // The server's verdict overrides a reconnecting client's own report.
                    bool alreadyOut = told.TryGetValue(reading.Name, out int fence)
                                      && fence == generation;

                    PlayerTelemetry row = Row(reading, report, wall, alreadyOut);
                    rows.Add(row);

                    if (replaced || !seen.ContainsKey(reading.Id))
                    {
                        Joined(ref changes, row);
                    }

                    // Order per player: joined, then announced events, then eliminated.
                    Announced(ref changes, reading.Id, row.Name);

                    if (row.Alive)
                    {
                        // Back in the match (reset or next group).
                        gone.Remove(reading.Id);
                    }
                    else if (gone.Add(reading.Id) && !alreadyOut)
                    {
                        // Skipped for server verdicts: that elimination is already in the log.
                        Add(ref changes, AuditRecordKind.PlayerEliminated, row);
                    }

                    seen[reading.Id] = row;
                }
            }

            return new RosterUpdate(rows, changes ?? (IReadOnlyList<RosterChange>)Nothing);
        }

        /// <summary>
        ///     How many of these rows are still in the match, counted from the rows so the two cannot disagree.
        /// </summary>
        public static int Alive(IReadOnlyList<PlayerTelemetry> rows)
        {
            int alive = 0;

            foreach (PlayerTelemetry row in rows)
            {
                if (row.Alive)
                {
                    alive++;
                }
            }

            return alive;
        }

        /// <summary>
        ///     Builds one row; <paramref name="alreadyOut"/> (the server's verdict) overrides the client's report.
        /// </summary>
        private static PlayerTelemetry Row(PlayerReading reading, PlayerReport report, ZoneSphere wall,
            bool alreadyOut) =>
            new PlayerTelemetry(reading.Name, reading.Alive && !report.Eliminated && !alreadyOut,
                reading.Position, report.Biome,
                reading.Health, reading.Oxygen, reading.Food, reading.Water,
                report.Vehicle, report.HeldItem,
                wall.Contains(reading.Position), wall.DistanceOutside(reading.Position),
                report.Inventory, report.Equipment, report.Rifle, report.RifleCharge,
                report.Frozen);

        /// <summary>Drops verdicts from other match generations.</summary>
        private void Forget(int generation)
        {
            List<string>? stale = null;

            foreach (KeyValuePair<string, int> verdict in told)
            {
                if (verdict.Value != generation)
                {
                    (stale ?? (stale = new List<string>())).Add(verdict.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (string name in stale)
            {
                told.Remove(name);
            }
        }

        /// <summary>
        ///     Records departures and drops all state for players no longer in the world; a rejoin is a
        ///     fresh arrival.
        /// </summary>
        private void Departed(IReadOnlyList<PlayerReading> readings, ref List<RosterChange>? changes)
        {
            if (seen.Count == 0)
            {
                return;
            }

            HashSet<ushort> present = new HashSet<ushort>();
            foreach (PlayerReading reading in readings)
            {
                present.Add(reading.Id);
            }

            List<ushort>? left = null;
            foreach (KeyValuePair<ushort, PlayerTelemetry> held in seen)
            {
                if (!present.Contains(held.Key))
                {
                    (left ?? (left = new List<ushort>())).Add(held.Key);
                }
            }

            if (left == null)
            {
                return;
            }

            foreach (ushort id in left)
            {
                Add(ref changes, AuditRecordKind.PlayerLeft, seen[id]);

                seen.Remove(id);
                reports.Remove(id);
                gone.Remove(id);

                // Dropped unrecorded: the id may be reused by somebody else.
                announced.Remove(id);
            }
        }

        /// <summary>
        ///     Emits a player's queued events, stamping the name from the server's own reading.
        /// </summary>
        private void Announced(ref List<RosterChange>? changes, ushort id, string name)
        {
            if (!announced.TryGetValue(id, out Queue<RosterChange> queue))
            {
                return;
            }

            while (queue.Count > 0)
            {
                RosterChange nameless = queue.Dequeue();

                (changes ?? (changes = new List<RosterChange>())).Add(
                    new RosterChange(nameless.Kind, name, nameless.Position, nameless.Biome,
                        nameless.Health, nameless.Elapsed, nameless.Generation, nameless.Amount,
                        nameless.Phase, nameless.Detail));
            }

            announced.Remove(id);
        }

        private static void Add(ref List<RosterChange>? changes, AuditRecordKind kind, PlayerTelemetry row)
        {
            (changes ?? (changes = new List<RosterChange>())).Add(
                new RosterChange(kind, row.Name, row.Position, row.Biome, row.Health));
        }

        /// <summary>
        ///     Records a join without a position: a new player's position still reads as the origin,
        ///     which is a real place in the world.
        /// </summary>
        private static void Joined(ref List<RosterChange>? changes, PlayerTelemetry row)
        {
            (changes ?? (changes = new List<RosterChange>())).Add(
                new RosterChange(AuditRecordKind.PlayerJoined, row.Name, null, null, null));
        }
    }
}
