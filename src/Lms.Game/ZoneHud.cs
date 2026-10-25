using System;
using System.IO;
using System.Reflection;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Lms.Core.Settings;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Everything the local player is told about the zone: the status line, a bearing to the
    ///     centre, and a minimap with the current and next spheres drawn on it.
    /// </summary>
    /// <remarks>
    ///     Subnautica's exponential fog hides a sphere beyond about 100 m, so the HUD shows which way
    ///     to swim. The minimap texture is generated offline by <c>./scripts/make-minimap</c> from the
    ///     terrain octrees; <c>Landscape.GetTopographicHeight</c> is dead code in the game.
    /// </remarks>
    internal sealed class ZoneHud : MonoBehaviour
    {
        internal const string MinimapFileName = "lms-minimap.png";

        /// <summary>
        ///     Where the two panels go. Read per frame so a resolution change applies immediately.
        /// </summary>
        private static HudLayout Layout => new HudLayout(Screen.width, Screen.height);
        private const int RingPixels = 128;
        private const int ConePixels = 64;

        /// <summary>How long the look cone is drawn, in pixels. A direction, not a distance.</summary>
        private const float ConeDrawPixels = 30f;

        /// <summary>The player's dot.</summary>
        private static readonly Color YouAreHere = new Color(1f, 1f, 1f);

        /// <summary>The look cone: the dot's own white, faint enough to read the map through.</summary>
        private static readonly Color LookingThatWay = new Color(1f, 1f, 1f, 0.45f);

        /// <summary>
        ///     Draws the map upside down, which puts north at the top.
        /// </summary>
        /// <remarks>
        ///     The stored image is south-up (<c>WorldMap.ImageIsSouthUp</c>): a mirror, not a rotation,
        ///     so drawing it as stored would swap east and west relative to the player's turns.
        /// </remarks>
        private static readonly Rect NorthUp = new Rect(0f, 1f, 1f, -1f);

        /// <summary>
        ///     Font size to scale when neither the skin nor its font reports one.
        /// </summary>
        /// <remarks>
        ///     Both can be zero (no override, dynamic font). 12 is the smallest size Subnautica's own
        ///     boxes use.
        /// </remarks>
        private const int DefaultBoxFont = 12;

        private GUIStyle? boxStyle;
        private int boxFont;

        // The wall's own colours (zone.colours), so the map reads like the world.
        private Color nowRing = GameColour.Solid(ZoneColours.Default.Now);
        private Color nextRing = GameColour.Solid(ZoneColours.Default.Next);

        private LmsRuntime? runtime;
        private TournamentConfig? config;
        private MatchFollower? match;
        private ZoneEnforcer? enforcer;
        private BiomeSampler? biomes;

        private Texture2D? minimap;
        private Texture2D? ring;
        private Texture2D? dot;
        private Texture2D? cone;
        private GUIStyle? label;
        private PingInstance? centrePing;
        private GameObject? pingAnchor;

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            config = runtime?.Config;
            match = runtime?.Follower;
            enforcer = GetComponent<ZoneEnforcer>();
            biomes = GetComponent<BiomeSampler>();

            if (config == null || match == null || enforcer == null)
            {
                enabled = false;
                return;
            }

            nowRing = GameColour.Solid(config.Colours.Now);
            nextRing = GameColour.Solid(config.Colours.Next);

            runtime.Reconfigured += Reconfigure;

            minimap = LoadMinimap();
            ring = MakeRing();
            dot = MakeDot();
            cone = MakeCone();
        }

        /// <summary>
        ///     Takes rules the server replaced.
        /// </summary>
        /// <remarks>
        ///     Only the ring colours are cached; everything else reads <c>config</c> per frame.
        /// </remarks>
        private void Reconfigure()
        {
            config = runtime?.Config;

            if (config == null)
            {
                return;
            }

            nowRing = GameColour.Solid(config.Colours.Now);
            nextRing = GameColour.Solid(config.Colours.Next);
        }

        private void OnDestroy()
        {
            if (pingAnchor != null)
            {
                Destroy(pingAnchor);
            }
        }

        private void Update()
        {
            if (match == null || Player.main == null)
            {
                return;
            }

            // Created in the first session frame, not Awake: in the main menu PingManager is not up.
            centrePing ??= CreateCentrePing();
            if (centrePing == null)
            {
                return;
            }

            // Marks where the wall is going; once there is no next sphere, the last one.
            ZoneState zone = match.Zone;
            ZoneSphere target = zone.NextSphere ?? zone.Sphere;

            pingAnchor!.transform.position =
                new Vector3((float)target.Centre.X, (float)target.Centre.Y, (float)target.Centre.Z);

            bool show = match.IsLive;
            if (centrePing.visible != show)
            {
                centrePing.SetVisible(show);
            }
        }

        /// <remarks>
        ///     A real <c>PingInstance</c> draws through terrain at any range with the distance. It
        ///     registers itself in <c>Start</c>, since a runtime <c>GameObject</c> is not a scene object.
        /// </remarks>
        private PingInstance? CreateCentrePing()
        {
            pingAnchor = new GameObject("LmsZoneCentre");
            DontDestroyOnLoad(pingAnchor);

            PingInstance ping = pingAnchor.AddComponent<PingInstance>();
            // origin is [AssertNotNull] and read during Start, so it has to be set before then.
            ping.origin = pingAnchor.transform;
            ping.pingType = PingType.Signal;
            ping.SetLabel("Zone");
            ping.displayPingInManager = false;
            ping.visible = false;

            Log.Info($"{LmsMod.LogPrefix} zone-centre ping created.");
            return ping;
        }

        private void OnGUI()
        {
            if (config == null || match == null || enforcer == null || Player.main == null)
            {
                return;
            }

            label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = false };

            ZoneState zone = match.Zone;
            ZoneAssessment assessment = enforcer.Assessment;

            string damage = config.Flags.EnforceDamage ? "ON" : "off";
            string standing = assessment.Verdict == ZoneVerdict.Outside
                ? $"OUTSIDE by {assessment.DistanceOutside:0} m"
                : assessment.Verdict.ToString();

            HudLayout layout = Layout;

            // The readout is dropped rather than drawn over the map on a window too narrow for both.
            if (Preferences.ShowStatusBox && !(layout.PanelsCollide && Preferences.ShowMinimap))
            {
                GUI.Box(Pixels(layout.Readout),
                    $"LMS {match.Status}\ndamage {damage}   {standing}\n{Bearing(zone)}\n{Where()}\n{Standing()}",
                    Legible(layout));
            }

            if (Preferences.ShowMinimap)
            {
                DrawMinimap(zone, layout);
            }
        }

        /// <summary>
        ///     One of Core's rectangles as the one Unity draws with.
        /// </summary>
        /// <remarks>
        ///     Not named <c>Screen</c>: that would shadow Unity's class and break <c>Screen.width</c>.
        /// </remarks>
        private static Rect Pixels(HudRect rect) => new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        /// <summary>
        ///     The box style, with its text scaled to the screen.
        /// </summary>
        /// <remarks>
        ///     Rebuilt only when the size changes, because a <c>GUIStyle</c> allocates.
        /// </remarks>
        private GUIStyle Legible(HudLayout layout)
        {
            // Scale the skin's own size, which is what fits the authored 460 px box.
            GUIStyle skin = GUI.skin.box;
            int authored = skin.fontSize > 0 ? skin.fontSize
                : skin.font != null && skin.font.fontSize > 0 ? skin.font.fontSize
                : DefaultBoxFont;

            int wanted = layout.Font(authored);

            if (boxStyle == null || boxFont != wanted)
            {
                boxStyle = new GUIStyle(GUI.skin.box) { fontSize = wanted, richText = false };
                boxFont = wanted;
            }

            return boxStyle;
        }

        /// <summary>
        ///     Who is left, this client's ping, and where the wall is going, all as the server says.
        /// </summary>
        /// <remarks>
        ///     The destination is only sent when <c>flags.showDestination</c> is on. A dash, not a zero,
        ///     when nothing was said: zero alive means the match is over.
        /// </remarks>
        private string Standing()
        {
            int? alive = match == null ? null : match.Alive;
            int ping = LmsNetwork.PingMs();

            string left = alive.HasValue ? $"{alive.Value} left" : "— left";
            string lag = ping < 0 ? "— ms" : $"{ping} ms";
            string going = match?.Destination;

            return string.IsNullOrEmpty(going)
                ? $"{left}   {lag}"
                : $"{left}   {lag}   to {going}";
        }

        /// <summary>Where the player is: the biome name and depth.</summary>
        private string Where()
        {
            string place = biomes == null || biomes.Named == null ? "somewhere unnamed" : biomes.Named;

            return $"{place}   {-Player.main.transform.position.y:0} m down";
        }

        /// <summary>Which way, how far, and whether to go up or down.</summary>
        private string Bearing(ZoneState zone)
        {
            Vector3 me = Player.main.transform.position;
            WorldPoint centre = zone.NextSphere?.Centre ?? zone.Sphere.Centre;

            float dx = (float)centre.X - me.x;
            float dz = (float)centre.Z - me.z;
            float dy = (float)centre.Y - me.y;
            float flat = Mathf.Sqrt(dx * dx + dz * dz);

            float degrees = CompassDegrees(dx, dz);
            string vertical = Mathf.Abs(dy) < 15f ? "level" : (dy > 0 ? $"{dy:0} m up" : $"{-dy:0} m down");
            string which = zone.NextSphere.HasValue ? "next zone" : "zone";

            return $"{which}: {flat:0} m on {Compass(degrees)} ({degrees:0}°), {vertical}";
        }

        /// <summary>
        ///     A bearing from north, which is +z in Subnautica, as the game's compass uses.
        /// </summary>
        private static float CompassDegrees(float dx, float dz) =>
            Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f);

        private static string Compass(float degrees)
        {
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return points[(int)Mathf.Repeat(Mathf.Round(degrees / 45f), 8f)];
        }

        private void DrawMinimap(ZoneState zone, HudLayout layout)
        {
            if (minimap == null || ring == null || dot == null)
            {
                return;
            }

            Rect box = Pixels(layout.Minimap);
            GUI.DrawTextureWithTexCoords(box, minimap, NorthUp);

            Vector3 me = Player.main.transform.position;

            // At the player's own depth: the zone is a sphere, so the equator would overstate how
            // far inside they are.
            if (zone.NextSphere.HasValue)
            {
                DrawRing(box, zone.NextSphere.Value, me.y, nextRing);
            }

            DrawRing(box, zone.Sphere, me.y, nowRing);

            Vector2 here = ToMinimap(box, me.x, me.z);

            // Under the dot, so the dot stays the thing that says where you are.
            DrawLook(here);

            Color was = GUI.color;
            GUI.color = YouAreHere;
            GUI.DrawTexture(new Rect(here.x - 3f, here.y - 3f, 6f, 6f), dot);
            GUI.color = was;
        }

        /// <summary>A cone at the player's dot, pointing where the camera looks.</summary>
        /// <remarks>
        ///     On the north-up map a compass bearing is the rotation: <c>RotateAroundPivot</c> is
        ///     clockwise because GUI y grows downward.
        /// </remarks>
        private void DrawLook(Vector2 here)
        {
            if (cone == null)
            {
                return;
            }

            Vector3 heading = Heading();
            if (heading.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Matrix4x4 wasMatrix = GUI.matrix;
            Color wasColour = GUI.color;

            GUIUtility.RotateAroundPivot(CompassDegrees(heading.x, heading.z), here);
            GUI.color = LookingThatWay;
            GUI.DrawTexture(new Rect(here.x - ConeDrawPixels / 2f, here.y - ConeDrawPixels / 2f,
                ConeDrawPixels, ConeDrawPixels), cone);

            GUI.color = wasColour;
            GUI.matrix = wasMatrix;
        }

        /// <summary>Which way the player is looking, flattened onto the map.</summary>
        /// <remarks>
        ///     Looking nearly straight up or down leaves almost no yaw, so the body's heading is used.
        /// </remarks>
        private static Vector3 Heading()
        {
            Camera view = MainCamera.camera;
            Vector3 forward = view != null ? view.transform.forward : Player.main.transform.forward;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);

            // sin(5 degrees) squared.
            if (flat.sqrMagnitude < 0.0076f)
            {
                Vector3 body = Player.main.transform.forward;
                flat = new Vector3(body.x, 0f, body.z);
            }

            return flat;
        }

        /// <summary>
        ///     Draws the sphere's cross-section at the given height as a scaled outline texture
        ///     (IMGUI has no circle).
        /// </summary>
        private void DrawRing(Rect box, ZoneSphere sphere, float atHeight, Color colour)
        {
            // Nothing to draw if the zone has closed or the sphere does not reach this depth.
            double across = sphere.RadiusAtHeight(atHeight);
            if (across <= 0)
            {
                return;
            }

            Vector2 centre = ToMinimap(box, (float)sphere.Centre.X, (float)sphere.Centre.Z);

            // The box is square, so one fraction serves both axes.
            float radius = (float)WorldMap.Fraction(across) * box.width;

            Color was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), ring);
            GUI.color = was;
        }

        /// <remarks>
        ///     Uses <c>WorldMap.NorthUp</c>, shared with the caster map, so both draw the same transform.
        /// </remarks>
        private static Vector2 ToMinimap(Rect box, float worldX, float worldZ)
        {
            MapPoint at = WorldMap.NorthUp(new WorldPoint(worldX, 0, worldZ));

            return new Vector2(box.x + (float)at.U * box.width, box.y + (float)at.V * box.height);
        }

        private static Texture2D? LoadMinimap()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string path = Path.Combine(directory, MinimapFileName);

            if (!File.Exists(path))
            {
                Log.Warn($"{LmsMod.LogPrefix} no {MinimapFileName} in '{directory}'; " +
                         "the bearing readout is there but there is no map. Run ./scripts/make-minimap.");
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Log.Error($"{LmsMod.LogPrefix} {MinimapFileName} is not a readable image.");
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            Log.Info($"{LmsMod.LogPrefix} minimap loaded, {texture.width}x{texture.height}.");
            return texture;
        }

        private static Texture2D MakeRing()
        {
            Texture2D texture = new Texture2D(RingPixels, RingPixels, TextureFormat.ARGB32, mipChain: false);
            texture.wrapMode = TextureWrapMode.Clamp;

            const float outer = RingPixels / 2f - 1f;
            const float inner = outer - 2.5f;
            Vector2 middle = new Vector2(RingPixels / 2f, RingPixels / 2f);

            for (int x = 0; x < RingPixels; x++)
            {
                for (int y = 0; y < RingPixels; y++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), middle);
                    // Tinted white, so GUI.color decides the ring's colour at draw time.
                    float alpha = distance <= outer && distance >= inner ? 1f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        /// <remarks>
        ///     Apex at the texture's centre, opening upward, fading outwards and off-axis.
        /// </remarks>
        private static Texture2D MakeCone()
        {
            const float halfAngle = 32f;

            Texture2D texture = new Texture2D(ConePixels, ConePixels, TextureFormat.ARGB32, mipChain: false);
            texture.wrapMode = TextureWrapMode.Clamp;

            Vector2 apex = new Vector2(ConePixels / 2f, ConePixels / 2f);
            const float reach = ConePixels / 2f;

            for (int x = 0; x < ConePixels; x++)
            {
                for (int y = 0; y < ConePixels; y++)
                {
                    Vector2 from = new Vector2(x + 0.5f, y + 0.5f) - apex;
                    float distance = from.magnitude;
                    float alpha = 0f;

                    // Texture y grows towards the top of the drawn rect.
                    if (from.y > 0f && distance <= reach)
                    {
                        float offAxis = Vector2.Angle(Vector2.up, from);
                        if (offAxis <= halfAngle)
                        {
                            alpha = (1f - distance / reach) * Mathf.Sqrt(1f - offAxis / halfAngle);
                        }
                    }

                    // White, so GUI.color decides how faint it is at draw time.
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private static Texture2D MakeDot()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, mipChain: false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }
    }
}
