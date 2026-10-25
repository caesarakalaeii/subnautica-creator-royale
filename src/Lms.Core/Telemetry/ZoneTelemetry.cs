using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>The current zone sphere and the one it is closing to.</summary>
    public sealed class ZoneTelemetry
    {
        public ZoneTelemetry(SphereTelemetry now, SphereTelemetry? next, double damagePerTick)
        {
            Now = now;
            Next = next;
            DamagePerTick = damagePerTick;
        }

        /// <summary>The wall that bites right now.</summary>
        public SphereTelemetry Now { get; }

        /// <summary>Where it is closing to, or null in the last phase, which closes onto itself.</summary>
        public SphereTelemetry? Next { get; }

        /// <summary>Health lost per tick of <c>zone.damageInterval</c> outside, not per second.</summary>
        public double DamagePerTick { get; }

        /// <summary>
        ///     Projects the server's zone state; the last phase has no next sphere.
        /// </summary>
        public static ZoneTelemetry Of(ZoneState state) =>
            new ZoneTelemetry(
                SphereTelemetry.Of(state.Sphere),
                state.NextSphere.HasValue ? SphereTelemetry.Of(state.NextSphere.Value) : (SphereTelemetry?)null,
                state.DamagePerTick);
    }
}
