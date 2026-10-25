using System;
using System.Globalization;
using Lms.Core.Diagnostics;

namespace Lms.Core.Zones
{
    /// <summary>What one absence cost: the bill a returning player is handed.</summary>
    /// <remarks>Carries what was forgiven as well as what was charged, so the bill explains itself.</remarks>
    public readonly struct ZoneDebt
    {
        internal ZoneDebt(TimeSpan absence, TimeSpan forgiven, int ticks, double damage,
            double health)
        {
            Absence = absence;
            Forgiven = forgiven;
            Ticks = ticks;
            Damage = damage;
            HealthBefore = health;
        }

        /// <summary>How long they were gone, in match time.</summary>
        public TimeSpan Absence { get; }

        /// <summary>How much of it the grace period let them keep.</summary>
        public TimeSpan Forgiven { get; }

        /// <summary>How much of it the wall was allowed to charge for.</summary>
        public TimeSpan Charged => Absence - Forgiven;

        /// <summary>How many bites fell due while they were away and outside the wall.</summary>
        public int Ticks { get; }

        /// <summary>Health the wall would have taken, priced at the phase each tick fell due in.</summary>
        public double Damage { get; }

        /// <summary>What the log last knew them to be on.</summary>
        public double HealthBefore { get; }

        /// <summary>Whether anything is actually owed. False is the ordinary case.</summary>
        public bool Owed => Ticks > 0;

        /// <summary>Whether the wall would have finished them while they were away.</summary>
        public bool IsFatal => Damage >= HealthBefore;

        /// <summary>What the bill leaves them on, floored at nothing.</summary>
        public double HealthLeft => IsFatal ? 0 : HealthBefore - Damage;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}, away {1:0.#}s",
                // Culture-invariant too: client and server logs may use different locales.
                Owed ? Readable.Line($"{Damage:0.#} over {Ticks} ticks") : "nothing owed",
                Absence.TotalSeconds);
    }
}
