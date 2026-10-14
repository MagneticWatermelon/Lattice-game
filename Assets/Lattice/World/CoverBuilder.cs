using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// Every cover box of the world, drawn as its kind's model fitted exactly
    /// to the box (GDD §14.3: what you see is what blocks you): walls tiled
    /// along their length, the rest stretched once. Each piece has levels of
    /// detail: the model near, a plain box in the model's own color farther,
    /// nothing once it's a speck. Drawn by the world's <see cref="StaticField"/>
    /// (no GameObject each). Made a few boxes per step, for the caller to run
    /// steps within a frame's budget.
    /// </summary>
    public static class CoverBuilder
    {
        /// <summary>The cover, nearest <paramref name="near"/> (Unity) first; each step
        /// yields the share made so far.</summary>
        public static IEnumerator<float> Build(LtWorldHandle world, WorldLook look, StaticField field, Vector3 near)
        {
            var clock = Stopwatch.StartNew();
            var boxes = world.Boxes();
            var g = Coords.ToGame(near);
            var dist = new float[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                float dx = (boxes[i].min.x + boxes[i].max.x) * 0.5f - g.x, dy = (boxes[i].min.y + boxes[i].max.y) * 0.5f - g.y;
                dist[i] = dx * dx + dy * dy;
            }
            System.Array.Sort(dist, boxes);
            var cube = CubeMesh();
            // A kind per cover model (its model near, its plain box far), and the plain box
            // for any kind without one, always drawn.
            var kinds = new int[look.Cover.Length];
            for (int k = 0; k < look.Cover.Length; k++)
            {
                var model = look.Cover[k];
                if (model.Mesh == null)
                    continue;
                if (model.Mesh.subMeshCount > 1 || model.Materials.Length > 1)
                    Debug.LogWarning($"Lattice: cover model {model.Mesh.name} has {model.Mesh.subMeshCount} submeshes: only the first is drawn");
                kinds[k] = field.AddKind(
                    new StaticField.Level { Mesh = model.Mesh, Material = model.Materials[0], MinHeight = look.CoverDetailHeight, ViewCull = true },
                    new StaticField.Level { Mesh = cube, Material = model.Far != null ? model.Far : look.PlainCover, MinHeight = look.CoverFarHeight });
            }
            int plain = field.AddKind(new StaticField.Level { Mesh = cube, Material = look.PlainCover, MinHeight = 0f });
            // Kiln's blockout kinds: a plain box each, in its own material; and the
            // sloped part of a wedge (C1), a prism, in the same.
            var prism = PrismMesh();
            int plainPrism = field.AddKind(new StaticField.Level { Mesh = prism, Material = look.PlainCover, MinHeight = 0f });
            var blockout = new int[look.Blockout?.Length ?? 0];
            var blockoutPrism = new int[blockout.Length];
            for (int k = 0; k < blockout.Length; k++)
            {
                blockout[k] = look.Blockout[k] != null ? field.AddKind(new StaticField.Level { Mesh = cube, Material = look.Blockout[k], MinHeight = 0f }) : plain;
                blockoutPrism[k] = look.Blockout[k] != null ? field.AddKind(new StaticField.Level { Mesh = prism, Material = look.Blockout[k], MinHeight = 0f }) : plainPrism;
            }
            int models = 0, shaped = 0;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                // Shields are drawn with the force volumes (Connectors): see-through.
                if (b.kind == LtCode.CoverShield)
                    continue;
                // The living bridges' coral is there only at night: Clockwork draws it.
                if (b.when != LtCode.WhenAlways)
                    continue;
                // Sunsails' stalks are drawn with their membranes (Life).
                if (b.kind == LtCode.CoverStalk)
                    continue;
                var model = b.kind < look.Cover.Length ? look.Cover[b.kind] : default;
                var center = Coords.ToUnity((b.min.x + b.max.x) * 0.5f, (b.min.y + b.max.y) * 0.5f, b.min.z);
                var size = new Vector3(b.max.x - b.min.x, b.max.z - b.min.z, b.max.y - b.min.y);
                int block = b.kind - LtCode.CoverBlock;
                bool inBlockout = block >= 0 && block < blockout.Length;
                if (b.axisX != 1f || b.axisY != 0f || b.low != b.max.z)
                {
                    // Turned, or a wedge: plain, along its axis.
                    Shaped(field, inBlockout ? blockout[block] : plain, inBlockout ? blockoutPrism[block] : plainPrism, cube, prism, b);
                    shaped++;
                }
                else if (model.Mesh != null)
                    models += Place(field, kinds[b.kind], model, b, center, size, cube);
                else
                    Plain(field, inBlockout ? blockout[block] : plain, cube, center, size);
                if ((i & 15) == 15)
                    yield return (i + 1f) / boxes.Length;
            }
            Debug.Log($"Lattice: {boxes.Length} cover boxes ({models} model pieces, {shaped} turned or wedges) in {clock.Elapsed.TotalSeconds:F2} s");
        }

        /// <summary>A model fitted into box <paramref name="b"/>: its long side along
        /// the box's long side, its front to the box's facing (or, for a model whose
        /// front is an end, like a container's doors, to one end); walls tile it.</summary>
        static int Place(StaticField field, int kind, CoverModel model, LtBox b, Vector3 bottomCenter, Vector3 size, Mesh cube)
        {
            bool alongX = size.x >= size.z;
            float length = alongX ? size.x : size.z, depth = alongX ? size.z : size.x;
            // Facing 0 east, 1 north, 2 west, 3 south (game); the model's front is +z.
            float yaw = b.facing switch { 0 => 90f, 1 => 0f, 2 => -90f, _ => 180f };
            // The model's long axis (x, or z if its front is an end) must lie along
            // the box's: turn by 90 if the facing would put it across.
            bool longAlongX = (Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) < 0.5f) != model.FrontOnEnd;
            if (longAlongX != alongX)
                yaw += 90f;
            int pieces = model.TileAspect > 0f ? Mathf.Max(1, Mathf.RoundToInt(length / (size.y * model.TileAspect))) : 1;
            float piece = length / pieces;
            var dir = alongX ? Vector3.right : Vector3.forward;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            // The piece in the model's own axes: x across its front, z front to back.
            var scale = model.FrontOnEnd ? new Vector3(depth, size.y, piece) : new Vector3(piece, size.y, depth);
            // The levels share the piece's transform; its size and center for them are a
            // LOD group's: the model's and the box's bounds together, in the piece's frame.
            var local = Scaled(model.Mesh.bounds, scale);
            local.Encapsulate(Scaled(cube.bounds, scale));
            for (int i = 0; i < pieces; i++)
            {
                var m = Matrix4x4.TRS(bottomCenter + dir * ((i + 0.5f) * piece - length * 0.5f), rot, Vector3.one);
                field.Add(kind, m * Matrix4x4.Scale(scale), World(m, local), Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z)));
            }
            return pieces;
        }

        /// <summary>A turned box or a wedge (C1): its footprint along its axis, its
        /// level part a box, a wedge's sloped part a prism on it, rising along the axis.</summary>
        static void Shaped(StaticField field, int boxKind, int prismKind, Mesh cube, Mesh prism, LtBox b)
        {
            var rot = Quaternion.LookRotation(new Vector3(b.axisX, 0f, b.axisY), Vector3.up);
            var bottom = Coords.ToUnity(b.centerX, b.centerY, b.min.z);
            float level = Mathf.Min(b.low, b.max.z) - b.min.z;
            float across = 2f * b.halfAcross, along = 2f * b.halfAlong;
            if (level > 0.01f)
            {
                var m = Matrix4x4.TRS(bottom, rot, new Vector3(across, level, along));
                field.Add(boxKind, m, World(m, cube.bounds), Mathf.Max(along, Mathf.Max(across, level)));
            }
            float rise = b.max.z - b.low;
            if (rise > 0.01f)
            {
                var m = Matrix4x4.TRS(bottom + Vector3.up * level, rot, new Vector3(across, rise, along));
                field.Add(prismKind, m, World(m, prism.bounds), Mathf.Max(along, Mathf.Max(across, rise)));
            }
        }

        static void Plain(StaticField field, int kind, Mesh cube, Vector3 bottomCenter, Vector3 size)
        {
            var m = Matrix4x4.TRS(bottomCenter, Quaternion.identity, size);
            field.Add(kind, m, World(m, cube.bounds), Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        }

        static Bounds Scaled(Bounds b, Vector3 s) => new Bounds(Vector3.Scale(b.center, s), Vector3.Scale(b.size, s));

        /// <summary>Box <paramref name="b"/> (in <paramref name="m"/>'s frame) as world bounds.</summary>
        public static Bounds World(in Matrix4x4 m, Bounds b)
        {
            var w = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            var e = b.extents;
            for (int i = 0; i < 8; i++)
                w.Encapsulate(m.MultiplyPoint3x4(b.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return w;
        }

        static Mesh prismMesh;

        /// <summary>A unit wedge standing on its base (x and z in -0.5..0.5): its top
        /// rising from y 0 at z -0.5 to y 1 at z +0.5 (a wedge's sloped part, along
        /// its axis); flat-shaded, facing out.</summary>
        public static Mesh PrismMesh()
        {
            if (prismMesh != null)
                return prismMesh;
            var a = new Vector3(-0.5f, 0f, -0.5f); // low edge, left
            var b = new Vector3(0.5f, 0f, -0.5f);  // low edge, right
            var c = new Vector3(-0.5f, 0f, 0.5f);
            var d = new Vector3(0.5f, 0f, 0.5f);
            var e = new Vector3(-0.5f, 1f, 0.5f);  // high edge
            var f = new Vector3(0.5f, 1f, 0.5f);
            // Faces: the slope, the back, the base, the two ends.
            var faces = new[] { new[] { a, b, f, e }, new[] { c, d, f, e }, new[] { a, b, d, c }, new[] { a, c, e }, new[] { b, d, f } };
            var center = new Vector3(0f, 0.3f, 0.15f);
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            foreach (var face in faces)
            {
                var nrm = Vector3.Cross(face[1] - face[0], face[2] - face[0]).normalized;
                var mid = Vector3.zero;
                foreach (var p in face)
                    mid += p / face.Length;
                if (Vector3.Dot(nrm, mid - center) < 0f)
                    nrm = -nrm;
                int s = v.Count;
                foreach (var p in face)
                {
                    v.Add(p);
                    n.Add(nrm);
                    uv.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
                }
                for (int k = 1; k + 1 < face.Length; k++)
                {
                    // Wound as CubeMesh's: each triangle's cross product along its normal.
                    var w = Vector3.Cross(face[k] - face[0], face[k + 1] - face[0]);
                    bool flip = Vector3.Dot(w, nrm) < 0f;
                    tri.Add(s);
                    tri.Add(flip ? s + k + 1 : s + k);
                    tri.Add(flip ? s + k : s + k + 1);
                }
            }
            var m = new Mesh { name = "Unit wedge (base at 0)" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            prismMesh = m;
            return m;
        }

        static Mesh cubeMesh;

        /// <summary>A unit box standing on its base (x and z in -0.5..0.5, y in 0..1),
        /// with world-sized UVs left to the shader.</summary>
        public static Mesh CubeMesh()
        {
            if (cubeMesh != null)
                return cubeMesh;
            var m = new Mesh { name = "Unit box (base at 0)" };
            var v = new Vector3[24];
            var nrm = new Vector3[24];
            var uv = new Vector2[24];
            var tri = new int[36];
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int f = 0; f < 6; f++)
            {
                var nf = normals[f];
                var u = f < 2 ? Vector3.forward : Vector3.right;
                var w = Vector3.Cross(nf, u);
                for (int k = 0; k < 4; k++)
                {
                    float su = (k & 1) == 0 ? -0.5f : 0.5f, sw = (k & 2) == 0 ? -0.5f : 0.5f;
                    var p = nf * 0.5f + u * su + w * sw;
                    v[f * 4 + k] = new Vector3(p.x, p.y + 0.5f, p.z);
                    nrm[f * 4 + k] = nf;
                    uv[f * 4 + k] = new Vector2(su + 0.5f, sw + 0.5f);
                }
                int b = f * 4;
                tri[f * 6 + 0] = b;
                tri[f * 6 + 1] = b + 2;
                tri[f * 6 + 2] = b + 1;
                tri[f * 6 + 3] = b + 1;
                tri[f * 6 + 4] = b + 2;
                tri[f * 6 + 5] = b + 3;
            }
            m.vertices = v;
            m.normals = nrm;
            m.uv = uv;
            m.triangles = tri;
            // Facing out: flip any face whose winding points in.
            for (int f = 0; f < 6; f++)
            {
                var a = v[tri[f * 6]];
                var c = Vector3.Cross(v[tri[f * 6 + 1]] - a, v[tri[f * 6 + 2]] - a);
                if (Vector3.Dot(c, normals[f]) < 0f)
                {
                    (tri[f * 6 + 1], tri[f * 6 + 2]) = (tri[f * 6 + 2], tri[f * 6 + 1]);
                    (tri[f * 6 + 4], tri[f * 6 + 5]) = (tri[f * 6 + 5], tri[f * 6 + 4]);
                }
            }
            m.triangles = tri;
            m.RecalculateTangents();
            m.RecalculateBounds();
            cubeMesh = m;
            return m;
        }
    }
}
