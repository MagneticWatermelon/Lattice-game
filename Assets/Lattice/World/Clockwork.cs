using System.Collections.Generic;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// Kiln's clockwork, drawn (K4, docs/kiln.md "Clockwork"): the vent lifts'
    /// columns (running 20 s, resting 10, each at its own phase), the Dune
    /// sea's sandfalls (pouring in turns, 20 minutes each) and the living
    /// bridges' coral (growing through dusk, standing at night, cracking a
    /// minute into dawn, then gone). Three meshes, built once, and the clock
    /// handed to their shader (Lattice/Clockwork) as globals each frame: the
    /// GPU works out each one's state from the game step, as the plugin's
    /// collision does, so what's drawn is what's there. Cosmetic.
    /// </summary>
    public sealed class Clockwork
    {
        static readonly int VentId = Shader.PropertyToID("_ClockVent"), CycleId = Shader.PropertyToID("_ClockCycle"),
            VentConstId = Shader.PropertyToID("_ClockVentConst"), DayConstId = Shader.PropertyToID("_ClockDayConst");

        readonly GameObject root;
        /// <summary>The cycle's step, run on by frame time between the plugin's
        /// steps (the clock's frame fraction), and snapped to it when off.</summary>
        double cycle = -1;
        uint lastStep;

        Clockwork(GameObject root) => this.root = root;

        public static Clockwork Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            if (look.VentMaterial == null || look.SandMaterial == null || look.CoralMaterial == null)
                return null;
            var volumes = world.Volumes();
            var boxes = world.Boxes();
            var vents = new Part();
            var sand = new Part();
            int nVents = 0, nFalls = 0;
            for (int i = 0; i < volumes.Length; i++)
            {
                var v = volumes[i];
                if (v.when == LtCode.WhenVent)
                {
                    vents.Column(v, 0.55f, 0.12f, v.phase, 0f);
                    nVents++;
                }
                else if (v.when == LtCode.WhenFallsA || v.when == LtCode.WhenFallsB)
                {
                    sand.Column(v, 0.9f, 0.7f, v.when, i);
                    nFalls++;
                }
            }
            // The bridges' coral, each vertex with how far it is from its bridge's
            // nearer end toward the middle (the bridges' routes give their ends).
            var lanes = world.Lanes();
            var pts = world.LanePoints();
            var spans = new List<(Vector2 a, Vector2 b)>();
            foreach (var l in lanes)
                if (l.when == LtCode.WhenBridge && l.count >= 2)
                    spans.Add((new Vector2(pts[l.first].x, pts[l.first].y), new Vector2(pts[l.first + l.count - 1].x, pts[l.first + l.count - 1].y)));
            var coral = new Part();
            int nCoral = 0;
            foreach (var b in boxes)
            {
                if (b.kind != LtCode.CoverCoral)
                    continue;
                coral.Box(b, spans);
                nCoral++;
            }
            if (nVents + nFalls + nCoral == 0)
                return null;
            var root = new GameObject("Clockwork");
            root.transform.SetParent(parent, false);
            vents.Finish(root.transform, "Vent lifts", look.VentMaterial);
            sand.Finish(root.transform, "Sandfalls", look.SandMaterial);
            coral.Finish(root.transform, "Living bridges", look.CoralMaterial);
            Shader.SetGlobalVector(VentConstId, new Vector4(LtConst.VentCycle, LtConst.VentOn, LtConst.TickHz, LtConst.FallsSteps));
            Shader.SetGlobalVector(DayConstId, new Vector4(LtConst.DuskStep, LtConst.NightStep, LtConst.DawnStep, LtConst.BridgeDownStep));
            Debug.Log($"Lattice: clockwork: {nVents} vent lifts' columns, {nFalls} sandfalls, {nCoral} coral boxes in {spans.Count} living bridges");
            return new Clockwork(root);
        }

        /// <summary>This frame's clock to the shader (after the plugin's calls).</summary>
        public void Update(NetFrame frame, float dt)
        {
            if (!frame.Welcomed)
                return;
            var c = frame.Clock;
            // Between steps, run on by frame time; snap back where it's drifted.
            double target = c.cycleStep;
            if (cycle < 0 || c.step < lastStep || System.Math.Abs(cycle - target) > 4.0)
                cycle = target;
            else
                cycle = System.Math.Max(target, System.Math.Min(cycle + dt * LtConst.TickHz, target + 1.0));
            lastStep = c.step;
            double frac = cycle - target;
            Shader.SetGlobalFloat(CycleId, (float)cycle);
            Shader.SetGlobalFloat(VentId, (float)(c.step % (uint)LtConst.VentCycle + frac));
        }

        public void Dispose()
        {
            if (root != null)
                Object.Destroy(root);
        }

        /// <summary>A mesh being built, in Unity's space.</summary>
        sealed class Part
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<Vector2> uv1 = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, float v0, float v1, Vector2 data, Vector3 n)
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
                for (int i = 0; i < 4; i++)
                {
                    uv1.Add(data);
                    normals.Add(n);
                }
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

            /// <summary>A volume's column: its four sides, alpha from the foot's to the
            /// top's, <paramref name="a"/> and <paramref name="b"/> in uv1.</summary>
            public void Column(LtVolume v, float footAlpha, float topAlpha, float a, float b)
            {
                var (lo, hi) = (v.min, v.max);
                Vector3 P(float x, float y, float z) => Coords.ToUnity(x, y, z);
                var foot = new Color(1f, 1f, 1f, footAlpha);
                var top = new Color(1f, 1f, 1f, topAlpha);
                float h = hi.z - lo.z;
                var d = new Vector2(a, b);
                Quad(P(lo.x, lo.y, lo.z), P(hi.x, lo.y, lo.z), P(hi.x, lo.y, hi.z), P(lo.x, lo.y, hi.z), foot, top, 0f, h, d, Vector3.back);
                Quad(P(hi.x, lo.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, hi.y, hi.z), P(hi.x, lo.y, hi.z), foot, top, 0f, h, d, Vector3.right);
                Quad(P(hi.x, hi.y, lo.z), P(lo.x, hi.y, lo.z), P(lo.x, hi.y, hi.z), P(hi.x, hi.y, hi.z), foot, top, 0f, h, d, Vector3.forward);
                Quad(P(lo.x, hi.y, lo.z), P(lo.x, lo.y, lo.z), P(lo.x, lo.y, hi.z), P(lo.x, hi.y, hi.z), foot, top, 0f, h, d, Vector3.left);
            }

            /// <summary>A coral box (a wedge's top rising along its axis), each
            /// corner's place along its bridge in uv1.x: 0 at an end, 1 midway.</summary>
            public void Box(LtBox b, List<(Vector2 a, Vector2 b)> spans)
            {
                var corner = new Vector3[8];
                var order = new float[8];
                for (int i = 0; i < 8; i++)
                {
                    float along = (i & 1) == 0 ? -b.halfAlong : b.halfAlong;
                    float across = (i & 2) == 0 ? -b.halfAcross : b.halfAcross;
                    float x = b.centerX + along * b.axisX - across * b.axisY, y = b.centerY + along * b.axisY + across * b.axisX;
                    float top = Mathf.Lerp(b.low, b.max.z, (along + b.halfAlong) / (2f * b.halfAlong));
                    corner[i] = Coords.ToUnity(x, y, (i & 4) == 0 ? b.min.z : top);
                    order[i] = Order(new Vector2(x, y), spans);
                }
                var white = Color.white;
                var mid = Vector3.zero;
                foreach (var p in corner)
                    mid += p / 8f;
                void Face(int a, int bb, int c, int d)
                {
                    // Outward, whichever way round the corners go (drawn from both sides).
                    var n = Vector3.Cross(corner[bb] - corner[a], corner[d] - corner[a]).normalized;
                    if (Vector3.Dot(n, (corner[a] + corner[c]) * 0.5f - mid) < 0f)
                        n = -n;
                    int s = verts.Count;
                    foreach (int k in new[] { a, bb, c, d })
                    {
                        verts.Add(corner[k]);
                        normals.Add(n);
                        uvs.Add(Vector2.zero);
                        uv1.Add(new Vector2(order[k], 0f));
                        colors.Add(white);
                    }
                    tris.Add(s);
                    tris.Add(s + 2);
                    tris.Add(s + 1);
                    tris.Add(s);
                    tris.Add(s + 3);
                    tris.Add(s + 2);
                }
                // Bottom, top, then the four sides (corners: bit 0 along, 1 across, 2 up).
                Face(0, 1, 3, 2);
                Face(4, 6, 7, 5);
                Face(0, 4, 5, 1);
                Face(1, 5, 7, 3);
                Face(3, 7, 6, 2);
                Face(2, 6, 4, 0);
            }

            static float Order(Vector2 p, List<(Vector2 a, Vector2 b)> spans)
            {
                float best = float.MaxValue, order = 0f;
                foreach (var (a, b) in spans)
                {
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float d = (a + ab * t - p).sqrMagnitude;
                    if (d < best)
                        (best, order) = (d, 1f - Mathf.Abs(2f * t - 1f));
                }
                return order;
            }

            public void Finish(Transform parent, string name, Material material)
            {
                if (verts.Count == 0)
                    return;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetUVs(1, uv1);
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
