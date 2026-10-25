using System;
using System.Collections.Generic;
using Lms.Core.Telemetry;
using Lms.Core.Zones;

namespace Lms.Core.Combat
{
    /// <summary>
    ///     Decides whether a reported swing landed, and for how much.
    /// </summary>
    /// <remarks>
    ///     The single judge for every weapon family; family differences come from the
    ///     <see cref="StrikePolicy"/>. Pure: vehicle, inventory and equipment facts are read from the
    ///     server's roster and passed in.
    /// </remarks>
    public static class StrikeRule
    {
        /// <summary>
        ///     Judges one swing. Refusals are checked in a fixed order, from tournament-wide facts down
        ///     to this moment, so the attacker is told the outermost cause.
        /// </summary>
        /// <param name="policy">
        ///     The weapon family's policy, picked by <see cref="StrikeWeapons.Family"/>.
        /// </param>
        /// <param name="weapon">What was swung, which decides the number.</param>
        /// <param name="attacker">Where the attacker is, from the server's own roster.</param>
        /// <param name="victim">Where the named player is, from the same roster reading.</param>
        /// <param name="attackerIsHeld">Whether a ring has the attacker.</param>
        /// <param name="attackerIsInAVehicle">
        ///     Whether the attacker is sitting in a vehicle. Only consulted when
        ///     <see cref="StrikePolicy.AVehicleDisarms"/> is true.
        /// </param>
        /// <param name="attackerIsArmed">
        ///     Whether the server's roster has the attacker holding, or sitting in, what they say
        ///     they swung.
        /// </param>
        /// <param name="matchIsLive">Whether a match is actually running.</param>
        /// <param name="victimIsInTheMatch">Whether the victim is still playing.</param>
        /// <param name="sinceLastStrike">
        ///     How long since this attacker's last landed hit.
        ///     <see cref="TimeSpan.MaxValue"/> for the first one.
        /// </param>
        /// <param name="attackerIsAGhost">Whether the attacker is a caster.</param>
        /// <param name="victimEquipment">What the victim is wearing, for the policy's resistances.</param>
        public static StrikeVerdict Judge(
            StrikePolicy policy,
            StrikeWeapon weapon,
            WorldPoint attacker,
            WorldPoint victim,
            bool attackerIsHeld,
            bool attackerIsInAVehicle,
            bool attackerIsArmed,
            bool matchIsLive,
            bool victimIsInTheMatch,
            TimeSpan sinceLastStrike,
            bool attackerIsAGhost = false,
            IReadOnlyList<EquipmentLine>? victimEquipment = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (!policy.HurtsPlayers)
            {
                return StrikeVerdict.Refused(policy.HarmlessRefusal);
            }

            double damage = policy.Damage(weapon);
            if (!(damage > 0))
            {
                // Zero damage is refused by name rather than recorded as a hit for nothing.
                return StrikeVerdict.Refused(policy.DisabledRefusal);
            }

            if (!matchIsLive)
            {
                return StrikeVerdict.Refused(StrikeRefusal.MatchIsNotRunning);
            }

            // First among attacker facts: casters are filtered out of the rows the later facts
            // come from.
            if (attackerIsAGhost)
            {
                return StrikeVerdict.Refused(StrikeRefusal.YouAreAGhost);
            }

            // The ring beats every weapon, before anything about the victim.
            if (attackerIsHeld)
            {
                return StrikeVerdict.Refused(StrikeRefusal.TheRingHasYou);
            }

            // Before being armed: boarding a vehicle holsters the held tool, so the vehicle is the
            // cause. A prawn's policy answers false here.
            if (policy.AVehicleDisarms && attackerIsInAVehicle)
            {
                return StrikeVerdict.Refused(StrikeRefusal.YouAreInAVehicle);
            }

            // After the ring: the ring ejects a held player from a prawn, so both can be true.
            if (!attackerIsArmed)
            {
                return StrikeVerdict.Refused(policy.NotArmedRefusal);
            }

            if (!victimIsInTheMatch)
            {
                return StrikeVerdict.Refused(StrikeRefusal.TheyAreAlreadyOut);
            }

            // Negated so a NaN position (a roster row can carry one) fails rather than passes.
            if (!(attacker.DistanceTo(victim) <= policy.Reach))
            {
                return StrikeVerdict.Refused(StrikeRefusal.OutOfRange);
            }

            // Negated likewise; a reset moves the clock backwards, so a negative gap must fail too.
            if (!(sinceLastStrike >= policy.MinInterval))
            {
                return StrikeVerdict.Refused(StrikeRefusal.TooSoon);
            }

            // Resistance scales a landed hit and never acts as a gate. Families with null
            // resistances (prawn) ignore equipment.
            if (victimEquipment != null && policy.Resistances != null)
            {
                foreach (EquipmentLine worn in victimEquipment)
                {
                    if (policy.Resistances.TryGetValue(worn.Item, out double multiplier))
                    {
                        damage *= multiplier;
                    }
                }
            }

            return StrikeVerdict.Hit(damage);

        }
    }
}
