using System;

namespace Lms.Core.Settings
{
    /// <summary>
    ///     Server rule for who may see beacons. The game draws beacons through terrain at any range, so
    ///     visible beacons would reveal contestants' positions; ghosts (casters) still see every beacon.
    /// </summary>
    public sealed class BeaconRule
    {
        private BeaconRule(bool hides)
        {
            Hides = hides;
        }

        /// <summary>Beacons are for ghosts only.</summary>
        public static BeaconRule Hidden { get; } = new BeaconRule(true);

        /// <summary>The game's own behaviour.</summary>
        public static BeaconRule Shown { get; } = new BeaconRule(false);

        /// <summary>Picks the rule a flag asks for.</summary>
        public static BeaconRule For(bool hideBeacons) => hideBeacons ? Hidden : Shown;

        /// <summary>Whether this rule takes beacons away from contestants at all.</summary>
        public bool Hides { get; }

        /// <summary>Whether the client looking at it may draw one.</summary>
        public bool Draws(bool isGhost) => !Hides || isGhost;

        /// <summary>
        ///     Whether hiding this one is worth a record: only a beacon a contestant turned back on, since
        ///     hiding happens to every beacon every tick.
        /// </summary>
        public bool Refuses(bool wasVisible, bool isGhost) => wasVisible && !Draws(isGhost);

        public override string ToString() =>
            Hides
                ? "beacons are hidden from contestants and drawn for ghosts"
                : "beacons are drawn, as the game draws them";
    }
}
