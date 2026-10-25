namespace Lms.Core.Zones
{
    /// <summary>What the zone says about where a player is.</summary>
    public enum ZoneVerdict
    {
        /// <summary>Inside the wall, and inside where it is going. Nothing to do.</summary>
        Inside,

        /// <summary>
        ///     Inside the wall but outside the sphere the next phase closes to: safe now, but standing
        ///     where the zone is going.
        /// </summary>
        Warning,

        /// <summary>Outside the wall. Taking a bite every interval until they are back in or dead.</summary>
        Outside,
    }
}
