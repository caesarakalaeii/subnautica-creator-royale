namespace Lms.Core.Spectating
{
    /// <summary>
    ///     What changed this frame, for the adapter to act on. Each change is reported exactly once.
    /// </summary>
    public enum SpectatorChange
    {
        /// <summary>Nothing to do.</summary>
        Nothing,

        /// <summary>
        ///     They died. The game plays its own death cam; the adapter records the elimination and stops
        ///     broadcasting the player's position.
        /// </summary>
        DeathCamStarted,

        /// <summary>
        ///     Take the camera: detach it, disable <c>MainCameraControl</c>, push an input dummy and
        ///     lock the player where they fell, the way <c>FreecamController</c> does.
        /// </summary>
        FreeCameraTaken,

        /// <summary>Spectating is disabled: remove them from the session.</summary>
        Removed,

        /// <summary>A new match started. Give the camera back and put them in it.</summary>
        Restored,
    }
}
