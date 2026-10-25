namespace Lms.Core.Telemetry
{
    /// <summary>One escape pod on the caster's map, the arena's fixed landmarks. Sent on every push.</summary>
    public readonly struct PodTelemetry
    {
        public PodTelemetry(double x, double y, double z, int assigned)
        {
            X = x;
            Y = y;
            Z = z;
            Assigned = assigned;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        /// <summary>
        ///     How many players are assigned to it, connected or not. Zero is a derelict; more than one
        ///     indicates a broken layout.
        /// </summary>
        public int Assigned { get; }
    }
}
