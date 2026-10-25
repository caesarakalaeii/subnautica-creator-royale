namespace Lms.Core.Membranes
{
    /// <summary>What one frame of movement did to the ring's wall.</summary>
    public enum MembraneCrossingKind
    {
        /// <summary>Nowhere near it: outside at both ends, and never touched it in between.</summary>
        Clear,

        /// <summary>Inside at both ends. A sphere is convex, so there is no way out and back in.</summary>
        Inside,

        /// <summary>Outside, then inside. The moment the ring starts holding this player.</summary>
        Entered,

        /// <summary>
        ///     Inside, then outside: what the shell collider prevents, caught here when physics fails.
        /// </summary>
        Exited,

        /// <summary>
        ///     Outside at both ends, but through the field on the way. Counts as having been in the ring.
        /// </summary>
        PassedThrough,
    }
}
