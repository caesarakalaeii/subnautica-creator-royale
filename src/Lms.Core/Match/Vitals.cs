namespace Lms.Core.Match
{
    /// <summary>How much trouble somebody is in, on the vital's own scale.</summary>
    public enum VitalLevel
    {
        /// <summary>Nothing to say about it.</summary>
        Fine,

        /// <summary>Worth a colour. Not yet worth a caster interrupting themselves.</summary>
        Low,

        /// <summary>The thing to cut to.</summary>
        Critical,
    }

    /// <summary>When a number about a player is worth a colour. Health is a percent; oxygen is in seconds.</summary>
    /// <remarks>
    ///     The dashboard holds a copy of these thresholds in <c>backend/internal/web/static/view.js</c>;
    ///     change both together.
    /// </remarks>
    public static class Vitals
    {
        /// <summary>Out of a hundred, and the same numbers the dashboard reddens a cell at.</summary>
        public const double HealthCritical = 25;

        public const double HealthLow = 50;

        /// <summary>Seconds of air. A base tank holds forty-five.</summary>
        public const double OxygenCritical = 10;

        public const double OxygenLow = 30;

        /// <summary>What a caster's plate colours somebody's health.</summary>
        public static VitalLevel Health(double health) => Level(health, HealthCritical, HealthLow);

        /// <summary>What a caster's plate colours somebody's air.</summary>
        public static VitalLevel Oxygen(double seconds) => Level(seconds, OxygenCritical, OxygenLow);

        /// <summary>At or below a threshold counts, so zero is critical.</summary>
        private static VitalLevel Level(double value, double critical, double low)
        {
            if (value <= critical)
            {
                return VitalLevel.Critical;
            }

            return value <= low ? VitalLevel.Low : VitalLevel.Fine;
        }
    }
}
