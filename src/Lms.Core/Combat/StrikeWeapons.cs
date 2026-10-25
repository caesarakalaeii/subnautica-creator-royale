using System;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     The words a <see cref="StrikeWeapon"/> is written as, on the wire and in the log.
    /// </summary>
    /// <remarks>
    ///     These words are the compatibility surface: renaming one is a wire change, and no two
    ///     weapons may share one. An unknown word is refused rather than defaulted.
    /// </remarks>
    public static class StrikeWeapons
    {
        private const string ClawWord = "claw";
        private const string DrillWord = "drill";
        private const string KnifeWord = "knife";
        private const string HeatBladeWord = "heatblade";

        /// <summary>The word for a weapon. What goes on the wire and into <c>detail</c>.</summary>
        /// <remarks>
        ///     Throws on a weapon it has no word for rather than falling back to the claw.
        /// </remarks>
        public static string Word(StrikeWeapon weapon)
        {
            switch (weapon)
            {
                case StrikeWeapon.Claw:
                    return ClawWord;

                case StrikeWeapon.Drill:
                    return DrillWord;

                case StrikeWeapon.Knife:
                    return KnifeWord;

                case StrikeWeapon.HeatBlade:
                    return HeatBladeWord;

                default:
                    throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "No word for that weapon.");
            }
        }

        /// <summary>
        ///     Which family a weapon belongs to, which picks the policy it is judged against. Throws on
        ///     a weapon with no family.
        /// </summary>
        public static WeaponFamily Family(StrikeWeapon weapon)
        {
            switch (weapon)
            {
                case StrikeWeapon.Claw:
                case StrikeWeapon.Drill:
                    return WeaponFamily.PrawnArm;

                case StrikeWeapon.Knife:
                case StrikeWeapon.HeatBlade:
                    return WeaponFamily.Blade;

                default:
                    throw new ArgumentOutOfRangeException(nameof(weapon), weapon,
                        "No family for that weapon, so nothing knows which numbers judge it.");
            }
        }

        /// <summary>Reads a word back, or declines it.</summary>
        public static bool TryParse(string? word, out StrikeWeapon weapon)
        {
            weapon = default;

            if (word == null)
            {
                return false;
            }

            switch (word.Trim())
            {
                case ClawWord:
                    weapon = StrikeWeapon.Claw;
                    return true;

                case DrillWord:
                    weapon = StrikeWeapon.Drill;
                    return true;

                case KnifeWord:
                    weapon = StrikeWeapon.Knife;
                    return true;

                case HeatBladeWord:
                    weapon = StrikeWeapon.HeatBlade;
                    return true;

                default:
                    return false;
            }
        }
    }
}
