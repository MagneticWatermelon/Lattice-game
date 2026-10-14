using System.Collections.Generic;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// The collision kit's see-through parts (K1, docs/kiln.md): shields, a
    /// glowing slab in their faction's color; and force volumes, a column of
    /// light up each lift and down each drop shaft, a short one on each
    /// spring. Cosmetic (what they do is the plugin's): a few meshes, one per
    /// material, built once. A site's shields (its spawn rooms', K3) are a mesh
    /// of their own, in <paramref name="siteShields"/>: <see cref="TerritoryView"/>
    /// colors them by who holds the site.
    /// </summary>
    public static class Connectors
    {
        public static GameObject Build(LtWorldHandle world, WorldLook look, Transform parent, List<(int site, MeshRenderer renderer)> siteShields)
        {
            var boxes = world.Boxes();
            var volumes = world.Volumes();
            var shields = new Part[3];
            for (int i = 0; i < shields.Length; i++)
                shields[i] = new Part();
            var bySite = new SortedDictionary<int, (Part part, int pass)>();
            int nShields = 0;
            foreach (var b in boxes)
            {
                if (b.kind != LtCode.CoverShield)
                    continue;
                if (b.site != LtCode.NoSite)
                {
                    if (!bySite.TryGetValue(b.site, out var s))
                        bySite[b.site] = s = (new Part(), b.pass);
                    s.part.Slab(b, new Color(1f, 1f, 1f, 0.35f));
                }
                else
                    shields[b.pass < 3 ? b.pass : 0].Slab(b, new Color(1f, 1f, 1f, 0.35f));
                nShields++;
            }
            var lifts = new Part();
            var drops = new Part();
            foreach (var v in volumes)
            {
                // (Vent lifts and sandfalls run on the clock: Clockwork draws them.)
                if (v.when != LtCode.WhenAlways)
                    continue;
                // Lifts brighter at the foot, fading as they rise; drop shafts the other way.
                if (v.force == LtCode.ForceLift)
                    lifts.Column(v, 0.55f, 0.12f);
                else if (v.force == LtCode.ForceDrop)
                    drops.Column(v, 0.1f, 0.4f);
                else
                    drops.Column(v, 0.7f, 0.7f);
            }
            if (nShields == 0 && volumes.Length == 0)
                return null;
            var root = new GameObject("Shields and lifts");
            root.transform.SetParent(parent, false);
            Material Shield(int faction) => faction < 3 ? (look.Shields != null && faction < look.Shields.Length ? look.Shields[faction] : null) : look.ShieldNone;
            for (int i = 0; i < shields.Length; i++)
                shields[i].Finish(root.transform, $"Shields {i}", Shield(i));
            foreach (var kv in bySite)
            {
                var r = kv.Value.part.Finish(root.transform, $"Site {kv.Key}'s shields", Shield(kv.Value.pass));
                if (r != null)
                    siteShields?.Add((kv.Key, r));
            }
            lifts.Finish(root.transform, "Lifts", look.LiftMaterial);
            drops.Finish(root.transform, "Drop shafts and springs", look.DropMaterial);
            Debug.Log($"Lattice: {nShields} shields ({bySite.Count} sites' own), {volumes.Length} force volumes");
            return root;
        }

        /// <summary>A force volume's footprint in game space, its corners
        /// counterclockwise from its axis's low end and right side (a column
        /// turned with the walkway or deck it stands in, plugin API v8).</summary>
        internal static Vector2[] Footprint(LtVolume v)
        {
            var (ax, ay, a, c) = (v.axisX, v.axisY, v.halfAlong, v.halfAcross);
            var (cx, cy) = (v.centerX, v.centerY);
            Vector2 At(float u, float w) => new Vector2(cx + u * ax - w * ay, cy + u * ay + w * ax);
            return new[] { At(-a, -c), At(a, -c), At(a, c), At(-a, c) };
        }

        /// <summary>A mesh being built, in Unity's space.</summary>
        sealed class Part
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, float v0, float v1)
            {
                int s = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                verts.Add(d);
                uvs.Add(new Vector2(0f, v0));
                uvs.Add(new Vector2(1f, v0));
                uvs.Add(new Vector2(1f, v1));
                uvs.Add(new Vector2(0f, v1));
                colors.Add(ca);
                colors.Add(ca);
                colors.Add(cb);
                colors.Add(cb);
                tris.Add(s);
                tris.Add(s + 1);
                tris.Add(s + 2);
                tris.Add(s);
                tris.Add(s + 2);
                tris.Add(s + 3);
            }

            /// <summary>A shield: its box's two broad faces and its edges (drawn from both sides).</summary>
            public void Slab(LtBox b, Color color)
            {
                var corner = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    float a = (i & 1) == 0 ? -b.halfAlong : b.halfAlong;
                    float c = (i & 2) == 0 ? -b.halfAcross : b.halfAcross;
                    float z = (i & 4) == 0 ? b.min.z : b.max.z;
                    corner[i] = Coords.ToUnity(b.centerX + a * b.axisX - c * b.axisY, b.centerY + a * b.axisY + c * b.axisX, z);
                }
                // The four sides (bottom corner pairs, then up).
                int[][] sides = { new[] { 0, 1 }, new[] { 1, 3 }, new[] { 3, 2 }, new[] { 2, 0 } };
                foreach (var s in sides)
                    Quad(corner[s[0]], corner[s[1]], corner[s[1] + 4], corner[s[0] + 4], color, color, 0f, 1f);
            }

            /// <summary>A force volume's column: its four sides, the glow across each
            /// (the vein texture's soft edges), alpha from the foot's to the top's.</summary>
            public void Column(LtVolume v, float footAlpha, float topAlpha)
            {
                var c = Footprint(v);
                var foot = new Color(1f, 1f, 1f, footAlpha);
                var top = new Color(1f, 1f, 1f, topAlpha);
                float h = v.max.z - v.min.z;
                for (int i = 0; i < 4; i++)
                {
                    var (a, b) = (c[i], c[(i + 1) % 4]);
                    Quad(Coords.ToUnity(a.x, a.y, v.min.z), Coords.ToUnity(b.x, b.y, v.min.z), Coords.ToUnity(b.x, b.y, v.max.z), Coords.ToUnity(a.x, a.y, v.max.z), foot, top, 0f, h);
                }
            }

            public MeshRenderer Finish(Transform parent, string name, Material material)
            {
                if (verts.Count == 0 || material == null)
                    return null;
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
                return r;
            }
        }
    }
}
