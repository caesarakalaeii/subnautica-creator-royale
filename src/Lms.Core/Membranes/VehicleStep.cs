namespace Lms.Core.Membranes
{
    /// <summary>One call the adapter has to make to turn a caught player back into a swimmer.</summary>
    public enum VehicleStep
    {
        /// <summary>
        ///     <c>Player.ToNormalMode(findNewPosition: false)</c>. Not <c>TryEject()</c>, which refuses
        ///     a docked Seamoth; the flag skips the game's own choice of landing spot.
        /// </summary>
        LeaveThePilotSeat,

        /// <summary>
        ///     <c>Player.SetCurrentSub(null, forced: true)</c>, for an occupant who was not piloting.
        /// </summary>
        DetachFromTheSub,
    }
}
