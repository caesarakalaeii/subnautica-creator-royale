using System;
using System.Globalization;
using Lms.Core.Zones;

namespace Lms.Core.Membranes
{
    /// <summary>One stasis field as a ring: where its wall is and how it moves over the field's life.</summary>
    /// <remarks>
    ///     Mirrors the game's <c>StasisSphere</c>: energy falls linearly from 1 to 0 and the wall is
    ///     drawn at <c>radius * SmoothValue(energy, 0.9)</c>, so it peaks a fifth of the way in and
    ///     then shrinks to under a fifth of the radius. Must match the drawn bubble. Players are points.
    /// </remarks>
    public sealed class Membrane
    {
        /// <summary>
        ///     The game's shaping factor for the wall (second argument of <c>StasisSphere.fieldRadius</c>).
        ///     Must match the game.
        /// </summary>
        private const double Shaping = 0.9;

        /// <summary>The game's minimum field radius while the field is alive.</summary>
        private const double SmallestWall = 0.01;

        public Membrane(WorldPoint centre, double nominalRadius, TimeSpan lifetime)
        {
            if (!(nominalRadius > 0) || double.IsInfinity(nominalRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(nominalRadius), nominalRadius,
                    "A ring with no radius cages nobody.");
            }

            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime,
                    "A field that ends the moment it lands is not a fight.");
            }

            Centre = centre;
            NominalRadius = nominalRadius;
            Lifetime = lifetime;
        }

        /// <summary>
        ///     The ring a shot leaves. Radius and lifetime both scale with the charge, as in
        ///     <c>StasisSphere.Shoot</c>.
        /// </summary>
        /// <param name="impact">Where the projectile hit, which is where the field switches on.</param>
        /// <param name="charge">The rifle's normalised charge, 0 to 1.</param>
        /// <param name="limits">What the live component says a field can be.</param>
        public static Membrane FromShot(WorldPoint impact, double charge, StasisFieldLimits limits)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }

            return new Membrane(impact, limits.RadiusFor(charge), limits.LifetimeFor(charge));
        }

        /// <summary>Where the projectile hit. A field does not move once it is up.</summary>
        public WorldPoint Centre { get; }

        /// <summary>
        ///     The radius the charge bought. The actual wall varies over time; see <see cref="RadiusAt"/>.
        /// </summary>
        public double NominalRadius { get; }

        /// <summary>How long the field lasts from the impact.</summary>
        public TimeSpan Lifetime { get; }

        /// <summary>Whether the field has ended by <paramref name="age"/>, leaving nothing behind.</summary>
        public bool HasGoneBy(TimeSpan age) => Age(age) >= Lifetime;

        /// <summary>Where the wall stands <paramref name="age"/> after the impact, in metres from the centre.</summary>
        public double RadiusAt(TimeSpan age)
        {
            if (HasGoneBy(age))
            {
                return 0;
            }

            // Energy falls linearly; the wall follows the game's non-linear curve.
            double energy = 1 - Age(age).TotalSeconds / Lifetime.TotalSeconds;
            double wall = NominalRadius * SmoothValue(energy, Shaping);

            return wall > SmallestWall ? wall : SmallestWall;
        }

        /// <summary>The wall at <paramref name="age"/> as a <see cref="ZoneSphere"/>.</summary>
        public ZoneSphere At(TimeSpan age) => new ZoneSphere(Centre, RadiusAt(age));

        /// <summary>Whether a point is within the wall as it stands at <paramref name="age"/>.</summary>
        public bool Contains(WorldPoint point, TimeSpan age) => At(age).Contains(point);

        /// <summary>What a frame of movement did to the wall as it stands at <paramref name="age"/>.</summary>
        public MembraneCrossing Crossing(WorldPoint from, WorldPoint to, TimeSpan age) =>
            MembraneCrossing.Through(At(age), from, to);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} r{1:0.#}m for {2:0.#}s", Centre, NominalRadius, Lifetime.TotalSeconds);

        private TimeSpan Age(TimeSpan age)
        {
            if (age < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(age), age, "Match time does not run backwards.");
            }

            return age;
        }

        /// <summary>The game's <c>MathExtensions.SmoothValue</c>: peaks at one when value is 0.81.</summary>
        private static double SmoothValue(double value, double factor) =>
            1 - value + 2 * Math.Sqrt(value) * factor - factor * factor;
    }
}
