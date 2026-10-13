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
    /// material, built once.
    /// </summary>
    public static class Connectors
    {
        public static GameObject Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            var boxes = world.Boxes();
            var volumes = world.Volumes();
            var shields = new Part[3];
            for (int i = 0; i < shields.Length; i++)
                shields[i] = new Part();
            int nShields = 0;
            foreach (var b in boxes)
            {
                if (b.kind != LtCode.CoverShield)
                    continue;
                shields[b.pass < 3 ? b.pass : 0].Slab(b, new Color(1f, 1f, 1f, 0.35f));
                nShields++;
            }
            var lifts = new Part();
            var drops = new Part();
            foreach (var v in volumes)
            {
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
            for (int i = 0; i < shields.Length; i++)
                shields[i].Finish(root.transform, $"Shields {i}", look.Shields != null && i < look.Shields.Length ? look.Shields[i] : null);
            lifts.Finish(root.transform, "Lifts", look.LiftMaterial);
            drops.Finish(root.transform, "Drop shafts and springs", look.DropMaterial);
            Debug.Log($"Lattice: {nShields} shields, {volumes.Length} force volumes");
            return root;
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
                var (lo, hi) = (v.min, v.max);
                Vector3 P(float x, float y, float z) => Coords.ToUnity(x, y, z);
                var foot = new Color(1f, 1f, 1f, footAlpha);
                var top = new Color(1f, 1f, 1f, topAlpha);
                float h = hi.z - lo.z;
                Quad(P(lo.x, lo.y, lo.z), P(hi.x, lo.y, lo.z), P(hi.x, lo.y, hi.z), P(lo.x, lo.y, hi.z), foot, top, 0f, h);
                Quad(P(hi.x, lo.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, hi.y, hi.z), P(hi.x, lo.y, hi.z), foot, top, 0f, h);
                Quad(P(hi.x, hi.y, lo.z), P(lo.x, hi.y, lo.z), P(lo.x, hi.y, hi.z), P(hi.x, hi.y, hi.z), foot, top, 0f, h);
                Quad(P(lo.x, hi.y, lo.z), P(lo.x, lo.y, lo.z), P(lo.x, lo.y, hi.z), P(lo.x, hi.y, hi.z), foot, top, 0f, h);
            }

            public void Finish(Transform parent, string name, Material material)
            {
                if (verts.Count == 0 || material == null)
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
