using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Membranes
{
    /// <summary>
    ///     Stops vehicles being a way out of the ring: a caught pilot is put out of their seat, and
    ///     a caught player cannot climb into anything.
    /// </summary>
    /// <remarks>Vehicles themselves are never contained; they pass through the shell.</remarks>
    public static class VehicleRule
    {
        private static readonly IReadOnlyList<VehicleStep> LeaveOnly =
            new ReadOnlyCollection<VehicleStep>(new[] { VehicleStep.LeaveThePilotSeat });

        private static readonly IReadOnlyList<VehicleStep> DetachOnly =
            new ReadOnlyCollection<VehicleStep>(new[] { VehicleStep.DetachFromTheSub });

        private static readonly IReadOnlyList<VehicleStep> Both =
            new ReadOnlyCollection<VehicleStep>(new[] { VehicleStep.LeaveThePilotSeat, VehicleStep.DetachFromTheSub });

        /// <summary>What the ring has to undo before it is holding a swimmer.</summary>
        /// <param name="isContained">Whether the ring has caught this player.</param>
        /// <param name="ride">What they are sitting in.</param>
        public static VehicleOrder For(bool isContained, PlayerRide ride)
        {
            if (!isContained)
            {
                return default;
            }

            if (ride.IsPiloting)
            {
                return new VehicleOrder(ride.IsAboardASub ? Both : LeaveOnly);
            }

            return ride.IsAboardASub ? new VehicleOrder(DetachOnly) : default;
        }

        /// <summary>
        ///     Whether a player may climb into a vehicle. Enforced at <c>Vehicle.CanPilot()</c> (hides
        ///     the prompt) and <c>Player.EnterPilotingMode</c> (hard gate).
        /// </summary>
        public static bool MayBoard(bool isContained) => !isContained;
    }
}
