using System;

namespace Lms.Core.Match
{
    /// <summary>The cadence a match runs on.</summary>
    public sealed class MatchClockSettings
    {
        public MatchClockSettings(TimeSpan cycleLength, TimeSpan warningLead)
            : this(cycleLength, warningLead, DefaultVictoryWindow)
        {
        }

        public MatchClockSettings(TimeSpan cycleLength, TimeSpan warningLead, TimeSpan victoryWindow)
            : this(cycleLength, warningLead, victoryWindow, null)
        {
        }

        /// <summary>Builds the cadence. A null <paramref name="openingLength"/> means one cycle.</summary>
        public MatchClockSettings(TimeSpan cycleLength, TimeSpan warningLead, TimeSpan victoryWindow,
            TimeSpan? openingLength)
        {
            if (victoryWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(victoryWindow), victoryWindow,
                    "A victory window cannot end before the match is won.");
            }

            if (cycleLength <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cycleLength), cycleLength, "A cull cycle has to last longer than nothing.");
            }

            if (warningLead < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(warningLead), warningLead, "A warning cannot start after the cull it warns about.");
            }

            if (warningLead >= cycleLength)
            {
                throw new ArgumentOutOfRangeException(nameof(warningLead), warningLead,
                    $"A warning lead of {warningLead} would start before the previous cull of a {cycleLength} cycle had fired, so a player would never see the safe state.");
            }

            TimeSpan opening = openingLength ?? cycleLength;

            if (opening <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(openingLength), openingLength,
                    "An opening has to last longer than nothing. A round that starts by closing " +
                    "the wall is not a round with an opening in it.");
            }

            // The opening is the one window that is not a cycle, so it needs the same check on its own.
            if (warningLead >= opening)
            {
                throw new ArgumentOutOfRangeException(nameof(warningLead), warningLead,
                    $"A warning lead of {warningLead} is not shorter than the {opening} opening, so " +
                    "the first warning would be due before the match began and nobody would ever " +
                    "see the opening as safe.");
            }

            CycleLength = cycleLength;
            WarningLead = warningLead;
            VictoryWindow = victoryWindow;
            OpeningLength = opening;
        }

        /// <summary>Ten seconds: enough that the stream does not cut the instant the last opponent dies.</summary>
        public static TimeSpan DefaultVictoryWindow { get; } = TimeSpan.FromSeconds(10);

        /// <summary>How long between culls.</summary>
        public TimeSpan CycleLength { get; }

        /// <summary>
        ///     How long the first window lasts, before the wall has moved at all. Defaults to
        ///     <see cref="CycleLength"/>.
        /// </summary>
        public TimeSpan OpeningLength { get; }

        /// <summary>How long before a cull its warning window opens. Zero means no warning.</summary>
        public TimeSpan WarningLead { get; }

        /// <summary>
        ///     How long a winner is left standing in the arena before the match is written off. Zero
        ///     writes the record immediately.
        /// </summary>
        public TimeSpan VictoryWindow { get; }
    }
}
