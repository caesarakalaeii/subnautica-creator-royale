using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>One sphere on the caster's map: a centre and a radius, flattened.</summary>
    public readonly struct SphereTelemetry
    {
        public SphereTelemetry(double x, double y, double z, double radius)
        {
            X = x;
            Y = y;
            Z = z;
            Radius = radius;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Radius { get; }

        public static SphereTelemetry Of(ZoneSphere sphere) =>
            new SphereTelemetry(sphere.Centre.X, sphere.Centre.Y, sphere.Centre.Z, sphere.Radius);
    }
}
