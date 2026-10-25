namespace Lms.Core.Combat
{
    /// <summary>
    ///     Why the server did not believe a swing.
    /// </summary>
    /// <remarks>
    ///     The game plays a weapon's animation and hit sound whatever the server decides, so every
    ///     refusal is sent back to the attacker with a reason.
    /// </remarks>
    public enum StrikeRefusal
    {
        /// <summary>Nothing was refused: the swing landed.</summary>
        None,

        /// <summary>
        ///     <c>prawn.hurtsPlayers</c> is off.
        /// </summary>
        PrawnsAreHarmless,

        /// <summary>
        ///     <c>blade.hurtsPlayers</c> is off.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="PrawnsAreHarmless"/> so each family is named in its own refusal.
        /// </remarks>
        BladesAreHarmless,

        /// <summary>
        ///     That arm's damage is zero, so it is switched off for the whole tournament.
        /// </summary>
        ArmIsDisabled,

        /// <summary>
        ///     That blade's damage is zero.
        /// </summary>
        BladeIsDisabled,

        /// <summary>The lobby, or a pause. The wall does not bite then either.</summary>
        MatchIsNotRunning,

        /// <summary>
        ///     The attacker is a caster, and the match cannot see them at all.
        /// </summary>
        /// <remarks>
        ///     Checked before every other fact about the attacker, since casters are filtered out of
        ///     the rows those facts come from.
        /// </remarks>
        YouAreAGhost,

        /// <summary>A ring has the attacker. The ring beats the prawn.</summary>
        TheRingHasYou,

        /// <summary>
        ///     The server's roster does not have the attacker in a prawn suit at all.
        /// </summary>
        NotInAPrawn,

        /// <summary>
        ///     The server's roster does not have the attacker holding the blade they reported.
        /// </summary>
        /// <remarks>
        ///     The held item is the client's own claim, so this stops an empty-handed swing but not a
        ///     modified client that fakes the field.
        /// </remarks>
        NotHoldingThatBlade,

        /// <summary>
        ///     The attacker is sitting in something, and boarding holsters what you were holding.
        /// </summary>
        /// <remarks>
        ///     Checked before <see cref="NotHoldingThatBlade"/> because it is the cause. Never reached
        ///     by a prawn (<see cref="StrikePolicy.AVehicleDisarms"/> is false).
        /// </remarks>
        YouAreInAVehicle,

        /// <summary>The victim is already out of the match, and a corpse is not billed.</summary>
        TheyAreAlreadyOut,

        /// <summary>Further apart than <c>prawn.reach</c> believes.</summary>
        OutOfRange,

        /// <summary>Sooner after the last landed hit than <c>minInterval</c> believes.</summary>
        TooSoon,

        /// <summary>
        ///     The server could not decode the swing report: the two builds differ.
        /// </summary>
        /// <remarks>
        ///     Not a verdict of <see cref="StrikeRule.Judge"/>; sent by the packet pump, once per
        ///     player per session.
        /// </remarks>
        TheBuildsDiffer,
    }
}
