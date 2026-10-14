using System.Collections.Generic;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// Kiln's life (K4, docs/kiln.md "The life"), drawn: the sunsails (their
    /// stalks, cover in the plugin's world, drawn exactly as their boxes; their
    /// membranes, 20-40 m across, open by day and folding at dusk), the night
    /// fliers rising out of the rifts and the Hollow after dark, the lattice
    /// coral on the rift walls glowing at night, and the sand-whales: ridges of
    /// moving sand swimming loops through the Dune sea. Cosmetic, and the same on
    /// every client: built from the world with fixed seeds, moved by the game's
    /// clock (Lattice/Life works out each one's place from it). Nothing here
    /// hides a player (GDD: "nothing cosmetic hides a player").
    /// </summary>
    public sealed class Life
    {
        static readonly int MinuteId = Shader.PropertyToID("_LifeMinute"), SecsId = Shader.PropertyToID("_LifeSecs"),
            NightId = Shader.PropertyToID("_LifeNight"), WhalesId = Shader.PropertyToID("_Whales"),
            HeightsId = Shader.PropertyToID("_LifeHeights"), MapId = Shader.PropertyToID("_LifeMap");

        /// <summary>A sunsail's membrane is this much of its stalk's height across, each side.</summary>
        const float SailReach = 0.6f;
        /// <summary>The rifts' directions (degrees, out from the center), as Kiln lays them.</summary>
        static readonly float[] Rifts = { 90f, 210f, 330f };
        const float Center = LtConst.WorldSize / 2f;
        /// <summary>A sand-whale's loop: its radii (m) and how long it takes (s).</summary>
        static readonly Vector2 WhaleLoop = new Vector2(320f, 170f);
        const float WhalePeriod = 540f;

        readonly GameObject root;
        readonly List<(Vector2 center, float turn, float phase)> whales = new List<(Vector2, float, float)>();
        readonly Vector4[] whaleData = new Vector4[2];
        double secs = -1;

        Life(GameObject root) => this.root = root;

        public static Life Build(LtWorldHandle world, WorldLook look, Transform parent, Texture2D heights)
        {
            if (look.SailMaterial == null || look.StalkMaterial == null || look.FlierMaterial == null || look.LifeCoralMaterial == null || look.WhaleMaterial == null)
                return null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var root = new GameObject("Life");
            root.transform.SetParent(parent, false);
            var life = new Life(root);
            var rng = new System.Random(0x11FE);

            // Sunsails: the stalks as their boxes, a membrane on each.
            var stalks = new Part();
            var sails = new Part();
            int nSails = 0;
            foreach (var b in world.Boxes())
            {
                if (b.kind != LtCode.CoverStalk)
                    continue;
                stalks.Box(b);
                float tall = b.max.z - b.min.z;
                var top = Coords.ToUnity(b.centerX, b.centerY, b.max.z + 0.2f);
                sails.Membrane(top, tall * SailReach, Hash(b.centerX * 0.37f + b.centerY * 0.11f));
                nSails++;
            }

            // Night fliers: flocks over each rift (along it, out from the Hollow) and the Hollow's.
            var fliers = new Part();
            int nFliers = 0;
            void Flock(float gx, float gy, float radius, int birds)
            {
                var anchor = Coords.ToUnity(gx, gy, world.Height(gx, gy) + 10f);
                float phase = (float)rng.NextDouble();
                for (int i = 0; i < birds; i++)
                    fliers.Flier(anchor, radius, (float)rng.NextDouble(), phase);
                nFliers += birds;
            }
            foreach (var deg in Rifts)
            {
                var dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                foreach (var along in new[] { 1250f, 1750f, 2450f, 3050f })
                    Flock(Center + dir.x * along, Center + dir.y * along, 110f + 60f * (float)rng.NextDouble(), 60);
            }
            Flock(Center + 260f, Center - 120f, 220f, 90);
            Flock(Center - 200f, Center + 230f, 260f, 90);

            // Lattice coral on the rift walls: clusters at random heights up the
            // walls, every 9 m along each, both sides.
            var coral = new Part();
            int nCoral = 0;
            foreach (var deg in Rifts)
            {
                var dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                var across = new Vector2(-dir.y, dir.x);
                for (float along = 900f; along < 3500f; along += 9f)
                {
                    var axis = new Vector2(Center, Center) + dir * along;
                    float floor = world.Height(axis.x, axis.y);
                    foreach (var side in new[] { -1f, 1f })
                    {
                        float want = floor + 6f + 150f * (float)(rng.NextDouble() * rng.NextDouble());
                        // Out from the floor to where the wall reaches that height.
                        Vector2 at = default;
                        bool found = false;
                        for (float d = 60f; d < 420f; d += 2f)
                        {
                            var q = axis + across * (side * d);
                            if (world.Height(q.x, q.y) >= want)
                            {
                                (at, found) = (q, true);
                                break;
                            }
                        }
                        if (!found)
                            continue;
                        float h = world.Height(at.x, at.y);
                        float gx = world.Height(at.x + 1f, at.y) - world.Height(at.x - 1f, at.y), gy = world.Height(at.x, at.y + 1f) - world.Height(at.x, at.y - 1f);
                        // On a wall only (steeper than 1:1).
                        if (gx * gx + gy * gy < 4f)
                            continue;
                        var normal = Coords.DirToUnity(new LtVec3 { x = -gx, y = -gy, z = 2f }).normalized;
                        coral.Cluster(Coords.ToUnity(at.x, at.y, h), normal, rng);
                        nCoral++;
                    }
                }
            }

            // Sand-whales: two loops through the Dune sea (sector II's outer
            // plains), each clear of every site.
            var sites = world.Sites();
            float axisDeg = 30f + 120f;
            var best = new List<(float score, Vector2 c)>();
            // (Inside the Rim, its 400 m and a margin; clear enough of the sites, the
            // nearest such loops to the plains' middle first.)
            const float inside = 520f;
            for (float r = 2750f; r <= 3350f; r += 50f)
            for (float deg = -44f; deg <= 44f; deg += 4f)
            {
                float a = (axisDeg + deg) * Mathf.Deg2Rad;
                var c = new Vector2(Center + Mathf.Cos(a) * r, Center + Mathf.Sin(a) * r);
                if (Mathf.Min(c.x, c.y) - WhaleLoop.x < inside || Mathf.Max(c.x, c.y) + WhaleLoop.x > LtConst.WorldSize - inside)
                    continue;
                float clear = float.MaxValue;
                foreach (var s in sites)
                    clear = Mathf.Min(clear, Vector2.Distance(c, new Vector2(s.x, s.y)) - s.half - WhaleLoop.x);
                best.Add((Mathf.Min(clear, 120f) - Mathf.Abs(deg) * 0.5f, c));
            }
            best.Sort((x, y) => y.score.CompareTo(x.score));
            foreach (var (score, c) in best)
            {
                if (life.whales.Count == 2 || score < 40f)
                    break;
                if (life.whales.Count == 1 && Vector2.Distance(life.whales[0].center, c) < 2f * WhaleLoop.x + 150f)
                    continue;
                life.whales.Add((c, (float)rng.NextDouble() * Mathf.PI, (float)rng.NextDouble()));
            }
            var whaleMesh = new Part();
            for (int w = 0; w < life.whales.Count; w++)
                whaleMesh.Whale(w, rng);

            stalks.Finish(root.transform, "Sunsails' stalks", look.StalkMaterial, true);
            sails.Finish(root.transform, "Sunsails' membranes", look.SailMaterial, false);
            fliers.Finish(root.transform, "Night fliers", look.FlierMaterial, false);
            coral.Finish(root.transform, "Lattice coral", look.LifeCoralMaterial, false);
            whaleMesh.Finish(root.transform, "Sand-whales", look.WhaleMaterial, false);
            Shader.SetGlobalTexture(HeightsId, heights);
            Shader.SetGlobalVector(MapId, new Vector4(Center, LtConst.TerrainRes, LtConst.TerrainN, LtConst.WorldSize));
            Debug.Log($"Lattice: life: {nSails} sunsails, {nFliers} night fliers, {nCoral} coral clusters, {life.whales.Count} sand-whales " +
                      $"(looping round {string.Join(", ", life.whales.ConvertAll(w => $"({w.center.x:F0}, {w.center.y:F0}) turned {w.turn:F3} phase {w.phase:F3}"))}) in {clock.Elapsed.TotalSeconds:F2} s");
            return life;
        }

        /// <summary>This frame's clock to the shader, and where the whales swim.</summary>
        public void Update(NetFrame frame, float dt)
        {
            if (!frame.Welcomed)
                return;
            double target = frame.Clock.cycleSecs;
            secs = secs < 0 || System.Math.Abs(secs + dt - target) > 1.0 ? target : secs + dt;
            float m = (float)(secs / 60.0);
            Shader.SetGlobalFloat(MinuteId, m);
            Shader.SetGlobalFloat(SecsId, (float)secs);
            Shader.SetGlobalFloat(NightId, DayCycle.Night(m));
            for (int i = 0; i < whaleData.Length; i++)
            {
                if (i >= whales.Count)
                {
                    whaleData[i] = Vector4.zero;
                    continue;
                }
                var (c, turn, phase) = whales[i];
                float t = (float)(secs / WhalePeriod) * 2f * Mathf.PI + phase * 6.283f;
                var local = new Vector2(Mathf.Cos(t) * WhaleLoop.x, Mathf.Sin(t) * WhaleLoop.y);
                var tangent = new Vector2(-Mathf.Sin(t) * WhaleLoop.x, Mathf.Cos(t) * WhaleLoop.y);
                float sn = Mathf.Sin(turn), cs = Mathf.Cos(turn);
                var p = c + new Vector2(local.x * cs - local.y * sn, local.x * sn + local.y * cs);
                var dir = new Vector2(tangent.x * cs - tangent.y * sn, tangent.x * sn + tangent.y * cs);
                // Unity's x, z (game x, y less half the world); heading in Unity's xz.
                whaleData[i] = new Vector4(p.x - Center, p.y - Center, Mathf.Atan2(dir.y, dir.x), 1f);
            }
            Shader.SetGlobalVectorArray(WhalesId, whaleData);
        }

        public void Dispose()
        {
            if (root != null)
                Object.Destroy(root);
        }

        static float Hash(float x) => Mathf.Repeat(Mathf.Sin(x * 127.1f + 3.7f) * 43758.5453f, 1f);

        /// <summary>A mesh being built, in Unity's space.</summary>
        sealed class Part
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uv0 = new List<Vector2>();
            readonly List<Vector2> uv1 = new List<Vector2>();
            readonly List<Vector4> uv2 = new List<Vector4>();
            readonly List<int> tris = new List<int>();

            int Vert(Vector3 p, Vector3 n, Vector2 a, Vector2 b, Vector4 c)
            {
                verts.Add(p);
                normals.Add(n);
                uv0.Add(a);
                uv1.Add(b);
                uv2.Add(c);
                return verts.Count - 1;
            }

            void Tri(int a, int b, int c)
            {
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
            }

            /// <summary>A box as the plugin has it (a stalk): its sides and top.</summary>
            public void Box(LtBox b)
            {
                var c = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    float along = (i & 1) == 0 ? -b.halfAlong : b.halfAlong;
                    float across = (i & 2) == 0 ? -b.halfAcross : b.halfAcross;
                    c[i] = Coords.ToUnity(b.centerX + along * b.axisX - across * b.axisY, b.centerY + along * b.axisY + across * b.axisX, (i & 4) == 0 ? b.min.z : b.max.z);
                }
                var mid = Vector3.zero;
                foreach (var p in c)
                    mid += p / 8f;
                void Face(int a, int bb, int cc, int d)
                {
                    var n = Vector3.Cross(c[bb] - c[a], c[d] - c[a]).normalized;
                    bool outward = Vector3.Dot(n, (c[a] + c[cc]) * 0.5f - mid) > 0f;
                    if (!outward)
                        n = -n;
                    int s = verts.Count;
                    foreach (int k in new[] { a, bb, cc, d })
                        Vert(c[k], n, Vector2.zero, Vector2.zero, Vector4.zero);
                    if (outward == (Vector3.Dot(Vector3.Cross(c[bb] - c[a], c[cc] - c[a]), n) > 0f))
                    {
                        Tri(s, s + 1, s + 2);
                        Tri(s, s + 2, s + 3);
                    }
                    else
                    {
                        Tri(s, s + 2, s + 1);
                        Tri(s, s + 3, s + 2);
                    }
                }
                Face(4, 5, 7, 6);
                Face(0, 4, 5, 1);
                Face(1, 5, 7, 3);
                Face(3, 7, 6, 2);
                Face(2, 6, 4, 0);
            }

            /// <summary>A sunsail's membrane: a shallow dish of <paramref name="reach"/> m round
            /// its stalk's top, in rings and spokes (its uv0 out and around).</summary>
            public void Membrane(Vector3 top, float reach, float hash)
            {
                const int rings = 5, spokes = 20;
                int start = verts.Count;
                var pivot = new Vector4(top.x, top.y, top.z, 0f);
                Vert(top, Vector3.up, new Vector2(0f, 0f), new Vector2(hash, 0f), pivot);
                for (int r = 1; r <= rings; r++)
                {
                    float f = (float)r / rings;
                    // Petals: the rim scalloped between ribs.
                    for (int s = 0; s < spokes; s++)
                    {
                        float a = s * 2f * Mathf.PI / spokes;
                        float scallop = r == rings && (s & 1) == 1 ? 0.88f : 1f;
                        var p = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (reach * f * scallop) + Vector3.up * (reach * (0.12f * f * f - 0.03f));
                        Vert(p, Vector3.up, new Vector2(f, (float)s / spokes), new Vector2(hash, 0f), pivot);
                    }
                }
                for (int s = 0; s < spokes; s++)
                    Tri(start, start + 1 + (s + 1) % spokes, start + 1 + s);
                for (int r = 1; r < rings; r++)
                {
                    int a0 = start + 1 + (r - 1) * spokes, b0 = start + 1 + r * spokes;
                    for (int s = 0; s < spokes; s++)
                    {
                        int s1 = (s + 1) % spokes;
                        Tri(a0 + s, a0 + s1, b0 + s1);
                        Tri(a0 + s, b0 + s1, b0 + s);
                    }
                }
            }

            /// <summary>A flier: a quad its shader turns to the camera and flies.</summary>
            public void Flier(Vector3 anchor, float radius, float seed, float phase)
            {
                var data = new Vector4(anchor.x, anchor.y, anchor.z, radius);
                int s = verts.Count;
                foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
                    Vert(anchor, Vector3.up, corner, new Vector2(seed, phase), data);
                Tri(s, s + 2, s + 1);
                Tri(s, s + 3, s + 2);
            }

            /// <summary>A cluster of coral on a wall: a few knobs (octahedra) on its face.</summary>
            public void Cluster(Vector3 at, Vector3 normal, System.Random rng)
            {
                var t = Vector3.Cross(normal, Vector3.up);
                t = t.sqrMagnitude < 1e-4f ? Vector3.right : t.normalized;
                var b = Vector3.Cross(normal, t);
                int knobs = 3 + rng.Next(4);
                for (int k = 0; k < knobs; k++)
                {
                    float size = 0.6f + 1.8f * (float)(rng.NextDouble() * rng.NextDouble());
                    var c = at + t * (float)((rng.NextDouble() - 0.5) * 6.0) + b * (float)((rng.NextDouble() - 0.5) * 4.0) + normal * (size * 0.3f);
                    Vector3[] axes = { t * size, -t * size, b * size, -b * size, normal * size * 0.7f, -normal * size * 0.4f };
                    int[,] faces = { { 0, 2, 4 }, { 2, 1, 4 }, { 1, 3, 4 }, { 3, 0, 4 }, { 2, 0, 5 }, { 1, 2, 5 }, { 3, 1, 5 }, { 0, 3, 5 } };
                    for (int f = 0; f < 8; f++)
                    {
                        var p0 = c + axes[faces[f, 0]];
                        var p1 = c + axes[faces[f, 1]];
                        var p2 = c + axes[faces[f, 2]];
                        var n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                        if (Vector3.Dot(n, (p0 + p1 + p2) / 3f - c) < 0f)
                            (p1, p2, n) = (p2, p1, -n);
                        int s = verts.Count;
                        Vert(p0, n, Vector2.zero, Vector2.zero, Vector4.zero);
                        Vert(p1, n, Vector2.zero, Vector2.zero, Vector4.zero);
                        Vert(p2, n, Vector2.zero, Vector2.zero, Vector4.zero);
                        Tri(s, s + 2, s + 1);
                    }
                }
            }

            /// <summary>Whale <paramref name="w"/>: its ridge (a grid in its own frame,
            /// along and across, m) and a spray of sand off its back (upright quads).</summary>
            public void Whale(int w, System.Random rng)
            {
                const float half = 70f, wide = 16f, step = 3f;
                int nx = (int)(2f * half / step) + 1, nz = (int)(2f * wide / 1.5f) + 1;
                int start = verts.Count;
                for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    Vert(Vector3.zero, Vector3.up, new Vector2(-half + i * step, -wide + j * 1.5f), new Vector2(w, 0f), Vector4.zero);
                for (int j = 0; j + 1 < nz; j++)
                for (int i = 0; i + 1 < nx; i++)
                {
                    int a = start + j * nx + i;
                    Tri(a, a + nx, a + 1);
                    Tri(a + 1, a + nx, a + nx + 1);
                }
                for (int k = 0; k < 48; k++)
                {
                    float along = (float)(rng.NextDouble() - 0.5) * 70f, across = (float)(rng.NextDouble() - 0.5) * 8f, tall = 3f + 6f * (float)rng.NextDouble();
                    // (Its corners: uv2 = its height, which side, bottom or top.)
                    int s = verts.Count;
                    var at = new Vector2(along, across);
                    Vert(Vector3.zero, Vector3.up, at, new Vector2(w, 1f), new Vector4(tall, -1f, 0f, 0f));
                    Vert(Vector3.zero, Vector3.up, at, new Vector2(w, 1f), new Vector4(tall, 1f, 0f, 0f));
                    Vert(Vector3.zero, Vector3.up, at, new Vector2(w, 1f), new Vector4(tall, 1f, 1f, 0f));
                    Vert(Vector3.zero, Vector3.up, at, new Vector2(w, 1f), new Vector4(tall, -1f, 1f, 0f));
                    Tri(s, s + 1, s + 2);
                    Tri(s, s + 2, s + 3);
                }
            }

            public void Finish(Transform parent, string name, Material material, bool shadows)
            {
                if (verts.Count == 0)
                    return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetUVs(2, uv2);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                // Moved on the GPU (flown, folded, swum): never culled for where the mesh was built.
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (LtConst.WorldSize * 1.5f));
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = shadows;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }
    }
}
