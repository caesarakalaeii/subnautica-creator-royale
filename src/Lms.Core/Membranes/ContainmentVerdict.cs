namespace Lms.Core.Membranes
{
    /// <summary>What the ring has to say about one player this frame.</summary>
    public enum ContainmentVerdict
    {
        /// <summary>Not in the ring and not held by it. The collider stays off for this player.</summary>
        Clear,

        /// <summary>
        ///     Newly in the ring this frame: the collider goes on and a pilot is dismounted.
        /// </summary>
        Caught,

        /// <summary>Caught, and still inside the wall. Nothing to do.</summary>
        Held,

        /// <summary>
        ///     Caught, but outside the wall right now (physics glitch, or the closing wall passed
        ///     them). Still contained; they need putting back.
        /// </summary>
        Escaped,

        /// <summary>
        ///     The field ended and let them go. Reported once, only to players it was holding.
        /// </summary>
        Released,
    }
}
