using System;

namespace Lms.Core.Membranes
{
    /// <summary>What an operator can change about the stasis ring between matches.</summary>
    /// <remarks>
    ///     The adapter writes the limits onto the game's <c>StasisSphere</c>, so the visible bubble
    ///     matches the one that holds players.
    /// </remarks>
    public sealed class RingPolicy
    {
        public RingPolicy(StasisFieldLimits limits, double placementMargin, double energyCost)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }

            if (!(placementMargin >= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(placementMargin), placementMargin,
                    "A placement margin is a distance inside the wall.");
            }

            if (placementMargin >= limits.MaxRadius)
            {
                throw new ArgumentOutOfRangeException(nameof(placementMargin), placementMargin,
                    "A margin this wide leaves nowhere inside the biggest ring but the impact point, " +
                    "which is usually inside whatever the shot hit.");
            }

            if (!(energyCost > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(energyCost), energyCost,
                    "A ring has to cost something. The game divides by this to work out how charged " +
                    "the trigger is, so zero is not a free ring -- it is a rifle that never fires.");
            }

            Limits = limits;
            PlacementMargin = placementMargin;
            EnergyCost = energyCost;
        }

        /// <summary>
        ///     The vanilla rifle: 1 m for 4 s at a tap, 10 m for 20 s at a full charge, a metre of
        ///     margin, and five units of battery for a full charge.
        /// </summary>
        public static RingPolicy Default { get; } = new RingPolicy(StasisFieldLimits.GameDefaults, 1, 5);

        /// <summary>What a field can be, at the two ends of the trigger.</summary>
        public StasisFieldLimits Limits { get; }

        /// <summary>
        ///     How far inside the wall a player is put when the ring moves them; a player placed on
        ///     the wall can be pushed straight back out.
        /// </summary>
        public double PlacementMargin { get; }

        /// <summary>
        ///     Battery units a full-charge ring costs. At the game's five, a 100-unit battery is
        ///     twenty rings.
        /// </summary>
        /// <remarks>
        ///     Spent while charging, proportionally. Also the game's charge scale
        ///     (<c>chargeNormalized = chargeAmount / energyCost</c>), so zero is refused.
        /// </remarks>
        public double EnergyCost { get; }
    }
}
