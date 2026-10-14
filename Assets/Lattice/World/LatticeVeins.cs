using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// The lattice made visible (docs/kiln.md, "a living world"): a vein of light
    /// along every lane, so routes read from the air and at a distance, dimmer
    /// along the ways inside sites (the control points are <see cref="TerritoryView"/>'s). Drawn on
    /// whatever the route walks on (the ground, a deck, a stair's steps, a
    /// tunnel's floor), a little above it, additive, a soft ribbon. Cosmetic:
    /// nothing collides with it. A few meshes, one per 2 km tile, built once (a
    /// world with no lanes, like Harrow, has none).
    ///
    /// In its holders' colors (K3, "a captured site's veins shift to its new
    /// owner's"): a site's own ways in its holder's, a lane between two sites
    /// from the one's holder's to the other's, pale while no one holds a site.
    /// A tile's colors are rewritten only when a site it touches changes hands.
    /// </summary>
    public sealed class LatticeVeins : IDisposable
    {
        /// <summary>A sample every this many meters along a route.</summary>
        const float Step = 3f;
        const int Tiles = 4;
        /// <summary>How much of a holder's color a vein takes (the rest white).</summary>
        const float Tint = 1f;
        /// <summary>The ways inside sites, dimmer.</summary>
        const byte Inside = 90, Lane = 255;

        readonly GameObject root;
        readonly Tile[] tiles;
        /// <summary>Who each site's veins show as holding it (254: not yet painted).</summary>
        readonly byte[] shown;
        readonly bool[] changed;

        LatticeVeins(GameObject root, Tile[] tiles, int sites)
        {
            (this.root, this.tiles) = (root, tiles);
            shown = new byte[sites];
            changed = new bool[sites];
            for (int i = 0; i < sites; i++)
                shown[i] = 254;
        }

        public GameObject Root => root;

        public static LatticeVeins Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            var lanes = world.Lanes();
            if (lanes.Length == 0 || look.VeinMaterial == null)
                return null;
            var clock = Stopwatch.StartNew();
            var pts = world.LanePoints();
            int sites = world.Sites().Length;
            var root = new GameObject("Lattice veins");
            root.transform.SetParent(parent, false);
            const float tile = LtConst.WorldSize / Tiles;
            var parts = new Part[Tiles * Tiles];
            for (int t = 0; t < parts.Length; t++)
                parts[t] = new Part(sites);
            int samples = 0;
            var line = new List<Vector3>();
            foreach (var lane in lanes)
            {
                // (Not the clockwork's ways: over a bridge at night, down a sandfall.)
                if (lane.count < 2 || lane.when != LtCode.WhenAlways)
                    continue;
                bool inside = lane.a == lane.b;
                var first = pts[lane.first];
                int t = Mathf.Clamp((int)(first.x / tile), 0, Tiles - 1) + Tiles * Mathf.Clamp((int)(first.y / tile), 0, Tiles - 1);
                var part = parts[t];
                // Resample the route every Step m, each point on what's walked there.
                line.Clear();
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
                part.Ribbon(line, look.VeinWidth, lane.a, lane.b, inside ? Inside : Lane);
            }
            var built = new List<Tile>();
            for (int t = 0; t < parts.Length; t++)
            {
                var done = parts[t].Finish(root.transform, $"Veins {t % Tiles},{t / Tiles}", look.VeinMaterial);
                if (done != null)
                    built.Add(done);
            }
            Debug.Log($"Lattice: {lanes.Length} routes' veins ({samples} samples) in {clock.Elapsed.TotalSeconds:F2} s");
            var veins = new LatticeVeins(root, built.ToArray(), sites);
            veins.Paint(null, all: true);
            return veins;
        }

        static Color32 VeinOf(byte holder)
        {
            // No one's: the lattice's own cyan (the material's white, bright).
            return holder < 3 ? (Color32)Color.Lerp(Color.white, (Color)TerritoryView.ColorOf(holder), Tint) : (Color32)new Color(0.24f, 0.8f, 1f);
        }

        /// <summary>Each frame, after the plugin's calls: recolors the tiles a site
        /// that's changed hands touches.</summary>
        public unsafe void Update(NetFrame f)
        {
            int n = Math.Min(f.SiteCount, shown.Length);
            bool any = false;
            for (int s = 0; s < n; s++)
            {
                byte owner = f.Sites[s].owner;
                changed[s] = owner != shown[s];
                if (changed[s])
                {
                    shown[s] = owner;
                    any = true;
                }
            }
            if (any)
                Paint(changed, all: false);
        }

        void Paint(bool[] which, bool all)
        {
            foreach (var t in tiles)
            {
                bool touched = all;
                for (int s = 0; !touched && s < which.Length; s++)
                    touched = which[s] && t.Sites[s];
                if (!touched)
                    continue;
                for (int v = 0; v < t.A.Length; v++)
                {
                    var ca = VeinOf(shown[t.A[v]]);
                    var cb = VeinOf(shown[t.B[v]]);
                    var c = Color32.Lerp(ca, cb, t.Mix[v] / 255f);
                    c.a = t.Bright[v];
                    t.Colors[v] = c;
                }
                t.Mesh.SetColors(t.Colors);
            }
        }

        public void Dispose()
        {
            if (tiles != null)
                foreach (var t in tiles)
                {
                    if (t.Colors.IsCreated)
                        t.Colors.Dispose();
                    if (t.Mesh != null)
                        UnityEngine.Object.Destroy(t.Mesh);
                }
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }

        /// <summary>A tile's mesh and, per vertex, the sites its color comes from.</summary>
        sealed class Tile
        {
            public Mesh Mesh;
            public NativeArray<Color32> Colors;
            /// <summary>The site at its route's start and its end, how far it is from the one's
            /// color to the other's (0-255), and its brightness (the alpha the material uses).</summary>
            public ushort[] A, B;
            public byte[] Mix, Bright;
            /// <summary>The sites it has any vertex of.</summary>
            public bool[] Sites;
        }

        /// <summary>A mesh being built: ribbons, in Unity's space.</summary>
        sealed class Part
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> tris = new List<int>();
            readonly List<ushort> a = new List<ushort>(), b = new List<ushort>();
            readonly List<byte> mix = new List<byte>(), bright = new List<byte>();
            readonly bool[] sites;

            public Part(int sites) => this.sites = new bool[sites];

            /// <summary>A ribbon <paramref name="width"/> wide along a line of game-space
            /// points, across it u 0..1 (the material's soft edge), along it v in meters; its
            /// color from site <paramref name="from"/>'s holder's to <paramref name="to"/>'s,
            /// changing over its middle third.</summary>
            public void Ribbon(List<Vector3> line, float width, ushort from, ushort to, byte brightness)
            {
                float total = 0f;
                for (int i = 1; i < line.Count; i++)
                    total += Vector2.Distance(new Vector2(line[i].x, line[i].y), new Vector2(line[i - 1].x, line[i - 1].y));
                if (from < sites.Length)
                    sites[from] = true;
                if (to < sites.Length)
                    sites[to] = true;
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
                    byte m = (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 0.67f, total > 0f ? along / total : 0f)) * 255f);
                    for (int k = 0; k < 2; k++)
                    {
                        a.Add(from);
                        b.Add(to);
                        mix.Add(m);
                        bright.Add(brightness);
                    }
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

            public Tile Finish(Transform parent, string name, Material material)
            {
                if (verts.Count == 0)
                    return null;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                var colors = new NativeArray<Color32>(verts.Count, Allocator.Persistent);
                mesh.SetColors(colors);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                mesh.MarkDynamic();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return new Tile { Mesh = mesh, Colors = colors, A = a.ToArray(), B = b.ToArray(), Mix = mix.ToArray(), Bright = bright.ToArray(), Sites = sites };
            }
        }
    }
}
