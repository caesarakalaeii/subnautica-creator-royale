using System;
using System.Globalization;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Prawn suit arm damage against players, and the server-side checks on a reported swing.
    /// </summary>
    /// <remarks>
    ///     Off by default (Nitrox leaves a remote player's <c>LiveMixin</c> invincible). The off
    ///     policy still carries the recommended numbers because the loader falls back field by field.
    /// </remarks>
    public sealed class PrawnPolicy : StrikePolicy
    {
        /// <summary>
        ///     How far the game's own claw reaches, in metres; the floor under <see cref="Reach"/>.
        /// </summary>
        /// <remarks>
        ///     A compiled constant in <c>ExosuitClawArm</c>. The drill reaches 5 m, so the claw sets
        ///     the floor.
        /// </remarks>
        public const double GameClawReach = 6.5;

        /// <param name="hurtsPlayers">False and the arms do nothing to anybody.</param>
        /// <param name="clawDamage">What one claw punch costs the player it lands on.</param>
        /// <param name="drillDamage">What one drill tick costs. Zero switches the drill off.</param>
        /// <param name="reach">How far apart the server will believe a hit was, in metres.</param>
        /// <param name="minInterval">The soonest a second hit from the same prawn is believed.</param>
        public PrawnPolicy(bool hurtsPlayers, double clawDamage, double drillDamage, double reach,
            TimeSpan minInterval)
            : base(hurtsPlayers, AsFarAsAClaw(reach), minInterval)
        {
            // Zero switches an arm off. Damage above a player's 100 health is allowed (one-shot).
            Damage(clawDamage, nameof(clawDamage));
            Damage(drillDamage, nameof(drillDamage));

            ClawDamage = clawDamage;
            DrillDamage = drillDamage;
        }

        /// <summary>
        ///     Arms switched off, with recommended starting damage values.
        /// </summary>
        public static PrawnPolicy Default { get; } =
            new PrawnPolicy(false, clawDamage: 20, drillDamage: 0, reach: 9,
                minInterval: TimeSpan.FromMilliseconds(800));

        /// <summary>
        ///     What one claw punch costs. The game's own value is 50 against 100 health.
        /// </summary>
        public double ClawDamage { get; }

        /// <summary>
        ///     What one drill tick costs. Zero by default.
        /// </summary>
        /// <remarks>
        ///     The game's drill does 4 per tick, but ticks fire from an animation event at an
        ///     unmeasured rate.
        /// </remarks>
        public double DrillDamage { get; }

        /// <summary>
        ///     False: a prawn suit is the vehicle, so being in one is required.
        /// </summary>
        public override bool AVehicleDisarms => false;

        public override StrikeRefusal HarmlessRefusal => StrikeRefusal.PrawnsAreHarmless;

        public override StrikeRefusal DisabledRefusal => StrikeRefusal.ArmIsDisabled;

        public override StrikeRefusal NotArmedRefusal => StrikeRefusal.NotInAPrawn;

        /// <inheritdoc/>
        /// <remarks>
        ///     Throws on an arm with no damage of its own rather than falling back to the claw's.
        /// </remarks>
        public override double Damage(StrikeWeapon weapon)
        {
            switch (weapon)
            {
                case StrikeWeapon.Claw:
                    return ClawDamage;

                case StrikeWeapon.Drill:
                    return DrillDamage;

                default:
                    throw new ArgumentOutOfRangeException(nameof(weapon), weapon,
                        "That is not a prawn's arm. The server picks the policy from the weapon's " +
                        "own family before it judges, so reaching this means the two have come apart.");
            }
        }

        /// <summary>
        ///     Rejects a reach not beyond <see cref="GameClawReach"/>. Static so it can run in the
        ///     <c>base</c> call.
        /// </summary>
        private static double AsFarAsAClaw(double reach)
        {
            if (!(reach > GameClawReach))
            {
                throw new ArgumentOutOfRangeException(nameof(reach), reach,
                    "The game's own claw reaches " + GameClawReach.ToString(CultureInfo.InvariantCulture) +
                    " m, so a reach no longer than that refuses hits the attacker really landed. " +
                    "From the cockpit that reads as a prawn that does not work, because the arms " +
                    "play their swing and their hit sound whatever the server decides.");
            }

            return reach;
        }

        private static void Damage(double damage, string name)
        {
            if (double.IsNaN(damage) || double.IsInfinity(damage) || damage < 0)
            {
                throw new ArgumentOutOfRangeException(name, damage,
                    "An arm's damage is a number of health, zero or more. Zero switches that arm " +
                    "off; a negative would be an arm that heals, and there is no tournament " +
                    "decision behind that.");
            }
        }
    }
}
