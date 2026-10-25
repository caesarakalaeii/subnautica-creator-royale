using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Zones
{
    /// <summary>
    ///     The squeeze: an opening sphere, then a phase per cycle of the match clock, and a final
    ///     collapse to nothing that the file does not write.
    /// </summary>
    /// <remarks>
    ///     A pure function of the clock, not a ticking thing, so pause, skip and reset need no
    ///     handling here.
    /// </remarks>
    public sealed class ZoneSchedule
    {
        private readonly IReadOnlyList<ZonePhase> phases;

        /// <param name="opening">Where the wall stands before the first phase fires.</param>
        /// <param name="openingDamagePerTick">
        ///     What it costs to be outside the opening sphere. Not zero: a player who spawns beyond
        ///     it, or swims out early, must not be free until the first phase.
        /// </param>
        /// <param name="phases">The phases, in order, one per cycle of the clock.</param>
        public ZoneSchedule(ZoneSphere opening, double openingDamagePerTick, IReadOnlyList<ZonePhase> phases)
        {
            if (phases == null)
            {
                throw new ArgumentNullException(nameof(phases));
            }

            if (phases.Count == 0)
            {
                throw new ArgumentException("A zone that never shrinks is not a squeeze; the match would never end.", nameof(phases));
            }

            if (!(openingDamagePerTick > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(openingDamagePerTick), openingDamagePerTick,
                    "Outside the opening sphere has to cost something, or leaving the map early is free.");
            }

            foreach (ZonePhase phase in phases)
            {
                if (phase == null)
                {
                    throw new ArgumentException("A phase is missing.", nameof(phases));
                }
            }

            Opening = opening;
            OpeningDamagePerTick = openingDamagePerTick;
            AuthoredPhaseCount = phases.Count;

            // Added here so the collapse is an ordinary phase downstream: the clock counts it,
            // `lms skip` starts it, the renderer draws it and the watch bites through it.
            List<ZonePhase> all = new List<ZonePhase>(phases);
            all.Add(ZonePhase.CollapseAfter(all[all.Count - 1]));
            this.phases = new ReadOnlyCollection<ZonePhase>(all);
        }

        public ZoneSphere Opening { get; }

        public double OpeningDamagePerTick { get; }

        /// <summary>Every phase the match runs, the collapse last.</summary>
        public IReadOnlyList<ZonePhase> Phases => phases;

        /// <summary>How many phases this schedule has, which is how many cycles a match runs for.</summary>
        /// <remarks>
        ///     Includes the collapse; use <see cref="AuthoredPhaseCount"/> when reporting to an author.
        /// </remarks>
        public int PhaseCount => phases.Count;

        /// <summary>How many phases the config actually wrote, which is what an operator counts.</summary>
        public int AuthoredPhaseCount { get; }

        /// <summary>The phase that closes the last sphere onto its own centre and ends the match.</summary>
        public ZonePhase FinalCollapse => phases[phases.Count - 1];

        /// <summary>Where the wall is.</summary>
        /// <param name="completedPhases">How many phases the clock has fired. Zero before the first.</param>
        /// <param name="sincePhaseStart">How long ago the last one fired.</param>
        public ZoneState At(int completedPhases, TimeSpan sincePhaseStart) =>
            ShrinkAt(completedPhases).At(sincePhaseStart);

        /// <summary>How many phases have fired by a moment in match time.</summary>
        /// <param name="elapsed">Match time. Paused time does not count, so this is the clock's own.</param>
        /// <param name="cycleLength">How long between phases, from <c>match.cycleLength</c>.</param>
        /// <param name="openingLength">
        ///     How long before the first one, from <c>match.openingLength</c>; the cycle when the
        ///     file does not say.
        /// </param>
        /// <remarks>
        ///     Must match <see cref="Lms.Core.Match.MatchClock"/>: phase N fires at the opening plus
        ///     N - 1 cycles. <c>ZoneScheduleElapsedTests</c> keeps the two in step.
        /// </remarks>
        public int PhasesFiredBy(TimeSpan elapsed, TimeSpan cycleLength, TimeSpan openingLength)
        {
            if (cycleLength <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cycleLength), cycleLength,
                    "A cycle of no time would fire every phase at once.");
            }

            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Match time does not run backwards.");
            }

            if (elapsed < openingLength)
            {
                return 0;
            }

            long fired = 1 + (elapsed - openingLength).Ticks / cycleLength.Ticks;

            return fired >= phases.Count ? phases.Count : (int)fired;
        }

        /// <summary>Where the wall is at a moment in match time.</summary>
        /// <remarks>
        ///     For callers that only have elapsed time, such as disconnect arrears; a running match
        ///     uses <see cref="At"/> with the clock's own counts.
        /// </remarks>
        public ZoneState AtElapsed(TimeSpan elapsed, TimeSpan cycleLength, TimeSpan openingLength)
        {
            int fired = PhasesFiredBy(elapsed, cycleLength, openingLength);
            TimeSpan since = fired == 0
                ? elapsed
                : elapsed - openingLength - TimeSpan.FromTicks((fired - 1) * cycleLength.Ticks);

            return At(fired, since > TimeSpan.Zero ? since : TimeSpan.Zero);
        }

        /// <summary>
        ///     The geometry of one phase, without a moment in it: what the server sends the clients
        ///     instead of the schedule itself.
        /// </summary>
        /// <param name="completedPhases">How many phases the clock has fired. Zero before the first.</param>
        /// <remarks>
        ///     Before the first phase this is a shrink from the opening sphere to itself over no time,
        ///     so nothing downstream special-cases the lobby.
        /// </remarks>
        public ZoneShrink ShrinkAt(int completedPhases)
        {
            if (completedPhases < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(completedPhases), completedPhases, "A match cannot have un-fired phases.");
            }

            if (completedPhases == 0)
            {
                return new ZoneShrink(Opening, Opening, TimeSpan.Zero, 0, OpeningDamagePerTick, phases[0].Target);
            }

            // Past the end of the schedule (skip or replay), hold the closed final sphere until
            // the match is reset.
            int index = Math.Min(completedPhases, phases.Count) - 1;
            ZonePhase phase = phases[index];

            return new ZoneShrink(
                index == 0 ? Opening : phases[index - 1].Target,
                phase.Target,
                phase.ShrinkFor,
                phase.Number,
                phase.DamagePerTick,
                index + 1 < phases.Count ? phases[index + 1].Target : (ZoneSphere?)null);
        }
    }
}
