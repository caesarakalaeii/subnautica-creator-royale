namespace Lms.Game
{
    /// <summary>
    ///     Whether any ring is holding the local player, for the static Harmony patches to read.
    /// </summary>
    internal static class RingHold
    {
        /// <summary>Set every frame by <see cref="RingEnforcer"/>, read by the boarding gate.</summary>
        internal static bool IsHeld { get; set; }
    }
}
