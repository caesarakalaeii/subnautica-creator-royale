using System;

namespace Lms.Core.Spectating
{
    /// <summary>
    ///     One player's state after elimination: death cam, then free camera or removal.
    /// </summary>
    /// <remarks>
    ///     The adapter feeds it the frame delta and applies the returned <see cref="SpectatorChange"/>.
    ///     A player is out of the match from the moment they die, so zone damage and rings stop applying
    ///     during the death cam.
    /// </remarks>
    public sealed class SpectatorSession
    {
        /// <summary>Not readonly: <see cref="Adopt"/> swaps it when the server replaces the rules.</summary>
        private SpectatorPolicy policy;

        private TimeSpan sinceDeath;

        public SpectatorSession(SpectatorPolicy policy)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public SpectatorState State { get; private set; } = SpectatorState.Playing;

        /// <summary>Still in the match, and therefore still subject to everything in it.</summary>
        public bool IsPlaying => State == SpectatorState.Playing;

        /// <summary>True once the camera has been handed over and the body left behind.</summary>
        public bool IsSpectating => State == SpectatorState.Spectating;

        /// <summary>Whether the wall may still take health off them.</summary>
        public bool TakesZoneDamage => IsPlaying;

        /// <summary>Whether a ring may still hold them.</summary>
        public bool CanBeCaged => IsPlaying;

        /// <summary>
        ///     Remaining death-cam time, for the HUD. Zero outside the death cam.
        /// </summary>
        public TimeSpan UntilFreeCamera
        {
            get
            {
                if (State != SpectatorState.DeathCam)
                {
                    return TimeSpan.Zero;
                }

                TimeSpan left = policy.DeathCam - sinceDeath;

                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>
        ///     Marks the player eliminated and starts the death cam. Idempotent: kills are decided locally,
        ///     so a second report of the same death must not restart the cam.
        /// </summary>
        public SpectatorChange Eliminate()
        {
            if (State != SpectatorState.Playing)
            {
                return SpectatorChange.Nothing;
            }

            State = SpectatorState.DeathCam;
            sinceDeath = TimeSpan.Zero;

            return SpectatorChange.DeathCamStarted;
        }

        /// <summary>
        ///     The server says this player was already out when they (re)joined.
        /// </summary>
        /// <remarks>
        ///     Skips the death cam, which would otherwise give a returning player five seconds of normal
        ///     gameplay. Does nothing if a local death cam is already running. Honours
        ///     <see cref="SpectatorPolicy.Spectate"/> the same way a death does.
        /// </remarks>
        public SpectatorChange AlreadyOut()
        {
            if (State != SpectatorState.Playing)
            {
                return SpectatorChange.Nothing;
            }

            sinceDeath = TimeSpan.Zero;

            if (!policy.Spectate)
            {
                State = SpectatorState.Removed;

                return SpectatorChange.Removed;
            }

            State = SpectatorState.Spectating;

            return SpectatorChange.FreeCameraTaken;
        }

        /// <summary>
        ///     A new match started (<c>MatchDirector.Generation</c> changed); puts the player back in.
        ///     A removed player is not restored.
        /// </summary>
        public SpectatorChange Restore()
        {
            if (State == SpectatorState.Playing || State == SpectatorState.Removed)
            {
                return SpectatorChange.Nothing;
            }

            State = SpectatorState.Playing;
            sinceDeath = TimeSpan.Zero;

            return SpectatorChange.Restored;
        }

        /// <summary>
        ///     Swaps in rules the server replaced, keeping this player's state and death-cam progress.
        /// </summary>
        /// <remarks>
        ///     Swapped in place rather than rebuilt, because every client component holds a reference to
        ///     this session.
        /// </remarks>
        public void Adopt(SpectatorPolicy policy) =>
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));

        /// <summary>Advances the death cam and says what fell due.</summary>
        public SpectatorChange Update(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "Match time does not run backwards.");
            }

            if (State != SpectatorState.DeathCam)
            {
                return SpectatorChange.Nothing;
            }

            sinceDeath += delta;
            if (sinceDeath < policy.DeathCam)
            {
                return SpectatorChange.Nothing;
            }

            if (!policy.Spectate)
            {
                State = SpectatorState.Removed;

                return SpectatorChange.Removed;
            }

            State = SpectatorState.Spectating;

            return SpectatorChange.FreeCameraTaken;
        }

        public override string ToString() =>
            State == SpectatorState.DeathCam
                ? $"{State}, free camera in {UntilFreeCamera}"
                : State.ToString();
    }
}
