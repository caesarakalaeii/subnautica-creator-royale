using System;
using Lms.Core.Audit;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     One discrete event a client saw happen to its own player (bite, warning, ring, cage), which a
    ///     state snapshot like <see cref="PlayerReport"/> cannot express.
    /// </summary>
    /// <remarks>
    ///     Timestamped in match time from the shared world clock, not the client's wall clock.
    ///     <see cref="CanBeReported"/> is a trust boundary: a client may never report eliminations,
    ///     kills or match lifecycle.
    /// </remarks>
    public readonly struct PlayerEvent
    {
        /// <param name="kind">What happened. One of <see cref="CanBeReported"/>.</param>
        /// <param name="elapsed">How far into the match, by the shared world clock.</param>
        /// <param name="position">Where the player was.</param>
        /// <param name="biome">What the client called the place.</param>
        /// <param name="amount">The damage of one tick.</param>
        /// <param name="health">What was left afterwards.</param>
        /// <param name="phase">Which phase, counting from one, or null before the first.</param>
        /// <param name="detail">A short token the kind gives meaning to.</param>
        public PlayerEvent(AuditRecordKind kind, TimeSpan elapsed, WorldPoint? position = null,
            string? biome = null, double? amount = null, double? health = null, int? phase = null,
            string? detail = null)
        {
            if (!CanBeReported(kind))
            {
                throw new ArgumentException(
                    $"{kind} is not something a client says about itself. See PlayerEvent.CanBeReported.",
                    nameof(kind));
            }

            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed,
                    "Nothing happens before a match starts.");
            }

            Kind = kind;
            Elapsed = elapsed;
            Position = position;
            Biome = biome;
            Amount = amount;
            Health = health;
            Phase = phase;
            Detail = detail;
        }

        public AuditRecordKind Kind { get; }

        /// <summary>Match time this happened at, which a replay orders by.</summary>
        public TimeSpan Elapsed { get; }

        public WorldPoint? Position { get; }

        public string? Biome { get; }

        /// <summary>The damage of one tick, never a run of them added up.</summary>
        public double? Amount { get; }

        public double? Health { get; }

        public int? Phase { get; }

        public string? Detail { get; }

        /// <summary>
        ///     Whether a client is allowed to report this kind: only events computed locally on that
        ///     client (zone warning and bite, ring enforcement, refused beacon, precache finished).
        /// </summary>
        /// <remarks>
        ///     <c>PlayerEliminated</c> is derived from the roster, <c>KillAttributed</c> is written by an
        ///     operator, and <c>PlayerPosition</c> is sampled by the server.
        /// </remarks>
        public static bool CanBeReported(AuditRecordKind kind)
        {
            switch (kind)
            {
                case AuditRecordKind.ZoneWarned:
                case AuditRecordKind.ZoneBite:
                case AuditRecordKind.RingFired:
                case AuditRecordKind.BeaconRefused:
                case AuditRecordKind.PlayerReady:
                case AuditRecordKind.PlayerCaged:
                case AuditRecordKind.PlayerReleased:
                    return true;

                default:
                    return false;
            }
        }

        public override string ToString() => $"{Kind} at {Elapsed}";
    }
}
