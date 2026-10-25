using System;
using System.Collections.Generic;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Per-family weapon settings that <see cref="StrikeRule.Judge"/> reads: switch, reach,
    ///     cooldown, damage and the refusals worded for that family.
    /// </summary>
    /// <remarks>
    ///     Each family names its own refusals so an attacker is never told about the wrong weapon.
    ///     Every family is off by default.
    /// </remarks>
    public abstract class StrikePolicy
    {
        /// <param name="hurtsPlayers">False and this family does nothing to anybody.</param>
        /// <param name="reach">
        ///     How far apart the server will believe a hit was, in metres. The subclass validates it
        ///     against the family's own floor first.
        /// </param>
        /// <param name="minInterval">The soonest a second hit from the same attacker is believed.</param>
        protected StrikePolicy(bool hurtsPlayers, double reach, TimeSpan minInterval)
        {
            if (double.IsNaN(reach) || double.IsInfinity(reach))
            {
                throw new ArgumentOutOfRangeException(nameof(reach), reach,
                    "A reach is a number of metres. A NaN would fail every distance comparison and " +
                    "read from the cockpit as a weapon that does not work.");
            }

            if (!(minInterval > TimeSpan.Zero))
            {
                throw new ArgumentOutOfRangeException(nameof(minInterval), minInterval,
                    "This is the anti-spam guard: with no interval a client can send a swing per " +
                    "packet and empty a health bar in a frame.");
            }

            HurtsPlayers = hurtsPlayers;
            Reach = reach;
            MinInterval = minInterval;
        }

        /// <summary>Whether this family of weapons hurts players at all.</summary>
        public bool HurtsPlayers { get; }

        /// <summary>
        ///     How far apart the server will believe a hit was, in metres.
        /// </summary>
        /// <remarks>
        ///     Measured between roster origins, not along the game's trace, so it must exceed the
        ///     game's reach by the body geometry.
        /// </remarks>
        public double Reach { get; }

        /// <summary>The soonest the server will believe a second hit from the same attacker.</summary>
        public TimeSpan MinInterval { get; }

        /// <summary>
        ///     Whether sitting in a vehicle takes this family out of an attacker's hands.
        /// </summary>
        public abstract bool AVehicleDisarms { get; }

        /// <summary>What one hit from <paramref name="weapon"/> costs. Zero means it is switched off.</summary>
        /// <remarks>
        ///     Throws on a weapon of another family rather than answering zero, which would be
        ///     reported as "that weapon is disabled".
        /// </remarks>
        public abstract double Damage(StrikeWeapon weapon);

        /// <summary>What the attacker is told when this whole family is switched off.</summary>
        public abstract StrikeRefusal HarmlessRefusal { get; }

        /// <summary>What the attacker is told when this one weapon's damage is zero.</summary>
        public abstract StrikeRefusal DisabledRefusal { get; }

        /// <summary>
        ///     What the attacker is told when the server's roster does not have them holding this.
        /// </summary>
        public abstract StrikeRefusal NotArmedRefusal { get; }

        /// <summary>
        ///     Damage multiplier per piece of the victim's equipment, by the game's item name, or null
        ///     for a family that ignores equipment.
        /// </summary>
        public virtual IReadOnlyDictionary<string, double>? Resistances => null;
    }
}
