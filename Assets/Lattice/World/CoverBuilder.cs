using System.Diagnostics;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// Every cover box of the world, drawn as its kind's model fitted exactly
    /// to the box (GDD §14.3: what you see is what blocks you): walls tiled
    /// along their length, the rest stretched once. Each piece is a level of
    /// detail: the model near, a plain box in the model's own color farther,
    /// nothing once it's a speck. Static renderers: the GPU Resident Drawer
    /// instances them.
    /// </summary>
    public static class CoverBuilder
    {
        public static GameObject Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            var clock = Stopwatch.StartNew();
            var root = new GameObject("Cover");
            root.transform.SetParent(parent, false);
            var boxes = world.Boxes();
            var cube = CubeMesh();
            int models = 0;
            foreach (var b in boxes)
            {
                var model = b.kind < look.Cover.Length ? look.Cover[b.kind] : default;
                var center = Coords.ToUnity((b.min.x + b.max.x) * 0.5f, (b.min.y + b.max.y) * 0.5f, b.min.z);
                var size = new Vector3(b.max.x - b.min.x, b.max.z - b.min.z, b.max.y - b.min.y);
                if (model.Mesh != null)
                {
                    models += Place(root.transform, model, look, b, center, size, cube);
                    continue;
                }
                Plain(root.transform, look.PlainCover, cube, center, size, b.kind);
            }
            Debug.Log($"Lattice: {boxes.Length} cover boxes ({models} model pieces) in {clock.Elapsed.TotalSeconds:F2} s");
            return root;
        }

        /// <summary>A model fitted into box <paramref name="b"/>: its long side along
        /// the box's long side, its front to the box's facing (or, for a model whose
        /// front is an end, like a container's doors, to one end); walls tile it.</summary>
        static int Place(Transform root, CoverModel model, WorldLook look, LtBox b, Vector3 bottomCenter, Vector3 size, Mesh cube)
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
            for (int i = 0; i < pieces; i++)
            {
                var go = new GameObject("Cover");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(bottomCenter + dir * ((i + 0.5f) * piece - length * 0.5f), rot);
                var near = Piece(go.transform, "Model", model.Mesh, model.Materials, scale);
                var far = Piece(go.transform, "Far", cube, model.Far != null ? new[] { model.Far } : new[] { look.PlainCover }, scale);
                var lods = go.AddComponent<LODGroup>();
                lods.SetLODs(new[]
                {
                    new LOD(look.CoverDetailHeight, new Renderer[] { near }),
                    new LOD(look.CoverFarHeight, new Renderer[] { far }),
                });
                lods.fadeMode = LODFadeMode.None;
                lods.RecalculateBounds();
            }
            return pieces;
        }

        static MeshRenderer Piece(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return mr;
        }

        static void Plain(Transform root, Material mat, Mesh cube, Vector3 bottomCenter, Vector3 size, byte kind)
        {
            var go = new GameObject("Cover");
            go.transform.SetParent(root, false);
            go.transform.position = bottomCenter;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
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
