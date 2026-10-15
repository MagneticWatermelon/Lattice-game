using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// The shapes the classes' abilities are drawn with (U6), built once: a
    /// Bulwark's shell (a unit sphere, scaled round the body), a jet's plume
    /// (crossed quads along +z, the muzzle flash's layout, so the flash shader
    /// burns on it), and a Mend field's disc (1 m across, flat, UVs 0..1).
    /// </summary>
    public static class AbilityFx
    {
        static Mesh sphere, plume, disc;

        /// <summary>A unit sphere (radius 1), smooth, 24 × 16.</summary>
        public static Mesh Sphere()
        {
            if (sphere != null)
                return sphere;
            const int Around = 24, Up = 16;
            var v = new Vector3[(Around + 1) * (Up + 1)];
            var n = new Vector3[v.Length];
            for (int j = 0; j <= Up; j++)
            for (int i = 0; i <= Around; i++)
            {
                float a = i * Mathf.PI * 2f / Around, b = j * Mathf.PI / Up;
                var p = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), -Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
                v[j * (Around + 1) + i] = p;
                n[j * (Around + 1) + i] = p;
            }
            var t = new int[Around * Up * 6];
            int k = 0;
            for (int j = 0; j < Up; j++)
            for (int i = 0; i < Around; i++)
            {
                int a = j * (Around + 1) + i, b = a + Around + 1;
                (t[k++], t[k++], t[k++]) = (a, b, a + 1);
                (t[k++], t[k++], t[k++]) = (a + 1, b, b + 1);
            }
            sphere = new Mesh { name = "Ability shell", vertices = v, normals = n, triangles = t };
            sphere.RecalculateBounds();
            return sphere;
        }

        /// <summary>A jet's plume: two crossed quads 0.5 m along +z, UV v 0..1 along it.</summary>
        public static Mesh Plume()
        {
            if (plume != null)
                return plume;
            const float W = 0.06f, L = 0.5f;
            var v = new[]
            {
                new Vector3(-W, 0, 0), new Vector3(W, 0, 0), new Vector3(W, 0, L), new Vector3(-W, 0, L),
                new Vector3(0, -W, 0), new Vector3(0, W, 0), new Vector3(0, W, L), new Vector3(0, -W, L),
            };
            var uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            };
            plume = new Mesh { name = "Jet plume", vertices = v, uv = uv, triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 } };
            plume.RecalculateBounds();
            return plume;
        }

        /// <summary>A flat disc 1 m across (a 48-gon), facing up, UVs 0..1 across it.</summary>
        public static Mesh Disc()
        {
            if (disc != null)
                return disc;
            const int N = 48;
            var v = new Vector3[N + 1];
            var uv = new Vector2[N + 1];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
            }
            var t = new int[N * 3];
            for (int i = 0; i < N; i++)
                (t[i * 3], t[i * 3 + 1], t[i * 3 + 2]) = (0, 1 + (i + 1) % N, 1 + i);
            disc = new Mesh { name = "Mend field", vertices = v, uv = uv, triangles = t };
            disc.RecalculateNormals();
            disc.RecalculateBounds();
            return disc;
        }
    }
}
