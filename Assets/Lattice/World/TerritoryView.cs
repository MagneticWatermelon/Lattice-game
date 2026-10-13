using System;
using System.Collections.Generic;
using Lattice.Native;
using Lattice.Net;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// Territory in the world (U5): every control point in its holder's color
    /// (pale while no one holds it): a core ring where it stands, a ring round
    /// the ground that counts for it (draped on what's there, broken where
    /// there's nothing to stand on), and a beam of light up from it that reads
    /// from across the map, all of it flickering while the point is contested.
    /// And every site's own shields (its spawn rooms', from
    /// <see cref="Connectors"/>) in its holder's color, or pale while no one
    /// holds it (no one passes them then). Read from each frame's territory
    /// (<see cref="NetFrame.Sites"/>): the points' colors are rewritten only
    /// when a holder changes (and a few times a second while one's contested),
    /// a site's shields' material swapped when its holder changes.
    /// </summary>
    public sealed class TerritoryView : IDisposable
    {
        const int CoreSides = 40, EdgeSides = 72;
        const float CoreRadius = 1.6f, CoreWidth = 0.6f, EdgeWidth = 0.32f;
        const float BeamHeight = 36f, BeamWidth = 1.1f;
        /// <summary>A contested point's flicker: its rate (Hz), and how often its colors are rewritten (s).</summary>
        const float Flicker = 2.5f, FlickerStep = 0.05f;

        /// <summary>The factions' colors, as the shields' (amber, jade, magenta), and no one's.</summary>
        static readonly Color32[] Holder =
        {
            new Color(0.92f, 0.62f, 0.10f), new Color(0.12f, 0.66f, 0.50f), new Color(0.78f, 0.22f, 0.72f),
        };
        static readonly Color32 NoOne = new Color(0.5f, 0.53f, 0.56f);

        readonly WorldLook look;
        readonly Mesh mesh;
        readonly GameObject go;
        NativeArray<Color32> colors;
        /// <summary>Each vertex's own alpha (the shape: a beam fading up, the edge ring dimmer).</summary>
        readonly byte[] shape;
        /// <summary>Per point: its site, its index among the site's, its vertices, and what's shown.</summary>
        readonly int[] site, slot, first, count;
        readonly byte[] shown;
        readonly bool[] shownContested;
        readonly List<(int site, MeshRenderer renderer)> shields;
        readonly byte[] shieldShown;
        float flickerAt = -1f;

        TerritoryView(WorldLook look, Mesh mesh, GameObject go, byte[] shape, int[] site, int[] slot, int[] first, int[] count, List<(int, MeshRenderer)> shields)
        {
            (this.look, this.mesh, this.go, this.shape, this.site, this.slot, this.first, this.count, this.shields) = (look, mesh, go, shape, site, slot, first, count, shields);
            colors = new NativeArray<Color32>(shape.Length, Allocator.Persistent);
            shown = new byte[site.Length];
            shownContested = new bool[site.Length];
            for (int i = 0; i < site.Length; i++)
            {
                shown[i] = LtCode.NoOne;
                Paint(i, NoOne, 1f);
            }
            mesh.SetColors(colors);
            shieldShown = new byte[shields.Count];
            for (int i = 0; i < shieldShown.Length; i++)
                shieldShown[i] = 254;
        }

        /// <summary>The points' marks (none on a world without points), and the sites' shields to color.</summary>
        public static TerritoryView Build(LtWorldHandle world, WorldLook look, Transform parent, List<(int site, MeshRenderer renderer)> siteShields)
        {
            var points = world.Points();
            if ((points.Length == 0 || look.PointMaterial == null) && siteShields.Count == 0)
                return null;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var shape = new List<byte>();
            var tris = new List<int>();
            int n = points.Length;
            var (site, slot, first, count) = (new int[n], new int[n], new int[n], new int[n]);
            var perSite = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                var p = points[i].pos;
                site[i] = points[i].site;
                perSite.TryGetValue(site[i], out int k);
                // (The plugin's territory keeps a site's first six.)
                slot[i] = Mathf.Min(k, 5);
                perSite[site[i]] = k + 1;
                first[i] = verts.Count;
                float lift = look.VeinLift + 0.03f;
                // The core: where it stands.
                float stand = Mathf.Max(world.Ground(p.x, p.y, p.z + 0.4f, 0.9f), p.z);
                Ring(verts, uvs, shape, tris, CoreSides, CoreRadius, CoreWidth, 255, (x, y) => (stand + lift, true), p);
                // The edge of what counts for it, draped: up to its height above, down to it below.
                Ring(verts, uvs, shape, tris, EdgeSides, LtConst.PointRadius, EdgeWidth, 150, (x, y) =>
                {
                    float g = world.Ground(x, y, p.z + LtConst.PointHeight * 0.5f, LtConst.PointHeight * 0.5f);
                    return (g + lift, g >= p.z - LtConst.PointHeight);
                }, p);
                // The beam: two crossed quads, bright at its foot, gone at its top.
                for (int q = 0; q < 2; q++)
                {
                    var d = q == 0 ? new Vector2(BeamWidth * 0.5f, 0f) : new Vector2(0f, BeamWidth * 0.5f);
                    int v = verts.Count;
                    verts.Add(Coords.ToUnity(p.x - d.x, p.y - d.y, stand));
                    verts.Add(Coords.ToUnity(p.x + d.x, p.y + d.y, stand));
                    verts.Add(Coords.ToUnity(p.x + d.x, p.y + d.y, stand + BeamHeight));
                    verts.Add(Coords.ToUnity(p.x - d.x, p.y - d.y, stand + BeamHeight));
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, BeamHeight));
                    uvs.Add(new Vector2(0f, BeamHeight));
                    shape.Add(170);
                    shape.Add(170);
                    shape.Add(0);
                    shape.Add(0);
                    tris.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                }
                count[i] = verts.Count - first[i];
            }
            var mesh = new Mesh { name = "Control points", indexFormat = IndexFormat.UInt32 };
            GameObject go = null;
            if (n > 0 && look.PointMaterial != null)
            {
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                mesh.MarkDynamic();
                go = new GameObject("Control points");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = look.PointMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Debug.Log($"Lattice: {n} control points ({verts.Count} vertices), {siteShields.Count} sites' shields");
            return new TerritoryView(look, mesh, go, shape.ToArray(), site, slot, first, count, siteShields);
        }

        /// <summary>A ring round <paramref name="c"/> (game space), each vertex at the
        /// height <paramref name="at"/> gives (and whether there's anything there).</summary>
        static void Ring(List<Vector3> verts, List<Vector2> uvs, List<byte> shape, List<int> tris, int sides, float radius, float width, byte alpha,
            Func<float, float, (float z, bool ok)> at, LtVec3 c)
        {
            int v0 = verts.Count;
            bool prevOk = false;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                float x = c.x + cx * radius, y = c.y + cy * radius;
                var (z, ok) = at(x, y);
                verts.Add(Coords.ToUnity(c.x + cx * (radius - width * 0.5f), c.y + cy * (radius - width * 0.5f), z));
                verts.Add(Coords.ToUnity(c.x + cx * (radius + width * 0.5f), c.y + cy * (radius + width * 0.5f), z));
                uvs.Add(new Vector2(0f, i));
                uvs.Add(new Vector2(1f, i));
                shape.Add(alpha);
                shape.Add(alpha);
                if (i > 0 && ok && prevOk)
                {
                    int v = v0 + i * 2;
                    tris.AddRange(new[] { v - 2, v, v - 1, v - 1, v, v + 1 });
                }
                prevOk = ok;
            }
        }

        void Paint(int point, Color32 c, float alpha)
        {
            for (int v = first[point], end = first[point] + count[point]; v < end; v++)
                colors[v] = new Color32(c.r, c.g, c.b, (byte)(shape[v] * alpha));
        }

        internal static Color32 ColorOf(byte holder) => holder < Holder.Length ? Holder[holder] : NoOne;

        Material ShieldOf(byte holder) => holder < 3 && look.Shields != null && holder < look.Shields.Length ? look.Shields[holder] : look.ShieldNone;

        /// <summary>Each frame, after the plugin's calls: what's changed hands.</summary>
        public unsafe void Update(NetFrame f, float time)
        {
            if (f.SiteCount == 0)
                return;
            bool dirty = false;
            bool flicker = time - flickerAt >= FlickerStep;
            float pulse = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(time * Flicker * 2f * Mathf.PI));
            for (int i = 0; i < site.Length; i++)
            {
                if (site[i] >= f.SiteCount)
                    continue;
                var s = f.Sites[site[i]];
                byte holder = slot[i] < s.pointCount ? s.points[slot[i]] : (byte)LtCode.NoOne;
                bool contested = (s.contested >> slot[i] & 1) != 0;
                if (holder != shown[i] || contested != shownContested[i] || (contested && flicker))
                {
                    Paint(i, ColorOf(holder), contested ? pulse : 1f);
                    (shown[i], shownContested[i]) = (holder, contested);
                    dirty = true;
                }
            }
            if (dirty)
            {
                mesh.SetColors(colors);
                if (flicker)
                    flickerAt = time;
            }
            for (int i = 0; i < shields.Count; i++)
            {
                var (s, r) = shields[i];
                if (s >= f.SiteCount)
                    continue;
                byte owner = f.Sites[s].owner;
                if (owner != shieldShown[i])
                {
                    var m = ShieldOf(owner);
                    if (m != null)
                        r.sharedMaterial = m;
                    shieldShown[i] = owner;
                }
            }
        }

        public void Dispose()
        {
            if (colors.IsCreated)
                colors.Dispose();
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);
            if (go != null)
                UnityEngine.Object.Destroy(go);
        }
    }
}
