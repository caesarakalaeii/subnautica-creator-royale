using System.Collections;
using Nitrox.Model.Logger;
using Lms.Core.Configuration;
using Lms.Core.Zones;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Draws the wall and the next zone: spheres at the zone's centre, scaled to its radius, in
    ///     the game's own stasis-field material tinted with <c>zone.colours</c>.
    /// </summary>
    /// <remarks>
    ///     The material comes from <c>StasisRifle.effectSpherePrefab</c> via <c>CraftData</c>. The
    ///     <c>StasisSphere</c> component is not used: its <c>LateUpdate</c> freezes every rigidbody in
    ///     its radius. If the material is unavailable the wall is enforced but not drawn.
    /// </remarks>
    internal sealed class ZoneRenderer : MonoBehaviour
    {
        /// <remarks>
        ///     At 128 segments the sagitta of a 1400 m sphere is 0.42 m, so the drawn wall stays within
        ///     half a metre of the one Core measures against.
        /// </remarks>
        private const int Longitudes = 128;
        private const int Latitudes = 64;

        private ZoneEnforcer? enforcer;

        /// <summary>Kept rather than read once, because a rules push replaces the config.</summary>
        private LmsRuntime? runtime;

        private ZoneColours colours = ZoneColours.Default;
        private Transform? shell;
        private Transform? ghost;

        /// <summary>
        ///     The game's own stasis-field materials, kept so a re-tint needs no second prefab load.
        /// </summary>
        /// <remarks>
        ///     Null until <see cref="Build"/> has loaded the prefab; a rules push can arrive before that.
        /// </remarks>
        private Material[]? source;

        private void Awake()
        {
            enforcer = GetComponent<ZoneEnforcer>();
            if (enforcer == null)
            {
                enabled = false;
                return;
            }

            runtime = GetComponent<LmsRuntime>();
            colours = Colours();

            // Subscribed here, not in Build: a push can land before the coroutine reaches a session.
            if (runtime != null)
            {
                runtime.Reconfigured += Reconfigure;
            }

            StartCoroutine(Build());
        }

        private void OnDestroy()
        {
            if (shell != null)
            {
                Destroy(shell.gameObject);
            }

            if (ghost != null)
            {
                Destroy(ghost.gameObject);
            }
        }

        private void LateUpdate()
        {
            if (shell == null || enforcer == null)
            {
                return;
            }

            ZoneState zone = enforcer.Zone;

            // A closed zone is hidden; a zero scale would leave a speck at the centre.
            Show(shell, zone.Sphere.Radius > 0);
            Place(shell, zone.Sphere);

            if (ghost != null)
            {
                // The last phase's next sphere is the collapse, radius zero: nothing to draw.
                bool hasNext = zone.NextSphere.HasValue && zone.NextSphere.Value.Radius > 0;
                Show(ghost, hasNext);

                if (hasNext)
                {
                    Place(ghost, zone.NextSphere!.Value);
                }
            }
        }

        private static void Show(Transform target, bool visible)
        {
            if (target.gameObject.activeSelf != visible)
            {
                target.gameObject.SetActive(visible);
            }
        }

        private static void Place(Transform target, ZoneSphere sphere)
        {
            if (sphere.Radius <= 0)
            {
                return;
            }

            target.position = new Vector3((float)sphere.Centre.X, (float)sphere.Centre.Y, (float)sphere.Centre.Z);
            target.localScale = Vector3.one * (float)sphere.Radius;
        }

        private IEnumerator Build()
        {
            // Initialize runs at the main menu, where the prefab database is not usable yet.
            while (Player.main == null)
            {
                yield return null;
            }

            CoroutineTask<GameObject> request = CraftData.GetPrefabForTechTypeAsync(TechType.StasisRifle, verbose: false);
            yield return request;

            Material[]? materials = MaterialsFrom(request.GetResult());
            if (materials == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} no stasis field material available; the zone is enforced but not drawn.");
                yield break;
            }

            // Kept so a colour push is a re-tint, not another prefab load.
            source = materials;

            Mesh mesh = SphereMesh.Unit("LmsZoneSphere", Longitudes, Latitudes);
            Mesh flipped = SphereMesh.FlippedCopy(mesh);

            shell = BuildShell("LmsZoneShell", mesh, flipped, Tinted(materials, colours.Now));
            ghost = BuildShell("LmsZoneGhost", mesh, flipped, Tinted(materials, colours.Next));
            ghost.gameObject.SetActive(false);

            Log.Info($"{LmsMod.LogPrefix} zone shell {colours.Now} and next-zone ghost {colours.Next} " +
                     $"built from the stasis field material ({materials.Length} pass(es)).");
        }

        /// <summary>
        ///     Re-tints both shells after the server pushed new rules; the mesh is unaffected.
        /// </summary>
        /// <remarks>
        ///     Unconditional: <c>ZoneColours</c> has no value equality, and pushes are rare.
        /// </remarks>
        private void Reconfigure()
        {
            colours = Colours();

            if (source == null)
            {
                // Build has not reached the prefab yet and will use these colours when it does.
                return;
            }

            Retint(shell, Tinted(source, colours.Now));
            Retint(ghost, Tinted(source, colours.Next));
        }

        private ZoneColours Colours()
        {
            TournamentConfig? config = runtime == null ? null : runtime.Config;

            return config == null ? ZoneColours.Default : config.Colours;
        }

        /// <summary>Re-tints both faces of a shell.</summary>
        /// <remarks>
        ///     <c>sharedMaterials</c>, not <c>materials</c>: <see cref="Tinted"/> already returns fresh
        ///     copies, and <c>materials</c> would make Unity instantiate another set.
        /// </remarks>
        private static void Retint(Transform? target, Material[] materials)
        {
            if (target == null)
            {
                return;
            }

            foreach (MeshRenderer face in target.GetComponentsInChildren<MeshRenderer>(true))
            {
                face.sharedMaterials = materials;
            }
        }

        private static Transform BuildShell(string name, Mesh outward, Mesh inward, Material[] materials)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(null);
            DontDestroyOnLoad(go);

            // Two faces: the wall is seen from inside and from outside.
            AddFace(go, outward, materials, "Outward");
            AddFace(go, inward, materials, "Inward");

            return go.transform;
        }

        /// <summary>The wall in the colour the config asked for, on a copy of the game's material.</summary>
        /// <remarks>
        ///     Copies, because <c>sharedMaterials</c> are the prefab's own and tinting them would also
        ///     recolour the stasis rifle. <c>_Color</c> is what <c>StasisSphere</c> fades itself by;
        ///     <c>_GlowColor</c> is tinted too, or a white glow washes out the colour.
        /// </remarks>
        private static Material[] Tinted(Material[] source, ZoneColour colour)
        {
            Material[] copies = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copies[i] = new Material(source[i]);
                Tint(copies[i], ShaderPropertyID._Color, colour, keepAlpha: false);
                Tint(copies[i], ShaderPropertyID._GlowColor, colour, keepAlpha: true);
            }

            return copies;
        }

        /// <remarks>
        ///     Hue from the config, intensity from the material, so HDR emissive brightness is kept.
        ///     Alpha multiplies the material's own.
        /// </remarks>
        private static void Tint(Material material, int property, ZoneColour colour, bool keepAlpha)
        {
            if (!material.HasProperty(property))
            {
                return;
            }

            Color was = material.GetColor(property);
            float intensity = Mathf.Max(1f, Mathf.Max(was.r, Mathf.Max(was.g, was.b)));
            Color wanted = GameColour.From(colour);

            material.SetColor(property, new Color(
                wanted.r * intensity,
                wanted.g * intensity,
                wanted.b * intensity,
                keepAlpha ? was.a : was.a * wanted.a));
        }

        private static Material[]? MaterialsFrom(GameObject? riflePrefab)
        {
            StasisRifle rifle = riflePrefab == null ? null : riflePrefab.GetComponent<StasisRifle>();
            GameObject spherePrefab = rifle == null ? null : rifle.effectSpherePrefab;
            MeshRenderer renderer = spherePrefab == null ? null : spherePrefab.GetComponent<MeshRenderer>();

            Material[] materials = renderer == null ? null : renderer.sharedMaterials;
            return materials != null && materials.Length > 0 ? materials : null;
        }

        private static void AddFace(GameObject parent, Mesh mesh, Material[] materials, string name)
        {
            GameObject face = new GameObject(name);
            face.transform.SetParent(parent.transform, worldPositionStays: false);
            face.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = face.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            // Shadows from a boundary of light would cost a shadow map for nothing.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
