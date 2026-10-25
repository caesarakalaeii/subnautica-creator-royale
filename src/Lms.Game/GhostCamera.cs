using System;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Spectating;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Gives a ghost the game's free camera and parks their body out of the way; <c>unghost</c>
    ///     reverses both.
    /// </summary>
    /// <remarks>
    ///     Uses the same <see cref="FreeCamera"/> as the spectator. While the camera is out
    ///     <c>MainCameraControl</c> is disabled, so Nitrox's <c>PlayerMovementBroadcaster</c> stops
    ///     sending this player's position and other clients keep the last one.
    /// </remarks>
    internal sealed class GhostCamera : MonoBehaviour
    {
        /// <summary>
        ///     How far above sea level a ghost's body is parked, in metres; nothing in the world is
        ///     that high. A backstop in case <see cref="RemotePlayerVisibility"/> fails.
        /// </summary>
        private const float ParkedAt = 500f;

        private LmsRuntime? runtime;
        private CasterSightings? sightings;

        /// <summary>Who this camera was following as of last frame, so a change in it is an edge.</summary>
        private string onCamera = string.Empty;

        /// <summary>
        ///     Where that player was last frame, so the shot rides their motion (<see cref="CasterShot.Keep"/>).
        /// </summary>
        private WorldPoint lastTarget;

        /// <summary>Where the body was standing when the operator made them a ghost.</summary>
        private Vector3 bodyWas;

        /// <summary>
        ///     Whether this component handed the camera over, as opposed to it merely being free.
        /// </summary>
        /// <remarks>
        ///     Not <see cref="FreeCamera.IsFlying"/>: a developer's own <c>freecam</c> must not be moved
        ///     or released by the tournament.
        /// </remarks>
        private bool hasCamera;

        /// <summary>Whether the body has been put up out of the way yet.</summary>
        private bool parked;

        /// <summary>The role as of last frame, so a change in it is an edge to act on.</summary>
        private bool wasGhost;

        /// <summary>
        ///     Jumps this camera to where the booth said to look (<c>lms look</c>), with an on-screen caption.
        /// </summary>
        /// <remarks>
        ///     Refused unless this component holds the camera (see <see cref="hasCamera"/>).
        /// </remarks>
        internal void LookAt(LookBroadcast look)
        {
            if (!hasCamera)
            {
                Log.Info($"{LmsMod.LogPrefix} the booth asked this camera to look at {look.Name}, " +
                         "but this client is not flying a ghost's camera. Nothing moved.");
                return;
            }

            Vector3 where = new Vector3((float)look.Where.X, (float)look.Where.Y, (float)look.Where.Z);

            if (!FreeCamera.PlaceAt(where))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not put the camera on {look.Name}.");
                return;
            }

            Log.Info($"{LmsMod.LogPrefix} camera on {look.Name} at {look.Where}.");
            ErrorMessage.AddMessage($"LMS: camera on {look.Name}.");
        }

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            sightings = GetComponent<CasterSightings>();

            if (runtime == null)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (runtime == null || Player.main == null)
            {
                return;
            }

            // Act on the edge: the role can change mid-session, and each direction must land once.
            if (runtime.IsGhost != wasGhost)
            {
                wasGhost = runtime.IsGhost;

                if (wasGhost)
                {
                    Take();
                }
                else
                {
                    GiveBack();
                }

                return;
            }

            if (!hasCamera)
            {
                return;
            }

            if (!parked)
            {
                // Wait until the camera is actually flying: the toggle lands next frame, and the lock
                // that stops the body falling comes with it.
                if (FreeCamera.IsFlying)
                {
                    Park();
                }

                return;
            }

            // Every frame: anything re-enabling the controller would drop the parked body.
            KeepTheBodyStill();

            // After the body, so the camera never follows a body that is still falling.
            KeepTheShot();
        }

        /// <summary>
        ///     Keeps this camera on whoever the booth said to follow.
        /// </summary>
        /// <remarks>
        ///     Cuts to the target on a new follow, then rides their motion and clamps the distance
        ///     (<see cref="CasterShot"/>), leaving the caster free to frame inside the band. A name not
        ///     yet in the feed is waited for.
        /// </remarks>
        private void KeepTheShot()
        {
            if (sightings == null)
            {
                return;
            }

            FollowBroadcast follow = sightings.Follow;

            if (!follow.Following)
            {
                if (onCamera.Length > 0)
                {
                    onCamera = string.Empty;
                    ErrorMessage.AddMessage("LMS: the camera is yours again.");
                }

                return;
            }

            if (!sightings.TryFind(follow.Name, out CasterSighting seen))
            {
                return;
            }

            MainCameraControl camera = MainCameraControl.main;
            if (camera == null)
            {
                return;
            }

            WorldPoint target = seen.Where;
            Vector3 was = camera.transform.position;
            WorldPoint here = new WorldPoint(was.x, was.y, was.z);

            bool opening = !string.Equals(onCamera, follow.Name, StringComparison.Ordinal);

            // On the cut there is no previous target position; CasterShot.Open picks the distance.
            WorldPoint targetWas = opening ? target : lastTarget;

            WorldPoint want = opening
                ? CasterShot.Open(here, target, Shot.FollowDistance, Shot.FollowHeight)
                : CasterShot.Keep(here, target, targetWas, Shot.FollowClosest, Shot.FollowFurthest);

            camera.transform.position = new Vector3((float)want.X, (float)want.Y, (float)want.Z);

            // Every frame, so the shot stays on a moving target.
            camera.transform.LookAt(new Vector3((float)target.X, (float)target.Y, (float)target.Z));

            if (opening)
            {
                onCamera = follow.Name;

                Log.Info($"{LmsMod.LogPrefix} the camera is staying on {follow.Name}.");
                ErrorMessage.AddMessage($"LMS: staying on {follow.Name}.");
            }

            lastTarget = target;
        }

        /// <summary>The shot this client frames a follow with. See <see cref="CasterPolicy"/>.</summary>
        private CasterPolicy Shot => runtime?.Config?.Caster ?? CasterPolicy.Default;

        private void Take()
        {
            if (!FreeCamera.IsAvailable)
            {
                // A ghost without a free camera keeps the other immunities and films from the body.
                Log.Warn($"{LmsMod.LogPrefix} you are a ghost, but this client has no free camera to " +
                         "give you; filming from the body instead.");
                return;
            }

            if (!FreeCamera.Take())
            {
                Log.Warn($"{LmsMod.LogPrefix} you are a ghost and the camera would not hand over; " +
                         "filming from the body instead.");
                return;
            }

            hasCamera = true;

            // The free camera will not fly until the cursor is locked.
            FreeCamera.LockTheCursor();

            // It starts on its slowest speed, 0.4 m/s.
            FreeCamera.SetSpeed(FlySpeed);

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} you are a ghost: the camera is free and your body is going up out ",
                $"of the way, flying at {FlySpeed:0.#} m/s."));
            ErrorMessage.AddMessage("LMS: you are a ghost. The camera is yours — 1-5 sets the speed, shift is faster.");
        }

        /// <summary>
        ///     Puts the body up out of the way, and remembers where it was.
        /// </summary>
        /// <remarks>
        ///     Local only: position broadcasting has already stopped, so the server keeps the real spot.
        /// </remarks>
        private void Park()
        {
            parked = true;
            bodyWas = Player.main.transform.position;

            // Before the move: an enabled controller makes the body fall.
            KeepTheBodyStill();

            Player.main.SetPosition(new Vector3(bodyWas.x, ParkedAt, bodyWas.z));

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} your body is parked at {ParkedAt:0} m while you film; ",
                $"unghost puts it back at {bodyWas}."));
        }

        /// <summary>Disables the player controller, which is what walks, swims and falls.</summary>
        private void KeepTheBodyStill()
        {
            PlayerController controller = Player.main.playerController;

            if (controller != null && controller.enabled)
            {
                controller.SetEnabled(false);
            }
        }

        private void GiveBack()
        {
            if (parked)
            {
                // Body first, so the view returns to a player already home.
                parked = false;
                Player.main.SetPosition(bodyWas);

                PlayerController controller = Player.main.playerController;
                if (controller != null && !controller.enabled)
                {
                    controller.SetEnabled(true);
                }
            }

            if (!hasCamera)
            {
                return;
            }

            hasCamera = false;

            // The follow goes with the camera.
            onCamera = string.Empty;

            if (FreeCamera.IsFlying)
            {
                FreeCamera.Release();
            }

            Log.Info($"{LmsMod.LogPrefix} back in the match: the camera is yours again, and so is your body.");
            ErrorMessage.AddMessage("LMS: you are in the match again.");
        }

        /// <summary>
        ///     How fast the ghost's camera flies, in metres a second; shares <c>spectator.flySpeed</c>.
        /// </summary>
        private double FlySpeed
        {
            get
            {
                TournamentConfig? config = runtime?.Config;

                return config == null
                    ? SpectatorPolicy.Default.FlySpeed
                    : config.Spectator.FlySpeed;
            }
        }
    }
}
