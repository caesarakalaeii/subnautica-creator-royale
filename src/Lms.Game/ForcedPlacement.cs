using System.Collections.Generic;
using Lms.Core.Membranes;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Turns a caught player back into a swimmer and puts them somewhere inside the ring.
    /// </summary>
    /// <remarks>
    ///     Order matters: Nitrox's <c>Player_ExitPilotingMode_Patch</c> re-parents a Cyclops pilot
    ///     into the sub, so leaving the seat must come before detaching (see <see cref="VehicleRule"/>).
    ///     <c>ToNormalMode(findNewPosition: false)</c> bypasses <c>CanEject()</c> and the game's own placement.
    /// </remarks>
    internal static class ForcedPlacement
    {
        /// <summary>
        ///     Roughly the player's girth: <c>PlayerController.controllerRadius</c> is 0.3 m, plus slack.
        /// </summary>
        private const float ClearanceRadius = 0.5f;

        private static int terrainMask = -1;

        /// <summary>What the player is sitting in, as Core's rule wants it.</summary>
        internal static PlayerRide RideOfLocalPlayer()
        {
            Player.Mode mode = Player.main.GetMode();

            return PlayerRide.Of(
                mode == Player.Mode.Piloting || mode == Player.Mode.LockedPiloting,
                Player.main.GetCurrentSub() != null);
        }

        /// <summary>
        ///     Applies the steps Core says a caught player needs, and returns whether any were
        ///     needed — which is also whether they have to be put somewhere.
        /// </summary>
        internal static bool Dismount(VehicleOrder order)
        {
            foreach (VehicleStep step in order.Steps)
            {
                switch (step)
                {
                    case VehicleStep.LeaveThePilotSeat:
                        Player.main.ToNormalMode(findNewPosition: false);
                        break;

                    case VehicleStep.DetachFromTheSub:
                        Player.main.SetCurrentSub(null, forced: true);
                        break;
                }
            }

            return order.AnythingToDo;
        }

        /// <summary>Puts the player at the first of Core's candidates that is not inside rock.</summary>
        /// <param name="wall">The ring to place the player inside.</param>
        /// <param name="from">Where the player is now.</param>
        /// <param name="margin">How far inside the wall to land.</param>
        /// <param name="leftAVehicle">
        ///     Whether they had to be taken out of something first; Core uses it to choose between the
        ///     smallest move and the middle of the ring.
        /// </param>
        internal static void PutInside(ZoneSphere wall, WorldPoint from, double margin, bool leftAVehicle)
        {
            if (Player.main.liveMixin != null && !Player.main.liveMixin.IsAlive())
            {
                // A dead player is being handed to the death cam; leave the body.
                return;
            }

            IReadOnlyList<WorldPoint> candidates = SafePlacement.Landing(wall, from, margin, leftAVehicle);
            if (candidates.Count == 0)
            {
                // A ring with no inside has gone.
                return;
            }

            foreach (WorldPoint candidate in candidates)
            {
                Vector3 point = new Vector3((float)candidate.X, (float)candidate.Y, (float)candidate.Z);
                if (!IsInsideTerrain(point))
                {
                    Player.main.SetPosition(point);
                    return;
                }
            }

            // Every candidate is in rock: use the nearest; the game pushes players out of terrain.
            WorldPoint fallback = candidates[0];
            Player.main.SetPosition(new Vector3((float)fallback.X, (float)fallback.Y, (float)fallback.Z));
            Log.Warn($"{LmsMod.LogPrefix} every placement inside {wall} is in terrain; used the nearest anyway.");
        }

        /// <remarks>
        ///     Terrain only: overlapping a vehicle or base sorts itself out, and testing everything
        ///     would veto every point next to the vehicle just left.
        /// </remarks>
        private static bool IsInsideTerrain(Vector3 point)
        {
            if (terrainMask < 0)
            {
                terrainMask = 1 << LayerID.TerrainCollider;
            }

            return Physics.CheckSphere(point, ClearanceRadius, terrainMask, QueryTriggerInteraction.Ignore);
        }
    }
}
