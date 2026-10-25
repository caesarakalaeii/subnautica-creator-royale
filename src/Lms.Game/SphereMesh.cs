using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Procedural unit-radius spheres, so a transform's scale is the radius in metres.
    /// </summary>
    internal static class SphereMesh
    {
        internal static Mesh Unit(string name, int longitudes, int latitudes)
        {
            Vector3[] vertices = new Vector3[(longitudes + 1) * (latitudes + 1)];
            Vector2[] uv = new Vector2[vertices.Length];

            for (int lat = 0; lat <= latitudes; lat++)
            {
                float polar = Mathf.PI * lat / latitudes;
                float y = Mathf.Cos(polar);
                float ring = Mathf.Sin(polar);

                for (int lon = 0; lon <= longitudes; lon++)
                {
                    float azimuth = 2f * Mathf.PI * lon / longitudes;
                    int index = lat * (longitudes + 1) + lon;

                    vertices[index] = new Vector3(ring * Mathf.Cos(azimuth), y, ring * Mathf.Sin(azimuth));
                    uv[index] = new Vector2((float)lon / longitudes, (float)lat / latitudes);
                }
            }

            int[] triangles = new int[longitudes * latitudes * 6];
            int t = 0;
            for (int lat = 0; lat < latitudes; lat++)
            {
                for (int lon = 0; lon < longitudes; lon++)
                {
                    int a = lat * (longitudes + 1) + lon;
                    int b = a + longitudes + 1;

                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;

                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                }
            }

            Mesh mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = triangles };
            // On a unit sphere the normals are the positions, exactly.
            mesh.normals = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh FlippedCopy(Mesh source)
        {
            Mesh mesh = new Mesh
            {
                name = source.name + "Inverted",
                vertices = source.vertices,
                uv = source.uv,
                triangles = Reversed(source.triangles),
                normals = Negated(source.normals),
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        ///     Every triangle twice, wound both ways, so a collider stops movement in both directions.
        /// </summary>
        /// <remarks>PhysX triangle meshes are one-sided for some queries and sweeps.</remarks>
        internal static Mesh DoubleSided(string name, int longitudes, int latitudes)
        {
            Mesh source = Unit(name, longitudes, latitudes);
            int[] outward = source.triangles;
            int[] inward = Reversed(outward);

            int[] both = new int[outward.Length * 2];
            outward.CopyTo(both, 0);
            inward.CopyTo(both, outward.Length);

            source.triangles = both;
            source.RecalculateBounds();
            return source;
        }

        private static int[] Reversed(int[] triangles)
        {
            int[] flipped = (int[])triangles.Clone();
            for (int i = 0; i < flipped.Length; i += 3)
            {
                int swap = flipped[i];
                flipped[i] = flipped[i + 2];
                flipped[i + 2] = swap;
            }

            return flipped;
        }

        private static Vector3[] Negated(Vector3[] normals)
        {
            Vector3[] flipped = new Vector3[normals.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                flipped[i] = -normals[i];
            }

            return flipped;
        }
    }
}
