using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lms.Core.Membranes
{
    /// <summary>The steps that turn a caught player back into a swimmer, in order.</summary>
    /// <remarks>
    ///     Order matters: Nitrox's <c>Player_ExitPilotingMode_Patch</c> re-parents a Cyclops pilot
    ///     into the sub, so detaching before leaving the seat is undone. Landing is
    ///     <see cref="SafePlacement"/>'s job.
    /// </remarks>
    public readonly struct VehicleOrder
    {
        private static readonly IReadOnlyList<VehicleStep> Nothing = new ReadOnlyCollection<VehicleStep>(new VehicleStep[0]);

        private readonly IReadOnlyList<VehicleStep>? steps;

        internal VehicleOrder(IReadOnlyList<VehicleStep> steps)
        {
            this.steps = steps;
        }

        /// <summary>The calls to make, in order. Empty for a player who was already a swimmer.</summary>
        public IReadOnlyList<VehicleStep> Steps => steps ?? Nothing;

        public bool AnythingToDo => Steps.Count > 0;
    }
}
