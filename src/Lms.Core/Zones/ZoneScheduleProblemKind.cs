namespace Lms.Core.Zones
{
    /// <summary>What is wrong with an authored squeeze.</summary>
    public enum ZoneScheduleProblemKind
    {
        /// <summary>
        ///     This phase's sphere is not entirely inside the one before it, so a player who was safe
        ///     may be unable to reach the new safe volume.
        /// </summary>
        PhaseEscapesItsPredecessor,

        /// <summary>The radius did not go down, so the match has a phase that squeezes nobody.</summary>
        PhaseDoesNotShrink,

        /// <summary>The wall would still be closing when the next phase fired.</summary>
        ShrinkOutlastsTheCycle,

        /// <summary>A phase hits no harder than the one before it, so the pressure comes off.</summary>
        DamageDoesNotEscalate,
    }
}
