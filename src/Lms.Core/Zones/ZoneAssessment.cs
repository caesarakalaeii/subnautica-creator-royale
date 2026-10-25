using System;
using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>The verdict for one player at one moment, and what it costs them.</summary>
    public readonly struct ZoneAssessment
    {
        internal ZoneAssessment(ZoneVerdict verdict, double distanceOutside, double exposure,
            int ticks, double damagePerTick, TimeSpan untilNextTick)
        {
            Verdict = verdict;
            DistanceOutside = distanceOutside;
            Exposure = exposure;
            Ticks = ticks;
            DamagePerTick = damagePerTick;
            UntilNextTick = untilNextTick;
        }

        public ZoneVerdict Verdict { get; }

        /// <summary>Metres past the wall, or zero inside it.</summary>
        public double DistanceOutside { get; }

        /// <summary>
        ///     Nought to one, for the adapter to hand to <c>Player.SetRadiationAmount</c>, which drives
        ///     the game's own static overlay as the "you are outside" warning.
        /// </summary>
        public double Exposure { get; }

        /// <summary>
        ///     How many bites fell due this frame: usually zero, one when a tick comes round, more
        ///     after a frame longer than the interval. Kept separate because the log writes one
        ///     record per tick.
        /// </summary>
        public int Ticks { get; }

        /// <summary>What one bite costs in this phase, or zero inside the wall.</summary>
        public double DamagePerTick { get; }

        /// <summary>
        ///     Health to take this frame: zero on most frames, a phase's worth when a tick falls due,
        ///     and several ticks' worth after a long frame.
        /// </summary>
        public double Damage => Ticks * DamagePerTick;

        /// <summary>How long until the next bite, for the HUD. Zero inside the wall.</summary>
        public TimeSpan UntilNextTick { get; }

        public override string ToString() =>
            Verdict == ZoneVerdict.Outside
                ? string.Format(CultureInfo.InvariantCulture, "{0} by {1:0} m", Verdict, DistanceOutside)
                : Verdict.ToString();
    }
}
