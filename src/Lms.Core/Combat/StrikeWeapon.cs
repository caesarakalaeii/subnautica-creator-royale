namespace Lms.Core.Combat
{
    /// <summary>
    ///     What a swing was made with: every weapon that can hit a player.
    /// </summary>
    /// <remarks>
    ///     Member names never travel; the wire and audit words live in <see cref="StrikeWeapons"/>.
    /// </remarks>
    public enum StrikeWeapon
    {
        /// <summary>The prawn's punch. 50 in the game's own numbers, on a one-second animation cooldown.</summary>
        Claw,

        /// <summary>
        ///     The prawn's drill, which hits repeatedly while it is held against something.
        /// </summary>
        /// <remarks>
        ///     4 per tick in the game's own numbers, but ticks fire from an animation event at an
        ///     unmeasured rate.
        /// </remarks>
        Drill,

        /// <summary>
        ///     The survival knife. 25 in the game's own numbers, halved to 12.5 by Nitrox's relay.
        /// </summary>
        /// <remarks>
        ///     Both blades run through <c>Knife.OnToolUseAnim</c> and are told apart on the client by
        ///     type, because <c>HeatBlade</c> does not override it.
        /// </remarks>
        Knife,

        /// <summary>
        ///     The heat blade, which in the game's own code is a <c>Knife</c> with a glow.
        /// </summary>
        /// <remarks>
        ///     It overrides no damage, so the tournament gives it its own damage value rather than a
        ///     multiple of the knife's.
        /// </remarks>
        HeatBlade,
    }
}
