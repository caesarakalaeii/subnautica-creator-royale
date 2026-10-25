using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Knife and heat blade damage against players, and the server-side checks on a reported swing.
    /// </summary>
    /// <remarks>
    ///     Two damage values rather than a knife plus a multiplier: in the game a <c>HeatBlade</c> is
    ///     a <c>Knife</c> with no damage override, so there is no ratio to derive. Off by default.
    /// </remarks>
    public sealed class BladePolicy : StrikePolicy
    {
        /// <summary>
        ///     The game's own knife trace distance in metres (<c>Knife.attackDist</c>).
        /// </summary>
        /// <remarks>
        ///     A serialized per-prefab field, not a compiled constant, so the client reads it off the
        ///     instance; this value only feeds the reach floor.
        /// </remarks>
        public const double GameBladeReach = 2.0;

        /// <summary>
        ///     Extra distance in metres a fully agreed hit already measures, origin to origin.
        /// </summary>
        /// <remarks>
        ///     The game traces from the camera, falls back to a 0.7 m sphere and stops on the victim's
        ///     collider surface, while the server measures between origins: 0.7 sphere + 0.3 camera +
        ///     1.8 capsule.
        /// </remarks>
        public const double BodyAllowance = 2.8;

        /// <param name="hurtsPlayers">False and blades do nothing to anybody.</param>
        /// <param name="knifeDamage">What one knife hit costs. Zero switches the knife off.</param>
        /// <param name="heatBladeDamage">What one heat blade hit costs. Zero switches it off.</param>
        /// <param name="reach">How far apart the server will believe a hit was, in metres.</param>
        /// <param name="minInterval">
        ///     The soonest a second hit from the same player is believed. The game gates tool use at
        ///     0.5 s; this is lower because the gap is measured between report arrivals, which jitter.
        /// </param>
        /// <param name="resistances">
        ///     Damage multiplier per equipped item, by the game's item name; null keeps
        ///     <see cref="DefaultResistances"/>.
        /// </param>
        public BladePolicy(bool hurtsPlayers, double knifeDamage, double heatBladeDamage, double reach,
            TimeSpan minInterval, IReadOnlyDictionary<string, double>? resistances = null)
            : base(hurtsPlayers, WithinArmsLength(reach), minInterval)
        {
            Damage(knifeDamage, nameof(knifeDamage));
            Damage(heatBladeDamage, nameof(heatBladeDamage));

            KnifeDamage = knifeDamage;
            HeatBladeDamage = heatBladeDamage;
            Resistances = resistances is null ? DefaultResistances : Guarded(resistances);
        }

        /// <summary>
        ///     Default equipment resistances: the reinforced dive suit halves blade damage.
        /// </summary>
        public static IReadOnlyDictionary<string, double> DefaultResistances { get; } =
            new Dictionary<string, double> { ["ReinforcedDiveSuit"] = 0.5 };

        /// <summary>
        ///     Blades switched off, with recommended starting damage values.
        /// </summary>
        public static BladePolicy Default { get; } =
            new BladePolicy(false, knifeDamage: 12, heatBladeDamage: 20, reach: 6,
                minInterval: TimeSpan.FromMilliseconds(250));
        /// <summary>
        ///     What one knife hit costs.
        /// </summary>
        public double KnifeDamage { get; }

        /// <summary>
        ///     What one heat blade hit costs.
        /// </summary>
        public double HeatBladeDamage { get; }

        /// <summary>
        ///     Boarding a vehicle holsters the held tool, so a blade swing from inside one is forged.
        /// </summary>
        public override bool AVehicleDisarms => true;

        public override StrikeRefusal HarmlessRefusal => StrikeRefusal.BladesAreHarmless;

        public override StrikeRefusal DisabledRefusal => StrikeRefusal.BladeIsDisabled;

        public override StrikeRefusal NotArmedRefusal => StrikeRefusal.NotHoldingThatBlade;

        /// <summary>
        ///     Damage multiplier in <c>(0, 1]</c> per piece of the victim's equipment, by the game's
        ///     item name. Empty and every stab lands in full.
        /// </summary>
        public override IReadOnlyDictionary<string, double> Resistances { get; }

        /// <inheritdoc/>
        public override double Damage(StrikeWeapon weapon)
        {
            switch (weapon)
            {
                case StrikeWeapon.Knife:
                    return KnifeDamage;

                case StrikeWeapon.HeatBlade:
                    return HeatBladeDamage;

                default:
                    throw new ArgumentOutOfRangeException(nameof(weapon), weapon,
                        "That is not a blade. The server picks the policy from the weapon's own " +
                        "family before it judges, so reaching this means the two have come apart.");
            }
        }

        /// <summary>
        ///     Rejects a reach that would not cover <see cref="GameBladeReach"/> plus
        ///     <see cref="BodyAllowance"/>. Static so it can run in the <c>base</c> call.
        /// </summary>
        private static double WithinArmsLength(double reach)
        {
            const double Floor = GameBladeReach + BodyAllowance;

            if (!(reach > Floor))
            {
                throw new ArgumentOutOfRangeException(nameof(reach), reach,
                    "A blade traces " + GameBladeReach.ToString(CultureInfo.InvariantCulture) +
                    " m, but it traces from the camera, falls back to a 0.7 m sphere and stops on " +
                    "the victim's surface -- while the server measures origin to origin. So a hit " +
                    "both machines agree about is already up to " +
                    Floor.ToString(CultureInfo.InvariantCulture) + " m apart by this measure, and a " +
                    "reach no longer than that refuses stabs that really landed. From the water that " +
                    "reads as a knife that does not work, because the blade plays its swing and its " +
                    "hit sound whatever the server decides.");
            }

            return reach;
        }

        private static void Damage(double damage, string name)
        {
            if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0)
            {
                throw new ArgumentOutOfRangeException(name, damage,
                    "A blade's damage is a number of health, zero or more. Zero switches that blade " +
                    "off; a negative would be a blade that heals, and there is no tournament " +
                    "decision behind that.");
            }
        }

        private static IReadOnlyDictionary<string, double> Guarded(IReadOnlyDictionary<string, double> resistances)
        {
            foreach (KeyValuePair<string, double> entry in resistances)
            {
                if (string.IsNullOrEmpty(entry.Key) || double.IsNaN(entry.Value)
                    || double.IsInfinity(entry.Value) || !(entry.Value > 0) || entry.Value > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(resistances), entry.Value,
                        "A resistance is a multiplier on a landed blade's damage, in (0, 1]. Zero " +
                        "would land a hit for nothing and above one would heal the wearer, and " +
                        "there is no tournament decision behind either.");
                }
            }

            return resistances;
        }
    }
}
