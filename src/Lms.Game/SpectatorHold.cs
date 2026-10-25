namespace Lms.Game
{
    /// <summary>
    ///     Whether the local player's body is staying where it died, for the static Harmony prefix to read.
    /// </summary>
    /// <remarks>
    ///     Set on death, not at camera handover: <c>Player.ResetPlayerOnDeath</c> teleports the body
    ///     at the five-second mark, the same moment the handover falls due.
    /// </remarks>
    internal static class SpectatorHold
    {
        /// <summary>Set by <see cref="SpectatorAdapter"/>, read by <see cref="RespawnSuppression"/>.</summary>
        internal static bool KeepsTheBody { get; set; }
    }
}
