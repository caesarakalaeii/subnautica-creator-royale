using System;
using System.Globalization;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     What an attacker's client reports: "I swung this weapon at player 7".
    /// </summary>
    /// <remarks>
    ///     Carries no damage and no attacker id: damage comes from the server's policy and the
    ///     attacker is the authenticated connection, so a client cannot inflate a hit or frame
    ///     somebody.
    /// </remarks>
    public static class StrikeReport
    {
        /// <summary>The first token of the wire form. It moves when the meaning of what follows moves.</summary>
        public const string WireHeader = "lms-strike/1";

        private const string VictimField = "victim";

        /// <summary>
        ///     The wire field a weapon travels in.
        /// </summary>
        /// <remarks>
        ///     Named <c>arm</c> for every weapon family; renaming it would break older clients.
        /// </remarks>
        private const string ArmField = "arm";

        /// <summary>Whether this is a swing at all, which is the server's first question.</summary>
        public static bool IsStrike(string? wire) =>
            !string.IsNullOrWhiteSpace(wire) && Head(wire!) == WireHeader;

        /// <summary>Writes one swing.</summary>
        public static string Encode(ushort victim, StrikeWeapon weapon) =>
            WireHeader
            + "\t" + VictimField + "=" + victim.ToString(CultureInfo.InvariantCulture)
            + "\t" + ArmField + "=" + StrikeWeapons.Word(weapon);

        /// <summary>
        ///     Reads a swing back, or declines it.
        /// </summary>
        /// <remarks>
        ///     Unknown fields are ignored; an unknown version or weapon is refused rather than
        ///     defaulted.
        /// </remarks>
        public static bool TryDecode(string? wire, out ushort victim, out StrikeWeapon weapon)
        {
            victim = 0;
            weapon = default;

            if (!IsStrike(wire))
            {
                return false;
            }

            string[] fields = wire!.Split('\t');
            string? victimField = null;
            string? weaponField = null;

            for (int field = 1; field < fields.Length; field++)
            {
                int split = fields[field].IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                string key = fields[field].Substring(0, split);
                string value = fields[field].Substring(split + 1);

                switch (key)
                {
                    case VictimField:
                        victimField = value;
                        break;

                    case ArmField:
                        weaponField = value;
                        break;

                    default:
                        break;
                }
            }

            return victimField != null
                   && ushort.TryParse(victimField, NumberStyles.Integer, CultureInfo.InvariantCulture, out victim)
                   && StrikeWeapons.TryParse(weaponField, out weapon);
        }

        private static string Head(string wire)
        {
            int tab = wire.IndexOf('\t');

            return tab < 0 ? wire.Trim() : wire.Substring(0, tab);
        }
    }
}
