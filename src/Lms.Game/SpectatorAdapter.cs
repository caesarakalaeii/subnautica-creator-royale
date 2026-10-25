using System;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Spectating;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Watches the local player die, and does what <see cref="SpectatorSession"/> says about it:
    ///     the game's own death view, then the camera off the body and away.
    /// </summary>
    /// <remarks>
    ///     No packet is needed: taking the free camera disables <c>MainCameraControl</c>, which makes
    ///     Nitrox's <c>PlayerMovementBroadcaster.Update</c> stop, so other clients keep the body at the death site.
    /// </remarks>
    internal sealed class SpectatorAdapter : MonoBehaviour
    {
        /// <summary>How long after the handover to look at what it did. A second is several frames.</summary>
        private const float HandoverCheckSeconds = 1f;

        /// <summary>
        ///     How many times to look, a second apart.
        /// </summary>
        /// <remarks>
        ///     Must outlast <c>ResetPlayerOnDeath</c>, which keeps resetting the player for a couple of
        ///     seconds after the handover.
        /// </remarks>
        private const int HandoverChecks = 10;

        private TournamentConfig? config;
        private SpectatorSession? session;
        private MatchFollower? match;

        private int matchGeneration;

        /// <summary>
        ///     Whether this client has seen the local player alive since the last reset.
        /// </summary>
        /// <remarks>
        ///     On a join the body exists before it has health; that frame is not a death.
        /// </remarks>
        private bool sawAlive;

        private bool statsFrozen;
        private bool madeInvincible;

        /// <summary>
        ///     When to look at what the handover actually did, or zero once it has been looked at.
        /// </summary>
        private float checkHandoverAt;

        /// <summary>How many readings are left to take.</summary>
        private int checksLeft;

        /// <summary>Whether this elimination has already reported a handover coming undone.</summary>
        private bool warnedAboutTheHandover;

        /// <summary>
        ///     A verdict the server sent that has not been acted on yet.
        /// </summary>
        /// <remarks>
        ///     It arrives on the packet pump before this client has a body or a match to check it
        ///     against, so it is held until a frame has both.
        /// </remarks>
        private EliminatedBroadcast? told;

        /// <summary>
        ///     Whether the camera has actually been the spectator's since this elimination.
        /// </summary>
        /// <remarks>
        ///     <c>FreecamController</c> toggles on its next frame (<c>toggleNextFrame</c>), so the first
        ///     frames after the handover still look like the game's camera.
        /// </remarks>
        private bool handoverSeen;

        private LmsRuntime? runtime;

        /// <summary>
        ///     Takes the server's word that this player is already out. Applied later; see <see cref="told"/>.
        /// </summary>
        internal void Told(EliminatedBroadcast verdict)
        {
            told = verdict;
        }

        /// <summary>Drops a pending verdict with the session.</summary>
        /// <remarks>
        ///     The state machine is not reset here; <see cref="Update"/> does that on a new generation.
        /// </remarks>
        internal void Forget()
        {
            told = null;
        }

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            config = runtime?.Config;
            session = runtime?.Spectator;
            match = runtime?.Follower;

            if (config == null || session == null || match == null)
            {
                // Without a config nobody is eliminated by this client.
                enabled = false;
                return;
            }

            runtime!.Reconfigured += Reconfigure;

            matchGeneration = match.Generation;

            if (!config.Spectator.Spectate)
            {
                // The game's own death view and respawn run unchanged.
                Log.Info($"{LmsMod.LogPrefix} spectating is off; an eliminated player will be respawned as usual.");
                return;
            }

            RespawnSuppression.Apply();

            // Prevents a throw for any detached camera, which Nitrox can reach from a packet processor.
            CinematicSuppression.Apply();
        }

        /// <summary>
        ///     Takes rules the server replaced. Fly speed and death-cam length are read at death time.
        /// </summary>
        private void Reconfigure()
        {
            config = runtime?.Config;
            session = runtime?.Spectator;
        }

        private void Update()
        {
            if (Player.main == null)
            {
                // Between sessions: unlatch so the next load in this process can respawn.
                SpectatorHold.KeepsTheBody = false;
                return;
            }

            if (matchGeneration != match!.Generation)
            {
                matchGeneration = match.Generation;
                Act(session!.Restore());
            }

            // After the restore: a fresh join looks like a new generation, and a verdict applied
            // first would be undone on the same frame.
            ApplyTheVerdict();

            WatchForDeath();

            Act(session!.Update(TimeSpan.FromSeconds(Time.deltaTime)));

            if (session.IsSpectating)
            {
                KeepTheBodyStill();
                NoticeAHandoverComingUndone();
            }

            ReportTheHandover();
        }

        /// <summary>
        ///     Logs the free camera's state a few times after the handover; the first reading also
        ///     moves the camera to the body and lists the mod's spheres in the world.
        /// </summary>
        private void ReportTheHandover()
        {
            if (checksLeft <= 0 || Time.time < checkHandoverAt)
            {
                return;
            }

            if (checksLeft == HandoverChecks)
            {
                // The toggle has happened by now, so placing the camera sticks; otherwise it starts
                // at the world origin.
                if (Player.main != null && FreeCamera.PlaceAt(Player.main.transform.position))
                {
                    // Formatted per component so it is culture-invariant and matches FreeCamera's at= line.
                    Vector3 body = Player.main.transform.position;
                    Log.Info(Readable.Line(
                        $"{LmsMod.LogPrefix} free camera moved to the body at ",
                        $"({body.x:0.0}, {body.y:0.0}, {body.z:0.0})."));
                }

                Log.Info($"{LmsMod.LogPrefix} spheres in the world: {FreeCamera.Spheres()}");
            }

            checksLeft--;
            checkHandoverAt = Time.time + HandoverCheckSeconds;

            Log.Info($"{LmsMod.LogPrefix} free camera: {FreeCamera.State()}");
        }

        /// <summary>
        ///     Warns once if the camera stops being the spectator's while they are still dead.
        /// </summary>
        /// <remarks>
        ///     Detects <c>ResetPlayerOnDeath</c> undoing the handover despite <see cref="RespawnSuppression"/>.
        /// </remarks>
        private void NoticeAHandoverComingUndone()
        {
            if (warnedAboutTheHandover || !SpectatorHold.KeepsTheBody)
            {
                return;
            }

            bool flying = FreeCamera.IsFlying;
            Player.Mode mode = Player.main.GetMode();
            bool theirs = flying && mode == Player.Mode.LockedPiloting;

            if (!handoverSeen)
            {
                handoverSeen = theirs;
                return;
            }

            if (theirs)
            {
                return;
            }

            warnedAboutTheHandover = true;
            Log.Warn($"{LmsMod.LogPrefix} the free camera stopped being the spectator's: " +
                     $"flying={flying} mode={mode}. They are dead, holding their own body, and no " +
                     "longer flying it — expect a camera at the world origin.");
        }

        /// <summary>
        ///     Acts on a verdict the server sent, once there is a body and a match to check it against.
        /// </summary>
        /// <remarks>
        ///     Goes straight to the free camera with no death cam, since the death happened in an
        ///     earlier session. Dropped if its group or generation is not the current match's.
        /// </remarks>
        private void ApplyTheVerdict()
        {
            if (told == null)
            {
                return;
            }

            EliminatedBroadcast verdict = told.Value;

            // The zone line precedes the verdict on the same channel, so this is only a wait.
            if (!match!.HasMatch)
            {
                return;
            }

            if (verdict.Group != match.Group || verdict.Generation != match.Generation)
            {
                told = null;
                Log.Info($"{LmsMod.LogPrefix} ignored an elimination for " +
                         $"{verdict.Group}.{verdict.Generation}; this client is in " +
                         $"{match.Group}.{match.Generation}.");
                return;
            }

            told = null;

            // A ghost already flies GhostCamera; this handover would fight it for the controller.
            if (runtime != null && runtime.IsGhost)
            {
                Log.Info($"{LmsMod.LogPrefix} the server says this player is out of " +
                         $"{verdict.Group}.{verdict.Generation}, but they are a ghost; nothing to do.");
                return;
            }

            // Both halves are checked before the body is held: a held body with no camera is worse
            // than a normal respawn.
            bool canSpectate = config!.Spectator.Spectate && RespawnSuppression.Active && FreeCamera.IsAvailable;
            SpectatorHold.KeepsTheBody = canSpectate;

            SpectatorChange change = session!.AlreadyOut();
            if (change == SpectatorChange.Nothing)
            {
                // Already out locally; the server's verdict trails the local kill.
                return;
            }

            Log.Info($"{LmsMod.LogPrefix} the server says group {verdict.Group}.{verdict.Generation} " +
                     $"is already finished with this player. " +
                     (canSpectate ? "Taking the camera." : "Spectating is off or unavailable."));

            if (canSpectate)
            {
                ErrorMessage.AddMessage("LMS: you are out of this round. The camera is yours — fly and watch.");
            }
            else if (config.Spectator.Spectate)
            {
                // Spectating is configured but unavailable: a broken install.
                Log.Warn($"{LmsMod.LogPrefix} this player is out and cannot be handed the free " +
                         $"camera: suppression={RespawnSuppression.Active} camera={FreeCamera.IsAvailable}. " +
                         "They will keep their body and take no further part in the match.");
            }

            Act(change);
        }

        private void WatchForDeath()
        {
            LiveMixin live = Player.main.liveMixin;
            if (live == null)
            {
                return;
            }

            if (live.IsAlive())
            {
                sawAlive = true;
                return;
            }

            // A ghost gets the game's own respawn, and already flies GhostCamera.
            if (runtime != null && runtime.IsGhost)
            {
                return;
            }

            // Only deaths during a live match make spectators; lobby deaths respawn normally.
            if (sawAlive && match!.IsLive)
            {
                Act(session!.Eliminate());
            }
        }

        private void Act(SpectatorChange change)
        {
            switch (change)
            {
                case SpectatorChange.DeathCamStarted:
                    BeginDeathCam();
                    break;

                case SpectatorChange.FreeCameraTaken:
                    warnedAboutTheHandover = false;
                    handoverSeen = false;
                    TakeTheCamera();
                    break;

                case SpectatorChange.Removed:
                    Log.Info($"{LmsMod.LogPrefix} eliminated, and spectating is off: respawning as usual.");
                    break;

                case SpectatorChange.Restored:
                    GiveTheCameraBack();
                    break;
            }
        }

        /// <summary>Starts the death cam and, if spectating is possible, claims the body.</summary>
        /// <remarks>
        ///     Claimed now rather than at the handover because <c>ResetPlayerOnDeath</c> teleports the
        ///     body exactly when the handover falls due.
        /// </remarks>
        private void BeginDeathCam()
        {
            bool canSpectate = config!.Spectator.Spectate && RespawnSuppression.Active && FreeCamera.IsAvailable;
            SpectatorHold.KeepsTheBody = canSpectate;

            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} eliminated. {(canSpectate ? "Free camera" : "Respawning")} ",
                $"in {config.Spectator.DeathCam.TotalSeconds:0}s."));

            if (canSpectate)
            {
                ErrorMessage.AddMessage("LMS: you are out. The camera is yours in a moment — fly and watch.");
            }
        }

        private void TakeTheCamera()
        {
            bool claimed = SpectatorHold.KeepsTheBody;

            if (claimed && FreeCamera.Take())
            {
                HoldTheBody();

                // The game's free camera will not fly until the cursor is locked.
                FreeCamera.LockTheCursor();

                // It otherwise starts at its slowest speed, 0.4 m/s.
                FreeCamera.SetSpeed(config!.Spectator.FlySpeed);

                // The toggle happens on the controller's next frame, so check later.
                checkHandoverAt = Time.time + HandoverCheckSeconds;
                checksLeft = HandoverChecks;

                Log.Info(Readable.Line(
                    $"{LmsMod.LogPrefix} spectating: the camera is free and the body stays where it fell, ",
                    $"flying at {config.Spectator.FlySpeed:0.#} m/s."));
                ErrorMessage.AddMessage("LMS: spectating. 1-5 sets the speed, shift is faster.");
                return;
            }

            if (!claimed)
            {
                // The game's own respawn has already run.
                return;
            }

            // Body held but no camera: release it and let the suppressed respawn happen now.
            Log.Warn($"{LmsMod.LogPrefix} the camera would not hand over; respawning instead.");
            SpectatorHold.KeepsTheBody = false;
            Player.main.MovePlayerToRespawnPoint();
        }

        private void GiveTheCameraBack()
        {
            ReleaseTheBody();

            if (FreeCamera.IsFlying)
            {
                FreeCamera.Release();
            }

            SpectatorHold.KeepsTheBody = false;
            sawAlive = false;

            Log.Info($"{LmsMod.LogPrefix} back in the match: the camera is the player's again.");
        }

        /// <summary>
        ///     Stops the corpse being a player again.
        /// </summary>
        /// <remarks>
        ///     <c>ResetPlayerOnDeath</c> later restores health and unfreezes stats, which would let the
        ///     body drown and die again. <c>FreezeStats</c> is counted, so ours outlives that unfreeze.
        /// </remarks>
        private void HoldTheBody()
        {
            if (!statsFrozen)
            {
                statsFrozen = true;
                Player.main.FreezeStats();
            }

            LiveMixin live = Player.main.liveMixin;
            if (live != null && !live.invincible)
            {
                madeInvincible = true;
                live.invincible = true;
            }
        }

        /// <summary>Undoes <see cref="HoldTheBody"/> and restores health.</summary>
        /// <remarks>
        ///     The game's respawn never ran, so health is still zero; without the reset a restored
        ///     player would start the next match dead.
        /// </remarks>
        private void ReleaseTheBody()
        {
            if (statsFrozen)
            {
                statsFrozen = false;
                Player.main.UnfreezeStats();
            }

            LiveMixin live = Player.main.liveMixin;

            if (live != null && !live.IsFullHealth())
            {
                live.ResetHealth();
            }

            if (!madeInvincible)
            {
                return;
            }

            madeInvincible = false;

            if (live != null)
            {
                live.invincible = false;
            }
        }

        /// <summary>Keeps the player controller off so the body stays at the death site.</summary>
        /// <remarks>
        ///     <c>ResetPlayerOnDeath</c> re-enables it after the handover. Only disabled when enabled,
        ///     since disabling zeroes velocity and fires trigger-exit recovery.
        /// </remarks>
        private void KeepTheBodyStill()
        {
            PlayerController controller = Player.main.playerController;
            if (controller != null && controller.enabled)
            {
                controller.SetEnabled(false);
            }
        }
    }
}
