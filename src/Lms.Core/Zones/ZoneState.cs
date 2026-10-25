using System.Globalization;

namespace Lms.Core.Zones
{
    /// <summary>Where the zone is right now, and what it is about to do.</summary>
    public readonly struct ZoneState
    {
        internal ZoneState(ZoneSphere sphere, ZoneSphere? nextSphere, int phaseNumber, double damagePerTick, bool isShrinking)
        {
            Sphere = sphere;
            NextSphere = nextSphere;
            PhaseNumber = phaseNumber;
            DamagePerTick = damagePerTick;
            IsShrinking = isShrinking;
        }

        /// <summary>The wall as it stands this tick. What the renderer draws and the watch tests against.</summary>
        public ZoneSphere Sphere { get; }

        /// <summary>
        ///     Where the next phase is heading, or null in the last phase. Drives the "outside the
        ///     next circle" warning and the caster map's second ring.
        /// </summary>
        public ZoneSphere? NextSphere { get; }

        /// <summary>Which phase the match is in. Zero before the first one fires.</summary>
        public int PhaseNumber { get; }

        /// <summary>What one tick outside the wall costs right now.</summary>
        public double DamagePerTick { get; }

        /// <summary>True while the wall is moving, which is when it is worth looking at.</summary>
        public bool IsShrinking { get; }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "phase {0} {1}{2}, {3:0.#}/tick",
                PhaseNumber, Sphere, IsShrinking ? " closing" : string.Empty, DamagePerTick);
    }
}
