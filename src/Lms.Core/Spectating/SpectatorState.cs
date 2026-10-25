namespace Lms.Core.Spectating
{
    /// <summary>Where an eliminated player is in the spectating lifecycle.</summary>
    public enum SpectatorState
    {
        /// <summary>In the match.</summary>
        Playing,

        /// <summary>
        ///     Dead, during the game's death cam. Already out: zone damage and rings no longer apply.
        /// </summary>
        DeathCam,

        /// <summary>
        ///     Free camera. The body stays where it fell and the client stops broadcasting its position.
        /// </summary>
        Spectating,

        /// <summary>
        ///     Spectating is disabled by config: the player is removed after the death cam.
        /// </summary>
        Removed,
    }
}
