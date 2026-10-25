using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>Checks an authored squeeze is playable before a match runs on it.</summary>
    /// <remarks>
    ///     Geometry only: it cannot tell whether the last sphere has water in it. Check the map.
    /// </remarks>
    public static class ZoneScheduleValidator
    {
        public static ZoneScheduleValidation Validate(ZoneSchedule schedule, TimeSpan cycleLength)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            List<ZoneScheduleProblem> problems = new List<ZoneScheduleProblem>();

            ZoneSphere previous = schedule.Opening;
            double previousDamage = schedule.OpeningDamagePerTick;

            foreach (ZonePhase phase in schedule.Phases)
            {
                // The collapse is synthesised: no line to point at, and it repeats the last
                // phase's damage on purpose, which the escalation rule would refuse.
                if (phase.IsFinalCollapse)
                {
                    continue;
                }

                if (phase.Target.Radius >= previous.Radius)
                {
                    Add(problems, ZoneScheduleProblemKind.PhaseDoesNotShrink, phase.Number,
                        "the radius goes from {0:0} m to {1:0} m, so this phase squeezes nobody.",
                        previous.Radius, phase.Target.Radius);
                }
                else if (!previous.Contains(phase.Target))
                {
                    // Only when the radius shrank; a growing phase always escapes and is already reported.
                    double overhang = previous.Centre.DistanceTo(phase.Target.Centre) + phase.Target.Radius - previous.Radius;
                    Add(problems, ZoneScheduleProblemKind.PhaseEscapesItsPredecessor, phase.Number,
                        "this sphere sticks {0:0} m out of the one before it, so a player who is safe now can end up outside a zone they cannot reach. Move the centre nearer {1} or cut the radius.",
                        overhang, previous.Centre);
                }

                if (phase.ShrinkFor > cycleLength)
                {
                    Add(problems, ZoneScheduleProblemKind.ShrinkOutlastsTheCycle, phase.Number,
                        "closing takes {0:0}s but a cycle is {1:0}s, so the next phase would start before this one arrived.",
                        phase.ShrinkFor.TotalSeconds, cycleLength.TotalSeconds);
                }

                if (phase.DamagePerTick <= previousDamage)
                {
                    Add(problems, ZoneScheduleProblemKind.DamageDoesNotEscalate, phase.Number,
                        "{0:0.#} per tick is no worse than the {1:0.#} before it, so the squeeze eases off as the map closes.",
                        phase.DamagePerTick, previousDamage);
                }

                previous = phase.Target;
                previousDamage = phase.DamagePerTick;
            }

            return new ZoneScheduleValidation(problems);
        }

        private static void Add(ICollection<ZoneScheduleProblem> problems, ZoneScheduleProblemKind kind,
            int phaseNumber, string format, params object[] arguments) =>
            problems.Add(new ZoneScheduleProblem(kind, phaseNumber,
                string.Format(CultureInfo.InvariantCulture, format, arguments)));
    }
}
