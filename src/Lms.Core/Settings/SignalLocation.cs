using System;

namespace Lms.Core.Settings
{
    /// <summary>
    ///     Whether a signal ping reveals another player's location (remote player marker or another
    ///     player's pod) rather than being a story signal, a mod ping, or the local player's own pod.
    /// </summary>
    /// <remarks>
    ///     Classified by object name and association, never by the localised label.
    /// </remarks>
    public static class SignalLocation
    {
        /// <summary>
        ///     The prefix the fork's <c>PlayerModelManager.AttachPing</c> puts on a remote
        ///     player's marker.
        /// </summary>
        private const string MarkerPrefix = "signal_";

        /// <summary>The anchor prefix the fork treats as the mod's own objects.</summary>
        private const string ModPrefix = "Lms";

        /// <summary>Whether the ping marks another player's position.</summary>
        public static bool IsALocation(string pingName, bool hasSignalPing, bool inAnotherPlayersPod)
        {
            string name = pingName ?? string.Empty;

            if (name.StartsWith(ModPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            // Must precede the SignalPing check: the fork removes the marker's story SignalPing only
            // after the ping registers. Story signals are prefab-named, so this cannot match one.
            if (IsMarker(name))
            {
                return true;
            }

            if (hasSignalPing)
            {
                return false;
            }

            return inAnotherPlayersPod;
        }

        /// <summary>
        ///     Whether the name is the fork's remote-player marker. Checked by name because the fork sets
        ///     <c>pingType</c> only after the ping registers.
        /// </summary>
        public static bool IsMarker(string pingName) =>
            (pingName ?? string.Empty).StartsWith(MarkerPrefix, StringComparison.Ordinal);
    }
}
