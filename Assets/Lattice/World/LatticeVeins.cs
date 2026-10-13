using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// The lattice made visible (docs/kiln.md, "a living world"): a vein of light
    /// along every lane, so routes read from the air and at a distance, dimmer
    /// along the ways inside sites; and a ring at every control point. Drawn on
    /// whatever the route walks on (the ground, a deck, a stair's steps), a little
    /// above it, additive, a soft ribbon. Cosmetic: nothing collides with it. A
    /// few meshes, one per 2 km tile, built once (a world with no lanes, like
    /// Harrow, has none).
    /// </summary>
    public static class LatticeVeins
    {
        /// <summary>A sample every this many meters along a route.</summary>
        const float Step = 3f;
        const int RingSides = 40;

        public static GameObject Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            var lanes = world.Lanes();
            if (lanes.Length == 0 || look.VeinMaterial == null)
                return null;
            var clock = Stopwatch.StartNew();
            var pts = world.LanePoints();
            var root = new GameObject("Lattice veins");
            root.transform.SetParent(parent, false);
            const int tiles = 4;
            const float tile = LtConst.WorldSize / tiles;
            var parts = new Part[tiles * tiles];
            for (int t = 0; t < parts.Length; t++)
                parts[t] = new Part();
            int samples = 0;
            foreach (var lane in lanes)
            {
                if (lane.count < 2)
                    continue;
                bool inside = lane.a == lane.b;
                var color = inside ? new Color(0.35f, 0.35f, 0.35f, 1f) : Color.white;
                var first = pts[lane.first];
                int t = Mathf.Clamp((int)(first.x / tile), 0, tiles - 1) + tiles * Mathf.Clamp((int)(first.y / tile), 0, tiles - 1);
                var part = parts[t];
                // Resample the route every Step m, each point on what's walked there.
                var line = new List<Vector3>();
                for (int i = 0; i + 1 < lane.count; i++)
                {
                    var a = pts[lane.first + i];
                    var b = pts[lane.first + i + 1];
                    float len = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
                    for (int k = i == 0 ? 0 : 1; k <= n; k++)
                    {
                        float s = (float)k / n;
                        float x = a.x + (b.x - a.x) * s, y = a.y + (b.y - a.y) * s, z = a.z + (b.z - a.z) * s;
                        float ground = world.Ground(x, y, z + 0.4f, 0.9f);
                        // On the terrain unless the route is well under its surface (down a
                        // trench, along a tunnel): there, on whatever floor it walks.
                        float surface = world.Height(x, y);
                        line.Add(new Vector3(x, y, (z < surface - 1.5f ? ground : Mathf.Max(ground, surface)) + look.VeinLift));
                    }
                }
                samples += line.Count;
                part.Ribbon(line, look.VeinWidth, color);
            }
            for (int t = 0; t < parts.Length; t++)
                parts[t].Finish(root.transform, $"Veins {t % tiles},{t / tiles}", look.VeinMaterial);

            // The control points' rings.
            var points = world.Points();
            if (points.Length > 0 && look.PointMaterial != null)
            {
                var rings = new Part();
                foreach (var p in points)
                {
                    float ground = world.Ground(p.pos.x, p.pos.y, p.pos.z + 0.4f, 0.9f);
                    rings.Ring(new Vector3(p.pos.x, p.pos.y, Mathf.Max(ground, p.pos.z) + look.VeinLift + 0.02f), 3.2f, 0.7f);
                }
                rings.Finish(root.transform, "Control points", look.PointMaterial);
            }
            Debug.Log($"Lattice: {lanes.Length} routes' veins ({samples} samples) and {points.Length} control points in {clock.Elapsed.TotalSeconds:F2} s");
            return root;
        }

        /// <summary>A mesh being built: ribbons and rings, in Unity's space.</summary>
        sealed class Part
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> tris = new List<int>();

            /// <summary>A ribbon <paramref name="width"/> wide along a line of game-space
            /// points, across it u 0..1 (the material's soft edge), along it v in meters.</summary>
            public void Ribbon(List<Vector3> line, float width, Color color)
            {
                float along = 0f;
                for (int i = 0; i < line.Count; i++)
                {
                    var prev = line[Mathf.Max(0, i - 1)];
                    var next = line[Mathf.Min(line.Count - 1, i + 1)];
                    var d = new Vector2(next.x - prev.x, next.y - prev.y);
                    if (d.sqrMagnitude < 1e-6f)
                        d = Vector2.right;
                    d.Normalize();
                    var side = new Vector2(-d.y, d.x) * (width * 0.5f);
                    if (i > 0)
                        along += Vector2.Distance(new Vector2(line[i].x, line[i].y), new Vector2(line[i - 1].x, line[i - 1].y));
                    int v = verts.Count;
                    var p = line[i];
                    verts.Add(Coords.ToUnity(p.x - side.x, p.y - side.y, p.z));
                    verts.Add(Coords.ToUnity(p.x + side.x, p.y + side.y, p.z));
                    uvs.Add(new Vector2(0f, along));
                    uvs.Add(new Vector2(1f, along));
                    colors.Add(color);
                    colors.Add(color);
                    if (i > 0)
                    {
                        tris.Add(v - 2);
                        tris.Add(v);
                        tris.Add(v - 1);
                        tris.Add(v - 1);
                        tris.Add(v);
                        tris.Add(v + 1);
                    }
                }
            }

            /// <summary>A flat ring at a game-space point.</summary>
            public void Ring(Vector3 c, float radius, float width)
            {
                int v0 = verts.Count;
                for (int i = 0; i <= RingSides; i++)
                {
                    float a = i * Mathf.PI * 2f / RingSides;
                    float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                    verts.Add(Coords.ToUnity(c.x + cx * (radius - width * 0.5f), c.y + cy * (radius - width * 0.5f), c.z));
                    verts.Add(Coords.ToUnity(c.x + cx * (radius + width * 0.5f), c.y + cy * (radius + width * 0.5f), c.z));
                    uvs.Add(new Vector2(0f, i));
                    uvs.Add(new Vector2(1f, i));
                    colors.Add(Color.white);
                    colors.Add(Color.white);
                    if (i > 0)
                    {
                        int v = v0 + i * 2;
                        tris.Add(v - 2);
                        tris.Add(v);
                        tris.Add(v - 1);
                        tris.Add(v - 1);
                        tris.Add(v);
                        tris.Add(v + 1);
                    }
                }
            }

            public void Finish(Transform parent, string name, Material material)
            {
                if (verts.Count == 0)
                    return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }
    }
}
