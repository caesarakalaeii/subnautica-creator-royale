namespace Lms.Core.Membranes
{
    /// <summary>
    ///     What a player is riding when the ring catches them: two independent facts, each undone
    ///     by a different game call.
    /// </summary>
    /// <remarks>
    ///     Piloting is <c>Player.mode</c>; being aboard is <c>Player.currentSub</c>. A Seamoth
    ///     pilot is the first without the second, a Cyclops passenger the second without the first,
    ///     and a Cyclops pilot — or a Seamoth docked in one — is both.
    /// </remarks>
    public readonly struct PlayerRide
    {
        private PlayerRide(bool isPiloting, bool isAboardASub)
        {
            IsPiloting = isPiloting;
            IsAboardASub = isAboardASub;
        }

        /// <summary>Swimming or walking: the only state the ring governs directly.</summary>
        public static PlayerRide OnFoot => new PlayerRide(false, false);

        /// <summary>At the controls of a Seamoth or a Prawn.</summary>
        public static PlayerRide Piloting => new PlayerRide(true, false);

        /// <summary>Riding a Cyclops without steering it.</summary>
        public static PlayerRide AboardASub => new PlayerRide(false, true);

        /// <summary>At a Cyclops helm, or in a Seamoth docked inside one.</summary>
        public static PlayerRide PilotingASub => new PlayerRide(true, true);

        public static PlayerRide Of(bool isPiloting, bool isAboardASub) => new PlayerRide(isPiloting, isAboardASub);

        public bool IsPiloting { get; }

        public bool IsAboardASub { get; }
    }
}
