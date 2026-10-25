using System;

namespace Lms.Core.Spectating
{
    /// <summary>Settings for what happens to an eliminated player.</summary>
    public sealed class SpectatorPolicy
    {
        /// <summary>
        ///     8 m/s, <c>FreecamController</c>'s fourth of five speeds. The game's default (0.4 m/s) is
        ///     too slow to cross the map; keys 1–5 still change it live.
        /// </summary>
        public const double DefaultFlySpeed = 8;

        public SpectatorPolicy(TimeSpan deathCam, bool spectate, double flySpeed = DefaultFlySpeed)
        {
            if (deathCam < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(deathCam), deathCam,
                    "A death cam cannot run before the death.");
            }

            if (!(flySpeed > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(flySpeed), flySpeed,
                    "A spectator who cannot move is not spectating. Write metres per second, or " +
                    "spectate: false to be seen out instead.");
            }

            DeathCam = deathCam;
            Spectate = spectate;
            FlySpeed = flySpeed;
        }

        /// <summary>
        ///     Five-second death cam, then free-flying. Five matches the game's own
        ///     <c>ResetPlayerOnDeath(5f)</c> in <c>Player.OnKill</c>.
        /// </summary>
        public static SpectatorPolicy Default { get; } = new SpectatorPolicy(TimeSpan.FromSeconds(5), true);

        /// <summary>How long the game's death view holds before the free camera takes over.</summary>
        public TimeSpan DeathCam { get; }

        /// <summary>
        ///     Whether an eliminated player flies, or is removed once the death cam is done.
        /// </summary>
        public bool Spectate { get; }

        /// <summary>
        ///     Starting free-camera speed with a movement key held, in metres per second.
        /// </summary>
        public double FlySpeed { get; }
    }
}
