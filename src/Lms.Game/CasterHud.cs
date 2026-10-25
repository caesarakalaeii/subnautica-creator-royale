using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Zones;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Draws a ghost the name, health, air and distance of everybody in the match, through terrain.
    /// </summary>
    /// <remarks>
    ///     IMGUI because <c>OnGUI</c> paints over the rendered scene, so plates show through terrain
    ///     with no shader work. Only drawn for a ghost; contestants are never sent the feed anyway.
    /// </remarks>
    internal sealed class CasterHud : MonoBehaviour
    {
        /// <summary>How wide the health bar is, in pixels before the screen scale.</summary>
        private const float BarWidth = 120f;

        private const float BarHeight = 5f;

        /// <summary>How far above somebody's position the plate is drawn, in metres.</summary>
        /// <remarks>A player's transform sits at their feet.</remarks>
        private const float AboveTheHead = 2f;

        /// <summary>The plate's own colours. Health greens down to the red the dashboard uses.</summary>
        private static readonly Color Fine = new Color(0.35f, 0.85f, 0.45f);
        private static readonly Color Low = new Color(0.95f, 0.75f, 0.25f);
        private static readonly Color Critical = new Color(0.9f, 0.3f, 0.3f);

        /// <summary>What is behind the health bar where the health is gone.</summary>
        private static readonly Color Empty = new Color(0f, 0f, 0f, 0.45f);

        /// <summary>The plate's backing, dark enough to read white text on over open water.</summary>
        private static readonly Color Backing = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>The gap between the text and the bar under it, in pixels.</summary>
        private const float Padding = 4f;

        private LmsRuntime? runtime;
        private CasterSightings? sightings;
        private Texture2D? block;
        private GUIStyle? plate;

        /// <summary>
        ///     The one <see cref="GUIContent"/> every plate is measured and drawn through.
        /// </summary>
        /// <remarks>Reused to avoid a per-plate allocation on the frame path.</remarks>
        private GUIContent? caption;

        /// <summary>What <see cref="GUI.color"/> was before <see cref="Tint"/> touched it.</summary>
        private Color was;

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            sightings = GetComponent<CasterSightings>();

            if (runtime == null || sightings == null)
            {
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (block != null)
            {
                Destroy(block);
            }
        }

        private void OnGUI()
        {
            if (runtime == null || sightings == null || !runtime.IsGhost || !Drawn)
            {
                return;
            }

            // Repaint only: every rect is absolute, so the layout passes have nothing to do.
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Camera view = MainCamera.camera;
            if (view == null)
            {
                return;
            }

            plate ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                richText = false,
            };

            block ??= Block();
            caption ??= new GUIContent(string.Empty);

            Vector3 from = view.transform.position;

            foreach (string name in sightings.Named)
            {
                if (sightings.TryFind(name, out CasterSighting seen))
                {
                    Draw(view, from, seen);
                }
            }
        }

        /// <remarks>
        ///     Read per frame, because the booth can replace the rules mid-match.
        /// </remarks>
        private bool Drawn
        {
            get
            {
                TournamentConfig? config = runtime?.Config;

                return config == null ? CasterPolicy.Default.Nameplates : config.Caster.Nameplates;
            }
        }

        private void Draw(Camera view, Vector3 from, CasterSighting seen)
        {
            Vector3 head = new Vector3((float)seen.Where.X, (float)seen.Where.Y + AboveTheHead,
                (float)seen.Where.Z);

            Vector3 screen = view.WorldToScreenPoint(head);

            // Behind the camera: Unity still projects these, mirrored through the screen centre.
            if (screen.z <= 0f)
            {
                return;
            }

            // GUI space counts down from the top and the projection counts up from the bottom.
            float x = screen.x;
            float y = Screen.height - screen.y;

            double away = seen.Where.DistanceTo(new WorldPoint(from.x, from.y, from.z));

            // Player's locale, not Readable: on-screen text. A dead contestant shows DEAD instead of vitals.
            if (!seen.Alive)
            {
                caption!.text = $"{seen.Name}\nDEAD   {away:0} m";

                Vector2 deadSize = plate!.CalcSize(caption);

                Tint(Backing);
                GUI.DrawTexture(new Rect(x - deadSize.x / 2f, y - deadSize.y, deadSize.x, deadSize.y),
                    block!);
                Untint();

                GUI.Label(new Rect(x - deadSize.x / 2f, y - deadSize.y, deadSize.x, deadSize.y),
                    caption, plate);

                return;
            }

            caption!.text = $"{seen.Name}\n{seen.Health:0} hp   {seen.Oxygen:0} s   {away:0} m";

            // CalcSize already includes both lines for a non-wrapping style.
            Vector2 size = plate!.CalcSize(caption);
            float width = Mathf.Max(size.x, BarWidth);

            Rect box = new Rect(x - width / 2f, y - size.y - BarHeight - Padding, width,
                size.y + BarHeight + Padding);

            Tint(Backing);
            GUI.DrawTexture(box, block!);
            Untint();

            GUI.Label(new Rect(box.x, box.y, box.width, size.y), caption, plate);

            Bar(new Rect(x - BarWidth / 2f, box.yMax - BarHeight - 2f, BarWidth, BarHeight),
                seen.Health);
        }

        /// <summary>
        ///     The health bar: as much of it as is left, in the colour the dashboard would use.
        /// </summary>
        /// <remarks>
        ///     Oxygen has no bar: it is reported in seconds and a tank changes the maximum.
        /// </remarks>
        private void Bar(Rect where, double health)
        {
            Tint(Empty);
            GUI.DrawTexture(where, block!);
            Untint();

            float left = Mathf.Clamp01((float)(health / 100.0));
            if (left <= 0f)
            {
                return;
            }

            Tint(Of(Vitals.Health(health)));
            GUI.DrawTexture(new Rect(where.x, where.y, where.width * left, where.height), block!);
            Untint();
        }

        /// <summary>
        ///     Sets the draw colour, remembering what it was.
        /// </summary>
        /// <remarks>
        ///     A method pair rather than a delegate to avoid a closure allocation per draw.
        ///     <c>GUI.color</c> is global for the frame, so it must be restored for the zone HUD.
        /// </remarks>
        private void Tint(Color colour)
        {
            was = GUI.color;
            GUI.color = colour;
        }

        private void Untint() => GUI.color = was;

        private static Color Of(VitalLevel level)
        {
            switch (level)
            {
                case VitalLevel.Critical: return Critical;
                case VitalLevel.Low: return Low;
                default: return Fine;
            }
        }

        /// <summary>Draws something in one colour and puts the old one back.</summary>
        /// <remarks>
        ///     <c>GUI.color</c> is global for the frame, so it must be restored for the zone HUD.
        /// </remarks>
        private static void Colour(Color colour, System.Action draw)
        {
            Color was = GUI.color;
            GUI.color = colour;

            draw();

            GUI.color = was;
        }

        /// <remarks>
        ///     One white pixel, stretched and coloured by <c>GUI.color</c> at draw time.
        /// </remarks>
        private static Texture2D Block()
        {
            Texture2D made = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };

            made.SetPixel(0, 0, Color.white);
            made.Apply();

            return made;
        }
    }
}
