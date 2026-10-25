using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>
    ///     One phase's geometry, with no clock in it: where the wall starts, where it is going, how
    ///     long it takes to get there and what standing outside it costs.
    /// </summary>
    /// <remarks>
    ///     The server broadcasts this rather than the schedule; each client folds the shared world
    ///     clock into <see cref="At"/> and gets the same sphere.
    /// </remarks>
    public readonly struct ZoneShrink : IEquatable<ZoneShrink>
    {
        internal ZoneShrink(ZoneSphere from, ZoneSphere target, TimeSpan shrinkFor, int phaseNumber,
            double damagePerTick, ZoneSphere? nextTarget)
        {
            From = from;
            Target = target;
            ShrinkFor = shrinkFor;
            PhaseNumber = phaseNumber;
            DamagePerTick = damagePerTick;
            NextTarget = nextTarget;
        }

        /// <summary>Where the wall stood when this phase began.</summary>
        public ZoneSphere From { get; }

        /// <summary>Where it holds once this phase has finished closing.</summary>
        public ZoneSphere Target { get; }

        /// <summary>
        ///     How long the wall takes to travel between the two. Zero before the first phase, where
        ///     the wall stands still.
        /// </summary>
        public TimeSpan ShrinkFor { get; }

        /// <summary>Which phase this is, counting from one. Zero before the first one fires.</summary>
        public int PhaseNumber { get; }

        /// <summary>What one tick outside the wall costs during this phase.</summary>
        public double DamagePerTick { get; }

        /// <summary>
        ///     Where the phase after this one is heading, or null in the last one. Drawn as the ghost
        ///     sphere so players are warned before the wall moves.
        /// </summary>
        public ZoneSphere? NextTarget { get; }

        /// <summary>Where the wall is, <paramref name="sincePhaseStart"/> into this phase.</summary>
        /// <remarks>
        ///     Clamped: before the start it holds <see cref="From"/>, after the window it holds
        ///     <see cref="Target"/> (the usual case for most of a cycle).
        /// </remarks>
        public ZoneState At(TimeSpan sincePhaseStart)
        {
            if (sincePhaseStart < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(sincePhaseStart), sincePhaseStart,
                    "Match time does not run backwards.");
            }

            double travelled = ShrinkFor <= TimeSpan.Zero
                ? 1
                : sincePhaseStart.TotalSeconds / ShrinkFor.TotalSeconds;

            return new ZoneState(
                ZoneSphere.Between(From, Target, travelled),
                NextTarget,
                PhaseNumber,
                DamagePerTick,
                isShrinking: travelled < 1);
        }

        public bool Equals(ZoneShrink other) =>
            From.Equals(other.From)
            && Target.Equals(other.Target)
            && ShrinkFor == other.ShrinkFor
            && PhaseNumber == other.PhaseNumber
            && DamagePerTick.Equals(other.DamagePerTick)
            && Nullable.Equals(NextTarget, other.NextTarget);

        public override bool Equals(object? obj) => obj is ZoneShrink other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = From.GetHashCode();
                hash = hash * 397 ^ Target.GetHashCode();
                hash = hash * 397 ^ ShrinkFor.GetHashCode();
                hash = hash * 397 ^ PhaseNumber;
                return hash * 397 ^ DamagePerTick.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "phase {0}: {1} to {2} over {3:0}s, {4:0.#}/tick",
                PhaseNumber, From, Target, ShrinkFor.TotalSeconds, DamagePerTick);
    }
}
