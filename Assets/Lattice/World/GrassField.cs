using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// Grass on the ground around the camera, where the splat is grass or meadow
    /// (green on grass, straw on meadow), out to <see cref="WorldLook.GrassDistance"/>,
    /// past which the terrain's own texture carries it. Every frame, after the
    /// camera is placed, a compute shader (Grass.compute) scatters clumps over a
    /// grid anchored in the world, culled to the view, and the clumps are drawn in
    /// one indirect draw (Lattice/Grass): the main thread only sets a few values.
    /// Cosmetic: nothing collides with it, and it's never tall enough to hide a
    /// soldier (they always stand, 1.8 m).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class GrassField : MonoBehaviour
    {
        const int Blades = 14, Segments = 3;
        /// <summary>How far a clump's blades spread from its middle, and a blade's half
        /// width at the root (m).</summary>
        const float ClumpRadius = 0.17f, BladeHalfWidth = 0.012f;
        const float Seed = 7919f;

        static readonly int ClumpsId = Shader.PropertyToID("_Clumps"), HeightsId = Shader.PropertyToID("_Heights"),
            GrassMapId = Shader.PropertyToID("_GrassMap"), GridId = Shader.PropertyToID("_Grid"), EyeId = Shader.PropertyToID("_Eye"),
            ThinId = Shader.PropertyToID("_Thin"), SizeId = Shader.PropertyToID("_Size"), MapId = Shader.PropertyToID("_Map"),
            PlanesId = Shader.PropertyToID("_Planes"), PixelId = Shader.PropertyToID("_GrassPixel");

        /// <summary>Not drawn (--off grass: to measure what it costs).</summary>
        public static bool Off;

        WorldLook look;
        ComputeShader compute;
        int kernel, cells, groups;
        GraphicsBuffer clumps, args;
        Mesh mesh;
        RenderParams draw;
        Texture2D heights, grassMap;
        readonly Plane[] planes = new Plane[6];
        readonly Vector4[] planeData = new Vector4[6];

        /// <summary>The grass, given the ground's heights and grass map (it owns them
        /// from here); null where it can't be drawn.</summary>
        public static GrassField Create(Transform parent, WorldLook look, Texture2D heights, Texture2D grassMap)
        {
            if (look.GrassMaterial == null || look.GrassCompute == null || heights == null || grassMap == null || !SystemInfo.supportsComputeShaders)
                return null;
            var go = new GameObject("Grass");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GrassField>();
            g.Init(look, heights, grassMap);
            return g;
        }

        void Init(WorldLook look, Texture2D heights, Texture2D grassMap)
        {
            this.look = look;
            this.heights = heights;
            this.grassMap = grassMap;
            cells = Mathf.CeilToInt(2f * look.GrassDistance / look.GrassCell);
            groups = (cells + 7) / 8;
            // At most a clump per cell within the circle (no view culling counted).
            int capacity = Mathf.CeilToInt(cells * cells * Mathf.PI / 4f * Mathf.Clamp01(look.GrassDensity)) + 1024;
            clumps = new GraphicsBuffer(GraphicsBuffer.Target.Append, capacity, 32);
            mesh = ClumpMesh();
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            args.SetData(new[]
            {
                new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance = mesh.GetIndexCount(0),
                    startIndex = mesh.GetIndexStart(0),
                    baseVertexIndex = mesh.GetBaseVertex(0),
                },
            });
            compute = look.GrassCompute;
            kernel = compute.FindKernel("Scatter");
            compute.SetBuffer(kernel, ClumpsId, clumps);
            compute.SetTexture(kernel, HeightsId, heights);
            compute.SetTexture(kernel, GrassMapId, grassMap);
            compute.SetVector(MapId, new Vector4(Coords.Half, LtConst.TerrainRes, LtConst.TerrainN, LtConst.WorldSize));
            var props = new MaterialPropertyBlock();
            props.SetBuffer(ClumpsId, clumps);
            draw = new RenderParams(look.GrassMaterial)
            {
                matProps = props,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                layer = 0,
            };
        }

        void LateUpdate() => Draw(Camera.main);

        /// <summary>Scatters the clumps for <paramref name="cam"/>'s view and queues their
        /// draw for this frame (each frame, once the camera is placed; tools rendering a
        /// camera by hand call it first).</summary>
        public void Draw(Camera cam)
        {
            if (cam == null || clumps == null || Off)
                return;
            float max = look.GrassDistance, cell = look.GrassCell;
            var eye = cam.transform.position;
            compute.SetVector(GridId, new Vector4(Mathf.Floor((eye.x - max) / cell), Mathf.Floor((eye.z - max) / cell), cell, cells));
            compute.SetVector(EyeId, new Vector4(eye.x, eye.y, eye.z, max));
            compute.SetVector(ThinId, new Vector4(look.GrassThinFrom, look.GrassFarKeep, look.GrassDensity, Seed));
            compute.SetVector(SizeId, new Vector4(look.GrassHeight.x, look.GrassHeight.y, look.GrassWidth.x, look.GrassWidth.y));
            GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix, planes);
            for (int i = 0; i < 6; i++)
                planeData[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            compute.SetVectorArray(PlanesId, planeData);
            clumps.SetCounterValue(0);
            compute.Dispatch(kernel, groups, groups, 1);
            GraphicsBuffer.CopyCount(clumps, args, sizeof(uint));
            Shader.SetGlobalFloat(PixelId, 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight));
            draw.camera = cam;
            draw.worldBounds = new Bounds(eye, new Vector3(2f * max + 4f, 600f, 2f * max + 4f));
            Graphics.RenderMeshIndirect(draw, mesh, args);
        }

        /// <summary>How many clumps the last scatter made (reads the GPU back: tools only).</summary>
        public int CountForTools()
        {
            var a = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            args.GetData(a);
            return (int)a[0].instanceCount;
        }

        void OnDestroy()
        {
            clumps?.Release();
            args?.Release();
            clumps = args = null;
            if (mesh != null)
                Destroy(mesh);
            if (heights != null)
                Destroy(heights);
            if (grassMap != null)
                Destroy(grassMap);
        }

        /// <summary>
        /// One clump, 1 m tall (the shader sizes it): blades rising from points in a
        /// small disk, each a strip tapering in three segments to a point, curving out
        /// from the clump's middle. Positions are the blades' centerlines; tangents are
        /// each vertex's offset across its blade (so width scales without moving the
        /// blade); uv0 (across, along), uv1 (the blade's random, its root half width),
        /// uv2 its root in the disk.
        /// </summary>
        static Mesh ClumpMesh()
        {
            var rnd = new System.Random(20261012);
            float R() => (float)rnd.NextDouble();
            int perBlade = Segments * 2 + 1;
            int count = Blades * perBlade;
            var pos = new Vector3[count];
            var nrm = new Vector3[count];
            var tan = new Vector4[count];
            var uv0 = new Vector2[count];
            var uv1 = new Vector2[count];
            var uv2 = new Vector2[count];
            var tri = new int[Blades * (Segments * 2 - 1) * 3];
            int vi = 0, ti = 0;
            for (int b = 0; b < Blades; b++)
            {
                float a = R() * 2f * Mathf.PI, r = ClumpRadius * Mathf.Sqrt(R());
                var root = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float leanAt = a + (R() - 0.5f) * 1.2f, lean = 0.15f + 0.45f * R();
                float tall = 0.45f + 0.55f * R();
                float half = BladeHalfWidth * (0.75f + 0.5f * R());
                float face = R() * 2f * Mathf.PI;
                var side = new Vector3(Mathf.Cos(face), 0f, Mathf.Sin(face));
                float rand = R();
                int first = vi;
                for (int s = 0; s <= Segments; s++)
                {
                    float t = s / (float)Segments;
                    var center = new Vector3(Mathf.Cos(leanAt) * lean * t * t, t, Mathf.Sin(leanAt) * lean * t * t) * tall;
                    var along = new Vector3(Mathf.Cos(leanAt) * lean * 2f * t, 1f, Mathf.Sin(leanAt) * lean * 2f * t).normalized;
                    var normal = Vector3.Cross(side, along).normalized;
                    int sides = s < Segments ? 2 : 1;
                    for (int k = 0; k < sides; k++)
                    {
                        float across = sides == 1 ? 0f : (k == 0 ? -1f : 1f);
                        float w = half * (1f - 0.7f * t);
                        pos[vi] = center;
                        nrm[vi] = normal;
                        tan[vi] = new Vector4(side.x * across * w, 0f, side.z * across * w, 0f);
                        uv0[vi] = new Vector2(sides == 1 ? 0.5f : k, t);
                        uv1[vi] = new Vector2(rand, half);
                        uv2[vi] = root;
                        vi++;
                    }
                }
                for (int s = 0; s < Segments - 1; s++)
                {
                    int a0 = first + 2 * s, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                    tri[ti++] = a0; tri[ti++] = b0; tri[ti++] = a1;
                    tri[ti++] = a1; tri[ti++] = b0; tri[ti++] = b1;
                }
                int last = first + 2 * (Segments - 1);
                tri[ti++] = last; tri[ti++] = first + 2 * Segments; tri[ti++] = last + 1;
            }
            var mesh = new Mesh { name = "Grass clump" };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tri, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one * 2f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
