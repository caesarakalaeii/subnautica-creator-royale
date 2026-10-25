using System;

namespace Lms.Core.Zones
{
    /// <summary>What the wall would have taken off a player who was disconnected.</summary>
    /// <remarks>
    ///     Computed from the match clock and the audit log's last position and health, by replaying
    ///     a real <see cref="ZoneWatch"/> at the damage interval so the bill matches what being
    ///     present would have cost. Every approximation favours the player: the watch starts fresh
    ///     after the grace, ticks due after the return are left to the live watch, the bill stops at
    ///     a lethal amount, and the position is taken as fixed (a disconnected player cannot move).
    /// </remarks>
    public sealed class ZoneArrears
    {
        private readonly ZoneSchedule schedule;
        private readonly ZonePolicy policy;
        private readonly TimeSpan cycleLength;
        private readonly TimeSpan openingLength;
        private readonly DisconnectPolicy disconnect;

        /// <param name="schedule">The authored squeeze, which is what prices each tick.</param>
        /// <param name="policy">How often the wall bites, so the bill is on the match's own cadence.</param>
        /// <param name="cycleLength">How long between phases, from <c>match.cycleLength</c>.</param>
        /// <param name="openingLength">How long before the first, from <c>match.openingLength</c>.</param>
        /// <param name="disconnect">How much of an absence is forgiven.</param>
        public ZoneArrears(ZoneSchedule schedule, ZonePolicy policy, TimeSpan cycleLength,
            TimeSpan openingLength, DisconnectPolicy disconnect)
        {
            this.schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            this.disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));

            if (cycleLength <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cycleLength), cycleLength,
                    "A cycle of no time would fire every phase at once.");
            }

            this.cycleLength = cycleLength;
            this.openingLength = openingLength;
        }

        /// <summary>What one absence cost.</summary>
        /// <param name="where">The last place the server saw them, off the audit log.</param>
        /// <param name="leftAt">Match time the log says they went.</param>
        /// <param name="returnedAt">Match time now, which is when they came back.</param>
        /// <param name="health">What the log last knew them to be on.</param>
        /// <remarks>
        ///     A return not after the departure owes nothing rather than throwing: the world clock
        ///     can step back slightly when corrected, and a reset takes match time to zero.
        /// </remarks>
        public ZoneDebt Of(WorldPoint where, TimeSpan leftAt, TimeSpan returnedAt, double health)
        {
            if (leftAt < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(leftAt), leftAt, "Match time does not run backwards.");
            }

            TimeSpan absence = returnedAt - leftAt;
            if (absence <= TimeSpan.Zero)
            {
                return new ZoneDebt(TimeSpan.Zero, TimeSpan.Zero, 0, 0, health);
            }

            TimeSpan forgiven = absence < disconnect.Grace ? absence : disconnect.Grace;
            TimeSpan from = leftAt + forgiven;

            ZoneWatch watch = new ZoneWatch(policy);
            int ticks = 0;
            double damage = 0;

            for (TimeSpan due = from + policy.DamageInterval; due <= returnedAt; due += policy.DamageInterval)
            {
                ZoneAssessment fell = watch.Update(where, schedule.AtElapsed(due, cycleLength, openingLength), policy.DamageInterval);

                ticks += fell.Ticks;
                damage += fell.Damage;

                // Stop rather than clamp afterwards, so the tick count is bites actually taken.
                if (damage >= health)
                {
                    break;
                }
            }

            return new ZoneDebt(absence, forgiven, ticks, damage, health);
        }
    }
}
