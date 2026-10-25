using System;
using System.Reflection;
using HarmonyLib;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Drives the game's own <c>FreecamController</c>, which already handles detaching the camera,
    ///     locking input and entering locked mode, and reverses it all on the way out.
    /// </summary>
    /// <remarks>
    ///     Calls the private <c>OnConsoleCommand_freecam</c> via reflection rather than the console:
    ///     <c>DevConsole.SendConsoleCommand</c> would set the persisted <c>used_cheats</c> flag. The
    ///     handler also sets <c>ghostMode = false</c>, so leaving does not teleport the body to the camera.
    /// </remarks>
    internal static class FreeCamera
    {
        private const string Toggle = "OnConsoleCommand_freecam";

        /// <summary>
        ///     The controller's private speed field, in metres a second.
        /// </summary>
        /// <remarks>
        ///     <c>FreecamController</c> starts on the slowest of its five speeds (0.4 m/s), far too slow
        ///     for a 4 km world.
        /// </remarks>
        private const string SpeedField = "speed";

        private static MethodInfo? toggle;
        private static bool resolved;
        private static bool warned;
        private static FieldInfo? speed;
        private static bool speedResolved;

        /// <summary>Whether the camera can be taken at all. Resolved once, and logged once if not.</summary>
        internal static bool IsAvailable => Controller() != null && Resolve() != null;

        /// <summary>Whether the game is flying the camera right now.</summary>
        internal static bool IsFlying
        {
            get
            {
                FreecamController? controller = Controller();
                return controller != null && controller.GetActive();
            }
        }

        /// <summary>Takes the camera off the player. Idempotent.</summary>
        internal static bool Take() => Set(true);

        /// <summary>
        ///     Sets how fast the camera flies, in metres a second. See <see cref="SpeedField"/>.
        /// </summary>
        /// <returns>Whether the speed could be set at all.</returns>
        internal static bool SetSpeed(double metresPerSecond)
        {
            FreecamController? controller = Controller();
            FieldInfo? field = SpeedOf();

            if (controller == null || field == null)
            {
                return false;
            }

            try
            {
                field.SetValue(controller, (float)metresPerSecond);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not set the free camera's speed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     Locks the cursor, which the game's free camera needs before it will fly.
        /// </summary>
        /// <remarks>
        ///     <c>FreecamController.Update</c> only moves while <c>UWE.Utils.lockCursor</c> is true, which
        ///     only a left click sets; the toggle sets <c>Screen.lockCursor</c>, a different property.
        /// </remarks>
        internal static void LockTheCursor()
        {
            try
            {
                UWE.Utils.lockCursor = true;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not lock the cursor for the free camera: {ex.Message}");
            }
        }

        /// <summary>
        ///     What the game's camera is actually doing, for one log line after the handover.
        /// </summary>
        /// <remarks>
        ///     <c>FreecamController.Update</c> skips all input unless <c>inputDummy.activeSelf</c>.
        /// </remarks>
        internal static string State()
        {
            FreecamController? controller = Controller();
            if (controller == null)
            {
                return "no controller";
            }

            return Readable.Line(
                $"flying={controller.GetActive()} input={InputIsLive(controller)} ",
                $"cursor={Cursor()} mode={Mode()} speed={SpeedOn(controller)} ",
                $"{Feeding()} {Gates()}");
        }

        /// <summary>
        ///     What the game's input source is handing the free camera, and where the camera is.
        /// </summary>
        /// <remarks>
        ///     <c>GameInput</c> reads zero for a frame or two after <c>ClearInput</c>, which
        ///     <c>EnterLockedMode</c> calls, so one sample proves nothing.
        /// </remarks>
        private static string Feeding()
        {
            try
            {
                UnityEngine.Vector3 move = GameInput.GetMoveDirection();
                UnityEngine.Vector2 look = GameInput.GetLookDelta();
                UnityEngine.Transform? camera = MainCameraControl.main == null
                    ? null
                    : MainCameraControl.main.transform;

                // Invariant culture, so a decimal-comma locale still logs readable coordinates.
                string at = camera == null
                    ? "?"
                    : Readable.Line($"({camera.position.x:0},{camera.position.y:0},{camera.position.z:0})");

                return Readable.Line(
                    $"move=({move.x:0.##},{move.y:0.##},{move.z:0.##}) ",
                    $"look=({look.x:0.##},{look.y:0.##}) ",
                    $"at={at}");
            }
            catch (Exception ex)
            {
                return $"input unreadable ({ex.GetType().Name})";
            }
        }

        /// <summary>
        ///     The three <c>GameInput</c> gates between a held key and <c>GetMoveDirection</c>.
        /// </summary>
        /// <remarks>
        ///     The game uses Unity's new input system, where a disabled action reads zero rather than
        ///     throwing. <c>clear=True</c>: something calls <c>ClearInput</c> every frame.
        ///     <c>rebinding=True</c>: a rebind was left open. <c>moveaction</c> off: the action is disabled.
        /// </remarks>
        private static string Gates()
        {
            try
            {
                return $"clear={GameInput.clearInput} rebinding={GameInput.IsRebinding} " +
                       $"held={GameInput.GetButtonHeld(GameInput.Button.MoveForward)} " +
                       $"moveaction={MoveAction()} body={Body()}";
            }
            catch (Exception ex)
            {
                return $"gates unreadable ({ex.GetType().Name})";
            }
        }

        /// <summary>
        ///     Whether the game's own <c>Move</c> action is switched on, and its phase.
        /// </summary>
        /// <remarks>
        ///     Reflection, because this assembly does not reference the input system; any shape change
        ///     prints a message instead of throwing.
        /// </remarks>
        private static string MoveAction()
        {
            object? source = AccessTools.Field(typeof(GameInput), "input")?.GetValue(null);
            if (source == null)
            {
                return "no input source";
            }

            object? table = AccessTools.Field(source.GetType(), "actions")?.GetValue(source);
            if (!(table is System.Collections.IDictionary actions))
            {
                return $"no action table on {source.GetType().Name}";
            }

            object? action = actions[GameInput.Button.Move];
            if (action == null)
            {
                return "no Move action";
            }

            object? enabled = AccessTools.PropertyGetter(action.GetType(), "enabled")?.Invoke(action, null);
            object? phase = AccessTools.PropertyGetter(action.GetType(), "phase")?.Invoke(action, null);

            return $"{(enabled == null ? "?" : enabled.ToString())}/{(phase == null ? "?" : phase.ToString())}";
        }

        /// <summary>
        ///     Whether the player controller has input enabled.
        /// </summary>
        /// <remarks>
        ///     The free camera's <c>EnterLockedMode(null)</c> disables it; <c>ResetPlayerOnDeath</c> calls
        ///     <c>ToNormalMode</c> at the same five-second mark, which re-enables it.
        /// </remarks>
        private static string Body()
        {
            if (Player.main == null)
            {
                return "no player";
            }

            PlayerController controller = Player.main.playerController;

            return controller == null ? "controller=none" : $"controller={controller.inputEnabled}";
        }

        private static string InputIsLive(FreecamController controller)
        {
            try
            {
                FieldInfo? field = AccessTools.Field(typeof(FreecamController), "inputDummy");
                object? dummy = field?.GetValue(controller);

                return dummy is UnityEngine.GameObject input ? input.activeSelf.ToString() : "unreadable";
            }
            catch (Exception ex)
            {
                return $"unreadable ({ex.GetType().Name})";
            }
        }

        /// <summary>The controller's speed as the reading prints it, or why it cannot be read.</summary>
        private static string SpeedOn(FreecamController controller)
        {
            FieldInfo? field = SpeedOf();
            if (field == null)
            {
                return "unreadable";
            }

            try
            {
                return Readable.Line($"{field.GetValue(controller)}");
            }
            catch (Exception ex)
            {
                return $"unreadable ({ex.GetType().Name})";
            }
        }

        private static FieldInfo? SpeedOf()
        {
            if (speedResolved)
            {
                return speed;
            }

            speedResolved = true;
            speed = AccessTools.Field(typeof(FreecamController), SpeedField);

            if (speed == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} no FreecamController.{SpeedField} to set; a spectator " +
                         "starts on the game's own 0.4 m/s and has to press 1-5 to get anywhere.");
            }

            return speed;
        }

        private static string Cursor()
        {
            try
            {
                return UWE.Utils.lockCursor.ToString();
            }
            catch (Exception ex)
            {
                return $"unreadable ({ex.GetType().Name})";
            }
        }

        private static string Mode() => Player.main == null ? "no player" : Player.main.GetMode().ToString();

        /// <summary>
        ///     Every sphere this mod has put in the world, with where and how big it is.
        /// </summary>
        internal static string Spheres()
        {
            string[] names = { "LmsZoneShell", "LmsZoneGhost", "LmsZoneSphere", "LmsRingShell" };
            System.Text.StringBuilder found = new System.Text.StringBuilder();

            foreach (string name in names)
            {
                UnityEngine.GameObject[] all = UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>();

                foreach (UnityEngine.GameObject candidate in all)
                {
                    if (candidate == null || !candidate.name.StartsWith(name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    UnityEngine.Transform at = candidate.transform;
                    found.Append(found.Length == 0 ? string.Empty : "; ")
                        .Append(candidate.name)
                        .Append(candidate.activeInHierarchy ? " on" : " OFF")
                        .Append(Readable.Line($" at ({at.position.x:0},{at.position.y:0},{at.position.z:0})"))
                        .Append(Readable.Line($" r{at.lossyScale.x:0.##}"));
                }
            }

            return found.Length == 0 ? "none in the scene" : found.ToString();
        }

        /// <summary>Gives it back, leaving the player exactly where the body has been standing.</summary>
        internal static bool Release() => Set(false);

        /// <summary>
        ///     Puts the camera where the body is; the handover otherwise leaves it at the world origin.
        /// </summary>
        /// <remarks>
        ///     Call after the toggle: <c>FreecamController</c> acts on its next frame and reparents the
        ///     transform.
        /// </remarks>
        internal static bool PlaceAt(UnityEngine.Vector3 where)
        {
            MainCameraControl camera = MainCameraControl.main;
            if (camera == null)
            {
                return false;
            }

            try
            {
                camera.transform.position = where;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"{LmsMod.LogPrefix} could not place the free camera: {ex.Message}");
                return false;
            }
        }

        private static bool Set(bool flying)
        {
            FreecamController? controller = Controller();
            MethodInfo? method = Resolve();

            if (controller == null || method == null)
            {
                Warn("the game's free camera is not reachable");
                return false;
            }

            if (controller.GetActive() == flying)
            {
                return true;
            }

            try
            {
                // Toggles on the controller's next frame, inside the game's Update, as the console does.
                method.Invoke(controller, null);
                return true;
            }
            catch (Exception ex)
            {
                Warn($"the free camera refused to toggle: {ex.Message}");
                return false;
            }
        }

        private static FreecamController? Controller()
        {
            MainCameraControl camera = MainCameraControl.main;

            // Unity's null: there is no camera in the main menu, where the mod initialises.
            return camera == null ? null : camera.GetComponent<FreecamController>();
        }

        private static MethodInfo? Resolve()
        {
            if (resolved)
            {
                return toggle;
            }

            resolved = true;
            toggle = AccessTools.Method(typeof(FreecamController), Toggle);

            if (toggle == null)
            {
                Warn($"no FreecamController.{Toggle} to call");
            }

            return toggle;
        }

        private static void Warn(string reason)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Log.Warn($"{LmsMod.LogPrefix} {reason}; eliminated players will respawn normally instead of spectating.");
        }
    }
}
