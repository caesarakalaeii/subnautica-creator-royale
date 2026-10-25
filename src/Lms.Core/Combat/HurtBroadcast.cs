using System;
using System.Globalization;
using Lms.Core.Wire;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Server-to-victim message that a validated hit landed: how much damage and who dealt it.
    /// </summary>
    /// <remarks>
    ///     The server decides the damage; the victim's own client applies it, since a remote
    ///     player's <c>LiveMixin</c> is invincible under Nitrox.
    /// </remarks>
    public readonly struct HurtBroadcast : IEquatable<HurtBroadcast>
    {
        /// <summary>The first token of the wire form. It moves when the meaning of a field moves.</summary>
        public const string WireHeader = "lms-hurt/1";

        /// <param name="damage">Health to remove, as configured.</param>
        /// <param name="by">The attacker's name, from Nitrox's roster.</param>
        public HurtBroadcast(double damage, string by)
        {
            if (!(damage > 0) || double.IsInfinity(damage))
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage,
                    "A hit worth nothing is a refusal, and a refusal travels with a reason on it.");
            }

            if (string.IsNullOrWhiteSpace(by))
            {
                throw new ArgumentException(
                    "A hit needs the name of whoever landed it: it goes on the victim's screen and " +
                    "into the audit record beside it.", nameof(by));
            }

            Damage = damage;
            By = by;
        }

        /// <summary>Health the victim's own client is about to take off them.</summary>
        public double Damage { get; }

        /// <summary>The name the log identifies the attacker by.</summary>
        public string By { get; }

        public string Encode() =>
            WireHeader
            + "\tdamage=" + Damage.ToString("G17", CultureInfo.InvariantCulture)
            + "\tby=" + WireText.Escape(By);

        /// <summary>Whether a line is one of these, without reading it.</summary>
        public static bool IsHurt(string? line) =>
            line != null && line.StartsWith(WireHeader, StringComparison.Ordinal);

        /// <summary>
        ///     Reads a hit back, or declines it when anything is missing or malformed, so a client
        ///     never applies a guessed damage.
        /// </summary>
        public static bool TryDecode(string? line, out HurtBroadcast hurt)
        {
            hurt = default;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string[] tokens = line!.Split('\t');
            if (tokens[0] != WireHeader)
            {
                return false;
            }

            double? damage = null;
            string? by = null;

            for (int i = 1; i < tokens.Length; i++)
            {
                int split = tokens[i].IndexOf('=');
                if (split < 1)
                {
                    continue;
                }

                string key = tokens[i].Substring(0, split);
                string value = tokens[i].Substring(split + 1);

                switch (key)
                {
                    case "damage":
                        damage = ReadNumber(value);
                        break;

                    case "by":
                        by = WireText.Unescape(value);
                        break;

                    default:
                        // Unknown keys from a newer server are ignored.
                        break;
                }
            }

            if (!damage.HasValue || !(damage.Value > 0) || string.IsNullOrWhiteSpace(by))
            {
                return false;
            }

            hurt = new HurtBroadcast(damage.Value, by!);

            return true;
        }

        public bool Equals(HurtBroadcast other) =>
            Damage.Equals(other.Damage) && string.Equals(By, other.By, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is HurtBroadcast other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Damage.GetHashCode() * 397 ^ (By == null ? 0 : By.GetHashCode());
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} for {1:0.#}", By, Damage);

        private static double? ReadNumber(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && !double.IsNaN(parsed)
            && !double.IsInfinity(parsed)
                ? parsed
                : (double?)null;
    }
}
