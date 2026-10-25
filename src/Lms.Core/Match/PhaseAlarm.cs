using System;

namespace Lms.Core.Match
{
    /// <summary>How loudly to say that the wall is about to move. Ordered, so levels can be compared.</summary>
    public enum AlarmLevel
    {
        /// <summary>The window has opened. There is time to swim.</summary>
        Notice,

        /// <summary>Seconds, not minutes. Stop what you are doing.</summary>
        Urgent,

        /// <summary>It is happening. Nothing else on the screen matters.</summary>
        Immediate,
    }

    /// <summary>When to say the wall is about to move, and how loudly.</summary>
    /// <remarks>
    ///     A ladder of countdown marks, each said once and only if it fits the warning window. When a
    ///     jump passes several marks, only the lowest one reached is said. The adapter owns words and sound.
    /// </remarks>
    public sealed class PhaseAlarm
    {
        /// <summary>
        ///     Seconds before the wall moves that are worth saying out loud, largest first. No zero:
        ///     the phase starting announces itself.
        /// </summary>
        private static readonly int[] Marks = { 60, 30, 15, 10, 5, 3, 2, 1 };

        private readonly TimeSpan lead;

        private int phase = -1;

        /// <summary>How many rungs from the top have been used up, said or skipped past.</summary>
        private int used;

        /// <param name="lead">
        ///     The warning window, from <see cref="MatchClockSettings.WarningLead"/>. Marks longer
        ///     than this are never said.
        /// </param>
        public PhaseAlarm(TimeSpan lead)
        {
            if (lead <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lead), lead,
                    "A warning window of nothing cannot warn anybody.");
            }

            this.lead = lead;
        }

        /// <summary>Whether to say something now, and how loudly.</summary>
        /// <param name="phaseNumber">Which phase is coming. A new one re-arms the whole ladder.</param>
        /// <param name="untilPhase">How long until the wall moves.</param>
        /// <returns>Null when there is nothing to say, which is most frames.</returns>
        public AlarmLevel? Due(int phaseNumber, TimeSpan untilPhase) =>
            Due(phaseNumber, untilPhase, out _);

        /// <param name="seconds">The mark this is for, which is the number to put in the message.</param>
        /// <inheritdoc cref="Due(int, TimeSpan)"/>
        public AlarmLevel? Due(int phaseNumber, TimeSpan untilPhase, out int seconds)
        {
            seconds = 0;

            if (phaseNumber != phase)
            {
                phase = phaseNumber;
                used = 0;
            }

            if (untilPhase < TimeSpan.Zero)
            {
                // The wall has already moved; the phase itself announces that.
                return null;
            }

            double left = untilPhase.TotalSeconds;
            int found = -1;

            // A rung is due once no more time is left than it names; the lowest due rung wins.
            for (int rung = used; rung < Marks.Length; rung++)
            {
                if (Marks[rung] > lead.TotalSeconds)
                {
                    // Longer than the warning window: never said, and skipped for good.
                    used = rung + 1;
                    continue;
                }

                if (left > Marks[rung])
                {
                    // Not due yet, and nor is anything below it.
                    break;
                }

                found = rung;
            }

            if (found < 0)
            {
                return null;
            }

            used = found + 1;
            seconds = Marks[found];

            return Loudness(Marks[found]);
        }

        /// <summary>Above 15 s is a notice, down to 5 s is urgent, the last three seconds are immediate.</summary>
        private static AlarmLevel Loudness(int mark)
        {
            if (mark > 15)
            {
                return AlarmLevel.Notice;
            }

            return mark > 3 ? AlarmLevel.Urgent : AlarmLevel.Immediate;
        }
    }
}
