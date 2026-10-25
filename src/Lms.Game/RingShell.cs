using System.Collections.Generic;
using Nitrox.Model.Logger;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     The wall of one boxing ring: a sphere of collision around a stasis field, switched on
    ///     only for a player the ring has already caught.
    /// </summary>
    /// <remarks>
    ///     A non-convex <c>MeshCollider</c>, not a <c>SphereCollider</c>: Unity pushes bodies out of
    ///     convex colliders, while a triangle mesh blocks crossing in both directions. It sits on a
    ///     free layer that collides only with the player's, so creatures and vehicles pass through.
    /// </remarks>
    internal sealed class RingShell
    {
        /// <remarks>
        ///     Worst-case inset of a polygon sphere is <c>1 - cos(pi / latitudes)</c>: 0.9% at 24 bands,
        ///     under 9 cm at a 10 m radius.
        /// </remarks>
        private const int Longitudes = 48;
        private const int Latitudes = 24;

        /// <summary>
        ///     How far the wall has to move before the collider is rescaled. Rescaling a
        ///     <c>MeshCollider</c> re-cooks its geometry, so it is not done every frame.
        /// </summary>
        private const float RescaleThresholdMetres = 0.1f;

        private static Mesh? sphere;
        private static int layer = -1;

        private readonly GameObject go;
        private readonly MeshCollider collider;

        private float scaledTo;

        private RingShell(GameObject go, MeshCollider collider)
        {
            this.go = go;
            this.collider = collider;
        }

        internal static RingShell Create(string name)
        {
            if (sphere == null)
            {
                sphere = SphereMesh.DoubleSided("LmsRingShell", Longitudes, Latitudes);
            }

            GameObject go = new GameObject(name) { layer = Layer() };

            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = sphere;
            // Non-convex, so the sphere has an inside to be held in.
            collider.convex = false;
            collider.enabled = false;

            // Kinematic: re-placing a static collider rebuilds PhysX's static tree and can miss a
            // collision that frame. It also stops the field's own OverlapSphere freezing it.
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            return new RingShell(go, collider);
        }

        /// <summary>
        ///     Whether the shell still exists; a scene change destroys it with its ring.
        /// </summary>
        internal bool IsAlive => go != null;

        /// <summary>Moves the wall to where the field's own bubble is now.</summary>
        internal void PlaceAt(Vector3 centre, float radius)
        {
            if (go == null)
            {
                return;
            }

            go.transform.position = centre;

            if (Mathf.Abs(radius - scaledTo) < RescaleThresholdMetres)
            {
                return;
            }

            scaledTo = radius;
            go.transform.localScale = Vector3.one * radius;
        }

        /// <summary>Whether the wall is solid to the local player. Off until the ring catches them.</summary>
        internal void SetHolding(bool holding)
        {
            if (collider != null && collider.enabled != holding)
            {
                collider.enabled = holding;
            }
        }

        internal void Dispose()
        {
            if (go != null)
            {
                Object.Destroy(go);
            }
        }

        /// <summary>
        ///     A layer nothing in the game uses, told to collide with the player and nothing else.
        /// </summary>
        /// <remarks>
        ///     An unnamed layer has no prefabs on it, so rewriting its collision row affects nothing
        ///     else. With no free layer it falls back to the player's own and warns.
        /// </remarks>
        private static int Layer()
        {
            if (layer >= 0)
            {
                return layer;
            }

            int[] players = PlayerLayers();

            // Layers below 8 are Unity's built-ins.
            for (int candidate = 8; candidate < 32; candidate++)
            {
                if (System.Array.IndexOf(players, candidate) >= 0 || !string.IsNullOrEmpty(LayerMask.LayerToName(candidate)))
                {
                    continue;
                }

                for (int other = 0; other < 32; other++)
                {
                    Physics.IgnoreLayerCollision(candidate, other, System.Array.IndexOf(players, other) < 0);
                }

                layer = candidate;
                Log.Info($"{LmsMod.LogPrefix} ring shells are on layer {candidate}, colliding with " +
                         $"{Named(players)} and nothing else.");
                return layer;
            }

            layer = players[0];
            Log.Warn($"{LmsMod.LogPrefix} no free physics layer; ring shells share the player's ({layer}). " +
                     "Creatures and vehicles may bounce off a ring instead of passing through it.");
            return layer;
        }

        /// <remarks>
        ///     Every layer the player might collide on: swimming and walking use different motor
        ///     colliders, which need not share a layer.
        /// </remarks>
        private static int[] PlayerLayers()
        {
            List<int> layers = new List<int>();

            if (Player.main != null)
            {
                Add(layers, Player.main.gameObject.layer);

                PlayerController controller = Player.main.playerController;
                if (controller != null)
                {
                    Add(layers, LayerOf(controller.activeController));
                    Add(layers, LayerOf(controller.underWaterController));
                    Add(layers, LayerOf(controller.groundController));
                }
            }

            Add(layers, LayerID.Player);
            return layers.ToArray();
        }

        private static int LayerOf(PlayerMotor? motor)
        {
            Collider? own = motor == null ? null : motor.GetCollider();
            return own == null ? -1 : own.gameObject.layer;
        }

        private static void Add(List<int> layers, int candidate)
        {
            if (candidate >= 0 && !layers.Contains(candidate))
            {
                layers.Add(candidate);
            }
        }

        private static string Named(int[] layers)
        {
            string[] names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                names[i] = $"{layers[i]} ('{LayerMask.LayerToName(layers[i])}')";
            }

            return string.Join(", ", names);
        }
    }
}
