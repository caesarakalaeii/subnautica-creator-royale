using System;
using System.Collections.Generic;
using Lms.Core.Audit;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Something that happened to a player, for the audit log.
    /// </summary>
    /// <remarks>
    ///     Joins, departures and eliminations are derived by comparing roster snapshots. Other kinds are
    ///     announced by the client and also carry the client's match time and <see cref="Generation"/>,
    ///     so a late batch is not filed under a newer match.
    /// </remarks>
    public readonly struct RosterChange
    {
        internal RosterChange(AuditRecordKind kind, string player, WorldPoint? position,
            string? biome, double? health, TimeSpan? elapsed = null, int? generation = null,
            double? amount = null, int? phase = null, string? detail = null)
        {
            Kind = kind;
            Player = player;
            Position = position;
            Biome = biome;
            Health = health;
            Elapsed = elapsed;
            Generation = generation;
            Amount = amount;
            Phase = phase;
            Detail = detail;
        }

        /// <summary>What happened, in the log's own word.</summary>
        public AuditRecordKind Kind { get; }

        public string Player { get; }

        /// <summary>Where they were. For a departure that is the last place anybody saw them.</summary>
        public WorldPoint? Position { get; }

        /// <summary>What the client called the place, or null if it never said.</summary>
        public string? Biome { get; }

        public double? Health { get; }

        /// <summary>
        ///     Match time the client saw an announced event at, or null for a derived one (the host
        ///     stamps the current match time).
        /// </summary>
        public TimeSpan? Elapsed { get; }

        /// <summary>Which attempt at the match the client was in, or null for a derived change.</summary>
        public int? Generation { get; }

        /// <summary>What it cost: the damage of one tick.</summary>
        public double? Amount { get; }

        public int? Phase { get; }

        /// <summary>A short token the kind gives meaning to.</summary>
        public string? Detail { get; }

        public override string ToString() => $"{Kind} {Player}";
    }

    /// <summary>
    ///     One look at the world: a row per player, and what changed since the last look.
    /// </summary>
    /// <remarks>
    ///     Produced together so the alive count and the eliminations in one push agree.
    /// </remarks>
    public readonly struct RosterUpdate
    {
        internal RosterUpdate(IReadOnlyList<PlayerTelemetry> rows, IReadOnlyList<RosterChange> changes)
        {
            Rows = rows;
            Changes = changes;
        }

        public IReadOnlyList<PlayerTelemetry> Rows { get; }

        /// <summary>
        ///     Everything to record, in order: departures, then per player their arrival, announced
        ///     events and elimination.
        /// </summary>
        public IReadOnlyList<RosterChange> Changes { get; }
    }
}
