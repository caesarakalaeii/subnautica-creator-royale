using System;
using System.Globalization;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     The server's decision on one reported swing: landed for some damage, or refused with a
    ///     reason that is sent back to the attacker.
    /// </summary>
    public readonly struct StrikeVerdict : IEquatable<StrikeVerdict>
    {
        private StrikeVerdict(bool landed, double damage, StrikeRefusal refusal)
        {
            Landed = landed;
            Damage = damage;
            Refusal = refusal;
        }

        /// <summary>Whether the victim loses health for this.</summary>
        public bool Landed { get; }

        /// <summary>What it costs them. Zero on a refusal, and never zero on a hit.</summary>
        public double Damage { get; }

        /// <summary>Why not, or <see cref="StrikeRefusal.None"/> when it landed.</summary>
        public StrikeRefusal Refusal { get; }

        /// <summary>A landed hit, for a damage the config named. Throws on zero or less.</summary>
        public static StrikeVerdict Hit(double damage)
        {
            if (!(damage > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage,
                    "A hit that costs nothing is a refusal, not a hit.");
            }

            return new StrikeVerdict(true, damage, StrikeRefusal.None);
        }

        /// <summary>A swing the server did not believe.</summary>
        public static StrikeVerdict Refused(StrikeRefusal refusal)
        {
            if (refusal == StrikeRefusal.None)
            {
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal,
                    "A refusal needs a reason: it is sent to the attacker, who watched their weapon swing.");
            }

            return new StrikeVerdict(false, 0, refusal);
        }

        public bool Equals(StrikeVerdict other) =>
            Landed == other.Landed && Damage.Equals(other.Damage) && Refusal == other.Refusal;

        public override bool Equals(object? obj) => obj is StrikeVerdict other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Landed ? 1 : 0;
                hash = hash * 397 ^ Damage.GetHashCode();
                return hash * 397 ^ (int)Refusal;
            }
        }

        public override string ToString() =>
            Landed
                ? string.Format(CultureInfo.InvariantCulture, "landed for {0:0.#}", Damage)
                : "refused: " + StrikeRefusals.Sentence(Refusal);
    }
}
