using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>
    ///     One step of the squeeze: where the zone is heading, how long it takes to get there, and
    ///     what it costs to be outside it.
    /// </summary>
    public sealed class ZonePhase
    {
        public ZonePhase(int number, ZoneSphere target, TimeSpan shrinkFor, double damagePerTick)
            : this(number, target, shrinkFor, damagePerTick, isFinalCollapse: false)
        {
        }

        private ZonePhase(int number, ZoneSphere target, TimeSpan shrinkFor, double damagePerTick, bool isFinalCollapse)
        {
            if (number < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(number), number, "Phases are counted from one, like the culls they replace.");
            }

            if (shrinkFor < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(shrinkFor), shrinkFor, "A zone does not close backwards.");
            }

            if (!(damagePerTick > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(damagePerTick), damagePerTick,
                    "A phase that does no damage is a phase with no zone in it.");
            }

            Number = number;
            Target = target;
            ShrinkFor = shrinkFor;
            DamagePerTick = damagePerTick;
            IsFinalCollapse = isFinalCollapse;
        }

        /// <summary>
        ///     The phase that closes the last authored sphere onto its own centre, ending the match.
        /// </summary>
        /// <remarks>
        ///     Inherits the window and damage of the phase it follows, so slowing the last phase slows
        ///     the collapse. Not authored, so the validator does not check it.
        /// </remarks>
        internal static ZonePhase CollapseAfter(ZonePhase last) =>
            new ZonePhase(last.Number + 1, new ZoneSphere(last.Target.Centre, 0),
                last.ShrinkFor, last.DamagePerTick, isFinalCollapse: true);

        /// <summary>Which phase this is, counting from one.</summary>
        public int Number { get; }

        /// <summary>True for the collapse the schedule adds itself; false for every authored phase.</summary>
        public bool IsFinalCollapse { get; }

        /// <summary>The sphere the zone holds once this phase has finished closing.</summary>
        public ZoneSphere Target { get; }

        /// <summary>
        ///     How long the wall takes to travel from the previous sphere to this one. The centre
        ///     moves across this window rather than jumping at its start.
        /// </summary>
        public TimeSpan ShrinkFor { get; }

        /// <summary>
        ///     Health taken from a player outside the wall, once per <c>zone.damageInterval</c>.
        ///     Authored per phase, so escalation lives in the config.
        /// </summary>
        public double DamagePerTick { get; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "phase {0}: {1}{2} over {3:0}s, {4:0.#}/tick",
                Number, IsFinalCollapse ? "closing to nothing at " : string.Empty,
                Target, ShrinkFor.TotalSeconds, DamagePerTick);
    }
}
