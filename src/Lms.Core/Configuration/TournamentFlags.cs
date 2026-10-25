namespace Lms.Core.Configuration
{
    /// <summary>Switches that change what the mod does to players.</summary>
    public sealed class TournamentFlags
    {
        internal TournamentFlags(bool enforceDamage, bool enforceContainment, bool hideBeacons,
            bool showDestination = false)
        {
            EnforceDamage = enforceDamage;
            EnforceContainment = enforceContainment;
            HideBeacons = hideBeacons;
            ShowDestination = showDestination;
        }

        /// <summary>
        ///     Whether being outside the wall costs health. Defaults to <c>false</c>, so a config
        ///     that omits it never starts hurting people.
        /// </summary>
        public bool EnforceDamage { get; }

        /// <summary>
        ///     Whether the boxing ring holds anybody. Defaults to <c>true</c>: being held is
        ///     recoverable. False leaves the stasis field behaving as in the base game.
        /// </summary>
        public bool EnforceContainment { get; }

        /// <summary>Whether beacons are hidden from contestants.</summary>
        /// <remarks>
        ///     Defaults to <c>true</c>: a hidden beacon is recoverable, a leaked position is not.
        ///     <see cref="Lms.Core.Settings.BeaconRule"/> is what the flag means.
        /// </remarks>
        public bool HideBeacons { get; }

        /// <summary>Whether a contestant is told, by name, where the wall is closing to.</summary>
        /// <remarks>
        ///     Defaults to <c>false</c>. A server flag rather than a client preference, so no
        ///     contestant can opt into the advantage; when off, the name is never sent.
        /// </remarks>
        public bool ShowDestination { get; }
    }
}
