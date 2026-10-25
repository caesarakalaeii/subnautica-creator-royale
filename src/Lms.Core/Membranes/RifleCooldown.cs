using System;

namespace Lms.Core.Membranes
{
    /// <summary>
    ///     How long the stasis rifle is locked after a shot: exactly as long as the ring that shot leaves.
    /// </summary>
    /// <remarks>
    ///     <c>StasisRifle.sphere</c> is static, so firing again would move the existing field and free
    ///     whoever it held. One ring at a time.
    /// </remarks>
    public sealed class RifleCooldown
    {
        private readonly StasisFieldLimits limits;

        private TimeSpan readyAt;

        public RifleCooldown(StasisFieldLimits limits)
        {
            this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        /// <summary>Records a shot at a normalised charge (0 to 1), locking the rifle for the life of its field.</summary>
        public void Fired(TimeSpan at, double charge)
        {
            readyAt = at + limits.LifetimeFor(charge);
        }

        public bool IsReady(TimeSpan now) => now >= readyAt;

        /// <summary>What is left of the lock. Never negative.</summary>
        public TimeSpan Remaining(TimeSpan now)
        {
            TimeSpan left = readyAt - now;

            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>Clears the lock for a new match.</summary>
        public void Ready()
        {
            readyAt = TimeSpan.Zero;
        }
    }
}
