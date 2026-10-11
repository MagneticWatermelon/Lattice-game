using System;
using System.Diagnostics;
using Lattice.Native;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// The ground (GDD §14.2, v0): 4 × 4 Unity Terrain tiles of 2,048 m, each
    /// 513 × 513 samples, which is exactly the game's 4 m height grid (tiles
    /// share their edge samples), filled from the plugin's heights. What you
    /// see is what you stand on: the terrain keeps every sample within reach
    /// (pixel error 1) and has no collider of its own (movement and shots are
    /// the plugin's).
    ///
    /// The splat (grass, meadow, soil, rock) comes from the ground itself:
    /// slope, broad noise for drier and greener patches, packed soil on the
    /// bases' flattened ground and worn soil around every piece of cover.
    /// It's computed in Burst jobs for all tiles at once, and the tiles are
    /// made one per frame (<see cref="Step"/>), so loading never stalls the
    /// connection. Once made, the heights and a map of the grass (from the
    /// splat) are handed on as textures, for the grass (<see cref="GrassField"/>).
    /// </summary>
    public sealed class TerrainBuilder : IDisposable
    {
        public const int Tiles = 4;
        public const int TileSamples = 513;
        public const float TileSize = 2048f;
        /// <summary>The grass map's side: 4 m texels, as the height grid.</summary>
        public const int GrassMapSize = 2048;

        public Terrain[] Terrains { get; } = new Terrain[Tiles * Tiles];
        public float HeightRange { get; private set; }
        /// <summary>The largest gap between the game's bilinear ground and any
        /// split of its 4 m cells into triangles (at a cell's center), m:
        /// what drawn triangles can differ from what feet stand on.</summary>
        public float GapP99 { get; private set; }
        public float GapMax { get; private set; }
        public bool Done => next >= Tiles * Tiles;
        /// <summary>Once done: the game's heights (m, one texel per 4 m sample) and the
        /// ground's grass (4 m texels: r how much is grass or meadow, g the meadow's
        /// share). Whoever takes them destroys them.</summary>
        public Texture2D HeightTexture { get; private set; }
        public Texture2D GrassTexture { get; private set; }

        readonly WorldLook look;
        readonly Transform parent;
        NativeArray<float> heights, gaps;
        NativeArray<float4> sites;
        NativeArray<LtBox> boxes;
        readonly NativeArray<Color32>[] splats = new NativeArray<Color32>[Tiles * Tiles];
        NativeArray<byte> grassMap;
        JobHandle jobs, gapJob, grassJob;
        readonly float[,] tileHeights = new float[TileSamples, TileSamples];
        int next;
        readonly Stopwatch clock = Stopwatch.StartNew();
        float maxHeight;

        public unsafe TerrainBuilder(LtWorldHandle world, WorldLook look, Transform parent)
        {
            this.look = look;
            this.parent = parent;
            int n = LtConst.TerrainN;
            heights = new NativeArray<float>(n * n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            world.Heights(0, 0, n, n, (float*)heights.GetUnsafePtr());
            maxHeight = 0f;
            for (int i = 0; i < heights.Length; i++)
                maxHeight = math.max(maxHeight, heights[i]);
            HeightRange = math.ceil((maxHeight + 16f) / 32f) * 32f;

            var siteList = world.Sites();
            sites = new NativeArray<float4>(siteList.Length, Allocator.Persistent);
            for (int i = 0; i < siteList.Length; i++)
                sites[i] = new float4(siteList[i].x, siteList[i].y, siteList[i].half, siteList[i].level);
            boxes = new NativeArray<LtBox>(world.Boxes(), Allocator.Persistent);

            gaps = new NativeArray<float>((n - 1) * (n - 1), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            gapJob = new GapJob { Heights = heights, N = n, Out = gaps }.Schedule(gaps.Length, 8192);

            int res = look.SplatResolution;
            int grassTile = GrassMapSize / Tiles;
            grassMap = new NativeArray<byte>(GrassMapSize * GrassMapSize * 2, Allocator.Persistent);
            var all = new NativeArray<JobHandle>(Tiles * Tiles, Allocator.Temp);
            for (int t = 0; t < Tiles * Tiles; t++)
            {
                float ox = (t % Tiles) * TileSize, oy = (t / Tiles) * TileSize;
                splats[t] = new NativeArray<Color32>(res * res, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var splat = new SplatJob { Heights = heights, Sites = sites, N = n, OriginX = ox, OriginY = oy, Texel = TileSize / res, Width = res, Out = splats[t] }
                    .Schedule(res * res, 2048);
                all[t] = new WearJob { Boxes = boxes, Splat = splats[t], Width = res, OriginX = ox, OriginY = oy, Texel = TileSize / res }.Schedule(splat);
                // The tiles' grass into the one map, a tile after another (they share it).
                grassJob = new GrassMapJob
                {
                    Splat = splats[t], SplatWidth = res, Width = grassTile, TileX = t % Tiles, TileY = t / Tiles, Side = GrassMapSize, Out = grassMap,
                }.Schedule(grassTile * grassTile, 4096, JobHandle.CombineDependencies(grassJob, all[t]));
            }
            jobs = JobHandle.CombineDependencies(all);
            all.Dispose();
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Makes the next tile once the jobs are done; true when all are made.</summary>
        public bool Step()
        {
            if (Done)
                return true;
            if (!jobs.IsCompleted)
                return false;
            jobs.Complete();
            Make(next++);
            if (Done)
            {
                for (int ty = 0; ty < Tiles; ty++)
                for (int tx = 0; tx < Tiles; tx++)
                    Terrains[ty * Tiles + tx].SetNeighbors(At(tx - 1, ty), At(tx, ty + 1), At(tx + 1, ty), At(tx, ty - 1));
                gapJob.Complete();
                gaps.Sort();
                GapP99 = gaps[(int)(gaps.Length * 0.99f)];
                GapMax = gaps[gaps.Length - 1];
                grassJob.Complete();
                int n = LtConst.TerrainN;
                HeightTexture = new Texture2D(n, n, TextureFormat.RFloat, false, true)
                    { name = "Ground heights", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                HeightTexture.SetPixelData(heights, 0);
                HeightTexture.Apply(false, true);
                GrassTexture = new Texture2D(GrassMapSize, GrassMapSize, TextureFormat.RG16, false, true)
                    { name = "Ground grass", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                GrassTexture.SetPixelData(grassMap, 0);
                GrassTexture.Apply(false, true);
                Debug.Log($"Lattice: terrain made in {clock.Elapsed.TotalSeconds:F2} s (heights to {maxHeight:F1} m, range {HeightRange} m); " +
                          $"drawn triangles differ from the ground by p99 {GapP99 * 100f:F2} cm, at most {GapMax * 100f:F1} cm");
                Dispose();
            }
            return Done;
        }

        Terrain At(int x, int y) => x < 0 || y < 0 || x >= Tiles || y >= Tiles ? null : Terrains[y * Tiles + x];

        unsafe void Make(int t)
        {
            int tx = t % Tiles, ty = t / Tiles, n = LtConst.TerrainN, res = look.SplatResolution;
            int x0 = tx * (TileSamples - 1), y0 = ty * (TileSamples - 1);
            float inv = 1f / HeightRange;
            for (int y = 0; y < TileSamples; y++)
            {
                int row = (y0 + y) * n + x0;
                for (int x = 0; x < TileSamples; x++)
                    tileHeights[y, x] = heights[row + x] * inv;
            }
            var td = new TerrainData
            {
                name = $"Ground {tx},{ty}",
                heightmapResolution = TileSamples,
                alphamapResolution = res,
                baseMapResolution = 1024,
            };
            td.size = new Vector3(TileSize, HeightRange, TileSize);
            td.SetHeights(0, 0, tileHeights);
            td.terrainLayers = look.Layers;
            // The splat straight into the control texture (layers 0-3 in RGBA),
            // or through the slow path if it isn't the format we write.
            var control = td.alphamapTextures[0];
            if (control.format == TextureFormat.RGBA32 && control.width == res)
            {
                control.SetPixelData(splats[t], 0);
                control.Apply(true);
                td.SetBaseMapDirty();
            }
            else
            {
                var maps = new float[res, res, 4];
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    var c = splats[t][y * res + x];
                    maps[y, x, 0] = c.r / 255f;
                    maps[y, x, 1] = c.g / 255f;
                    maps[y, x, 2] = c.b / 255f;
                    maps[y, x, 3] = c.a / 255f;
                }
                td.SetAlphamaps(0, 0, maps);
            }

            var go = new GameObject(td.name);
            go.transform.SetParent(parent, false);
            go.transform.position = Coords.ToUnity(tx * TileSize, ty * TileSize, 0f);
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = td;
            terrain.materialTemplate = look.TerrainMaterial;
            terrain.heightmapPixelError = look.PixelError;
            terrain.basemapDistance = look.BasemapDistance;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.allowAutoConnect = true;
            terrain.drawTreesAndFoliage = false;
            terrain.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            Terrains[t] = terrain;
            if (t == 0)
            {
                // What the first tile was given (a player build has once drawn it untextured).
                long r = 0, g = 0, b = 0, a = 0;
                var sp = splats[t];
                for (int i = 0; i < sp.Length; i += 97)
                    (r, g, b, a) = (r + sp[i].r, g + sp[i].g, b + sp[i].b, a + sp[i].a);
                int count = (sp.Length + 96) / 97;
                var l0 = td.terrainLayers.Length > 0 ? td.terrainLayers[0] : null;
                Debug.Log($"Lattice: terrain tile 0: control {control.format} {control.width}, splat mean {r / count}/{g / count}/{b / count}/{a / count}, " +
                          $"layers {td.terrainLayers.Length} (first {(l0 != null && l0.diffuseTexture != null ? l0.diffuseTexture.name + " " + l0.diffuseTexture.width : "none")}), " +
                          $"material {terrain.materialTemplate?.shader?.name} supported {terrain.materialTemplate?.shader?.isSupported} keywords [{string.Join(",", terrain.materialTemplate?.shaderKeywords ?? new string[0])}]");
            }
        }

        public void Dispose()
        {
            jobs.Complete();
            gapJob.Complete();
            grassJob.Complete();
            if (grassMap.IsCreated) grassMap.Dispose();
            if (heights.IsCreated) heights.Dispose();
            if (gaps.IsCreated) gaps.Dispose();
            if (sites.IsCreated) sites.Dispose();
            if (boxes.IsCreated) boxes.Dispose();
            for (int i = 0; i < splats.Length; i++)
                if (splats[i].IsCreated)
                    splats[i].Dispose();
        }

        static float Bilinear(NativeArray<float> h, int n, float x, float y)
        {
            float last = n - 1;
            float fx = math.clamp(x / LtConst.TerrainRes, 0f, last), fy = math.clamp(y / LtConst.TerrainRes, 0f, last);
            int ix = math.min((int)fx, n - 2), iy = math.min((int)fy, n - 2);
            float tx = fx - ix, ty = fy - iy;
            float h00 = h[iy * n + ix], h10 = h[iy * n + ix + 1], h01 = h[(iy + 1) * n + ix], h11 = h[(iy + 1) * n + ix + 1];
            float a = h00 + (h10 - h00) * tx, b = h01 + (h11 - h01) * tx;
            return a + (b - a) * ty;
        }

        static byte Byte(float v) => (byte)math.clamp((int)math.round(v * 255f), 0, 255);

        [BurstCompile]
        struct GapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Heights;
            public int N;
            [WriteOnly] public NativeArray<float> Out;

            public void Execute(int i)
            {
                int m = N - 1, x = i % m, y = i / m;
                float h00 = Heights[y * N + x], h10 = Heights[y * N + x + 1], h01 = Heights[(y + 1) * N + x], h11 = Heights[(y + 1) * N + x + 1];
                Out[i] = math.abs(h00 + h11 - h10 - h01) * 0.25f;
            }
        }

        [BurstCompile]
        struct SplatJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Heights;
            [ReadOnly] public NativeArray<float4> Sites;
            public int N, Width;
            public float OriginX, OriginY, Texel;
            [WriteOnly] public NativeArray<Color32> Out;

            public void Execute(int i)
            {
                int px = i % Width, py = i / Width;
                float x = OriginX + (px + 0.5f) * Texel, y = OriginY + (py + 0.5f) * Texel;
                const float e = 2f;
                float dx = (Bilinear(Heights, N, x + e, y) - Bilinear(Heights, N, x - e, y)) / (2f * e);
                float dy = (Bilinear(Heights, N, x, y + e) - Bilinear(Heights, N, x, y - e)) / (2f * e);
                float slope = math.sqrt(dx * dx + dy * dy);

                // The bases' and outposts' flattened ground: packed soil, fading
                // out over the blend back to the hills.
                float site = 0f;
                for (int s = 0; s < Sites.Length; s++)
                {
                    var st = Sites[s];
                    float d = math.max(math.abs(x - st.x), math.abs(y - st.y)) - st.z;
                    site = math.max(site, 1f - math.smoothstep(-6f, 14f, d));
                }

                var p = new float2(x, y);
                float broad = noise.snoise(p / 260f) + 0.45f * noise.snoise(p / 90f + 31.7f);
                float mid = noise.snoise(p / 38f + 7.3f);
                float fine = noise.snoise(p / 7.5f + 91.1f);

                float meadow = math.smoothstep(-0.25f, 0.6f, broad + 0.2f * mid + 0.1f * fine);
                float soil = math.smoothstep(0.32f, 0.62f, slope + 0.08f * mid)
                             + 0.5f * math.smoothstep(0.66f, 0.95f, mid * 0.7f + fine * 0.4f)
                             + 0.92f * site;
                soil = math.saturate(soil);
                float rock = math.smoothstep(0.6f, 0.95f, slope + 0.1f * fine);

                float g = (1f - rock) * (1f - soil);
                float4 w = new float4(g * (1f - meadow), g * meadow, (1f - rock) * soil, rock);
                Out[i] = new Color32(Byte(w.x), Byte(w.y), Byte(w.z), Byte(w.w));
            }
        }

        /// <summary>One tile's splat into its part of the grass map: each 4 m texel the
        /// mean of its splat texels' grass and meadow, and the meadow's share.</summary>
        [BurstCompile]
        struct GrassMapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> Splat;
            public int SplatWidth, Width, TileX, TileY, Side;
            [NativeDisableParallelForRestriction] public NativeArray<byte> Out;

            public void Execute(int i)
            {
                int lx = i % Width, ly = i / Width;
                int f = math.max(1, SplatWidth / Width);
                float grass = 0f, meadow = 0f;
                for (int y = 0; y < f; y++)
                for (int x = 0; x < f; x++)
                {
                    var c = Splat[math.min(ly * f + y, SplatWidth - 1) * SplatWidth + math.min(lx * f + x, SplatWidth - 1)];
                    grass += c.r;
                    meadow += c.g;
                }
                float k = 1f / (255f * f * f);
                grass *= k;
                meadow *= k;
                float amount = grass + meadow;
                int o = ((TileY * Width + ly) * Side + TileX * Width + lx) * 2;
                Out[o] = Byte(amount);
                Out[o + 1] = Byte(amount > 1e-3f ? meadow / amount : 0f);
            }
        }

        /// <summary>Packed, worn soil around every piece of cover: what feet and
        /// shovels do to the ground beside walls and crates.</summary>
        [BurstCompile]
        struct WearJob : IJob
        {
            [ReadOnly] public NativeArray<LtBox> Boxes;
            public NativeArray<Color32> Splat;
            public int Width;
            public float OriginX, OriginY, Texel;

            public void Execute()
            {
                const float Reach = 2.2f;
                float size = Width * Texel;
                for (int k = 0; k < Boxes.Length; k++)
                {
                    var b = Boxes[k];
                    float minX = b.min.x - Reach, maxX = b.max.x + Reach, minY = b.min.y - Reach, maxY = b.max.y + Reach;
                    if (maxX < OriginX || maxY < OriginY || minX > OriginX + size || minY > OriginY + size)
                        continue;
                    int x0 = math.max(0, (int)((minX - OriginX) / Texel)), x1 = math.min(Width - 1, (int)((maxX - OriginX) / Texel));
                    int y0 = math.max(0, (int)((minY - OriginY) / Texel)), y1 = math.min(Width - 1, (int)((maxY - OriginY) / Texel));
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = OriginX + (x + 0.5f) * Texel, py = OriginY + (y + 0.5f) * Texel;
                        float dx = math.max(0f, math.max(b.min.x - px, px - b.max.x));
                        float dy = math.max(0f, math.max(b.min.y - py, py - b.max.y));
                        float wear = 0.85f * (1f - math.smoothstep(0.3f, Reach, math.sqrt(dx * dx + dy * dy)));
                        if (wear <= 0f)
                            continue;
                        int i = y * Width + x;
                        var c = Splat[i];
                        float keep = 1f - wear;
                        Splat[i] = new Color32(Byte(c.r / 255f * keep), Byte(c.g / 255f * keep), Byte(c.b / 255f * keep + wear), Byte(c.a / 255f * keep));
                    }
                }
            }
        }
    }
}
