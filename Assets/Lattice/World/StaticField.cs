using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// The world's static props (the cover, the rocks) drawn through one
    /// BatchRendererGroup, with no GameObject, renderer or LOD group each. As
    /// renderers, their 69,000 renderers and 45,000 LOD groups cost a
    /// 1,000-player fight's main thread about 2 ms at p99 (culling, LOD, the
    /// GPU Resident Drawer's bookkeeping) for things that never move.
    ///
    /// Here their transforms go to the GPU once. Each piece keeps its levels of
    /// detail, with the thresholds a LOD group would apply (its share of the
    /// screen's height, with the quality's LOD bias), picked by a Burst job only
    /// when the camera has moved a few meters or zoomed. The views draw what
    /// the last pick chose: levels marked for it are culled to the view by
    /// 128 m cell (the plain far boxes aren't: cheaper drawn than culled), and
    /// the shadows come from the pieces within the shadow distance, each cell
    /// into the cascades whose spheres it touches.
    /// </summary>
    public sealed unsafe class StaticField : IDisposable
    {
        /// <summary>A level of detail: a mesh's first submesh in a material, shown while
        /// the piece is at least <see cref="MinHeight"/> of the screen's height.</summary>
        public struct Level
        {
            public Mesh Mesh;
            public Material Material;
            public float MinHeight;
            /// <summary>Culled to the view by cell (worth it for detailed meshes).</summary>
            public bool ViewCull;
        }

        const float CellSize = 128f;
        const int Cells = 64; // 8,192 m / 128 m
        /// <summary>How far the camera moves, or how much its field of view changes, before
        /// the levels are picked again (the transitions are hundreds of meters out).</summary>
        const float MoveAgain = 4f, ZoomAgain = 0.015f;

        struct Staged
        {
            public int Kind;
            public Matrix4x4 Matrix;
            public Vector3 Center, Min, Max;
            public float Size;
        }

        readonly List<Level[]> kinds = new List<Level[]>();
        List<Staged> staged = new List<Staged>();

        // Per instance (sorted by cell, then kind).
        NativeArray<float3> centers;
        NativeArray<float> sizes;
        NativeArray<byte> kindOf;
        NativeArray<ushort> cellOf;
        // Per cell: the bounds of what's in it.
        NativeArray<float3> cellMin, cellMax;
        // Per kind: its first group and level count. Per group (a kind's level):
        // its threshold, where its picks go, and where its cell ranges go.
        NativeArray<int> kindGroup, kindLevels, groupBase, rangeBase;
        NativeArray<float> minHeight;
        bool[] viewCull;
        BatchMeshID[] meshes;
        BatchMaterialID[] materials;
        int groups;

        BatchRendererGroup brg;
        GraphicsBuffer data;
        BatchID batch;
        float shadowReach;

        /// <summary>A pick: per group, the chosen instances (by cell) and their cell ranges
        /// (cell, start, count); the same again for the shadow casters.</summary>
        sealed class Pick : IDisposable
        {
            public NativeArray<int> Indices, Counts, RangeCounts, ShadowIndices, ShadowCounts, ShadowRangeCounts;
            public NativeArray<int4> Ranges, ShadowRanges;

            public Pick(int indices, int ranges, int groups)
            {
                Indices = new NativeArray<int>(indices, Allocator.Persistent);
                ShadowIndices = new NativeArray<int>(indices, Allocator.Persistent);
                Ranges = new NativeArray<int4>(ranges, Allocator.Persistent);
                ShadowRanges = new NativeArray<int4>(ranges, Allocator.Persistent);
                Counts = new NativeArray<int>(groups, Allocator.Persistent);
                ShadowCounts = new NativeArray<int>(groups, Allocator.Persistent);
                RangeCounts = new NativeArray<int>(groups, Allocator.Persistent);
                ShadowRangeCounts = new NativeArray<int>(groups, Allocator.Persistent);
            }

            public void Dispose()
            {
                Indices.Dispose(); ShadowIndices.Dispose(); Ranges.Dispose(); ShadowRanges.Dispose();
                Counts.Dispose(); ShadowCounts.Dispose(); RangeCounts.Dispose(); ShadowRangeCounts.Dispose();
            }
        }

        Pick front, back;
        /// <summary>Scratch: each shadow range's cascades.</summary>
        int[] masks = new int[256];
        JobHandle picking;
        bool pickRunning, picked;
        Vector3 pickedAt;
        float pickedK;

        /// <summary>The instances (for the report).</summary>
        public int Count => centers.IsCreated ? centers.Length : staged.Count;

        /// <summary>A kind of piece: its levels, most detailed first.</summary>
        public int AddKind(params Level[] levels)
        {
            kinds.Add(levels);
            return kinds.Count - 1;
        }

        /// <summary>One piece: its transform, world bounds, and size for its levels (as a
        /// LOD group's: its largest dimension).</summary>
        public void Add(int kind, in Matrix4x4 matrix, in Bounds bounds, float size)
        {
            staged.Add(new Staged { Kind = kind, Matrix = matrix, Center = bounds.center, Min = bounds.min, Max = bounds.max, Size = size });
        }

        static int CellOf(Vector3 p)
        {
            int cx = Mathf.Clamp((int)((p.x + Cells * CellSize * 0.5f) / CellSize), 0, Cells - 1);
            int cz = Mathf.Clamp((int)((p.z + Cells * CellSize * 0.5f) / CellSize), 0, Cells - 1);
            return cz * Cells + cx;
        }

        /// <summary>Everything added goes to the GPU; the field starts drawing.</summary>
        public void Finish(float shadowDistance)
        {
            int n = staged.Count;
            shadowReach = shadowDistance + 8f;
            var cellKeys = new int[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                cellKeys[i] = CellOf(staged[i].Center) * 256 + staged[i].Kind;
                order[i] = i;
            }
            Array.Sort(cellKeys, order);

            centers = new NativeArray<float3>(n, Allocator.Persistent);
            sizes = new NativeArray<float>(n, Allocator.Persistent);
            kindOf = new NativeArray<byte>(n, Allocator.Persistent);
            cellOf = new NativeArray<ushort>(n, Allocator.Persistent);
            cellMin = new NativeArray<float3>(Cells * Cells, Allocator.Persistent);
            cellMax = new NativeArray<float3>(Cells * Cells, Allocator.Persistent);
            for (int c = 0; c < Cells * Cells; c++)
            {
                cellMin[c] = new float3(float.MaxValue);
                cellMax[c] = new float3(float.MinValue);
            }
            var perKind = new int[kinds.Count];
            var cellsPerKind = new int[kinds.Count];
            var lastCell = new int[kinds.Count];
            for (int k = 0; k < kinds.Count; k++)
                lastCell[k] = -1;

            // The instance data: a zero block, then each matrix and its inverse, packed 3x4.
            int f4 = 4 + n * 6;
            var gpu = new NativeArray<float4>(f4, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < n; i++)
            {
                var s = staged[order[i]];
                int cell = cellKeys[i] / 256;
                centers[i] = s.Center;
                sizes[i] = s.Size;
                kindOf[i] = (byte)s.Kind;
                cellOf[i] = (ushort)cell;
                cellMin[cell] = math.min(cellMin[cell], (float3)s.Min);
                cellMax[cell] = math.max(cellMax[cell], (float3)s.Max);
                perKind[s.Kind]++;
                if (lastCell[s.Kind] != cell)
                {
                    lastCell[s.Kind] = cell;
                    cellsPerKind[s.Kind]++;
                }
                Pack(gpu, 4 + i * 3, s.Matrix);
                Pack(gpu, 4 + n * 3 + i * 3, s.Matrix.inverse);
            }
            staged = null;

            // Groups: a kind's levels, each with its share of the pick buffers.
            groups = 0;
            foreach (var levels in kinds)
                groups += levels.Length;
            kindGroup = new NativeArray<int>(kinds.Count, Allocator.Persistent);
            kindLevels = new NativeArray<int>(kinds.Count, Allocator.Persistent);
            groupBase = new NativeArray<int>(groups, Allocator.Persistent);
            rangeBase = new NativeArray<int>(groups, Allocator.Persistent);
            minHeight = new NativeArray<float>(groups, Allocator.Persistent);
            viewCull = new bool[groups];
            meshes = new BatchMeshID[groups];
            materials = new BatchMaterialID[groups];
            brg = new BatchRendererGroup(Cull, IntPtr.Zero);
            int g = 0, indexTotal = 0, rangeTotal = 0;
            for (int k = 0; k < kinds.Count; k++)
            {
                kindGroup[k] = g;
                kindLevels[k] = kinds[k].Length;
                foreach (var level in kinds[k])
                {
                    groupBase[g] = indexTotal;
                    rangeBase[g] = rangeTotal;
                    indexTotal += perKind[k];
                    rangeTotal += cellsPerKind[k];
                    minHeight[g] = level.MinHeight;
                    viewCull[g] = level.ViewCull;
                    meshes[g] = brg.RegisterMesh(level.Mesh);
                    materials[g] = brg.RegisterMaterial(level.Material);
                    g++;
                }
            }
            front = new Pick(Math.Max(indexTotal, 1), Math.Max(rangeTotal, 1), groups);
            back = new Pick(Math.Max(indexTotal, 1), Math.Max(rangeTotal, 1), groups);

            data = new GraphicsBuffer(GraphicsBuffer.Target.Raw, f4 * 4, 4);
            data.SetData(gpu);
            gpu.Dispose();
            var meta = new NativeArray<MetadataValue>(2, Allocator.Temp);
            meta[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = 0x80000000u | 64u };
            meta[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = 0x80000000u | (uint)(64 + n * 48) };
            batch = brg.AddBatch(meta, data.bufferHandle);
            meta.Dispose();
            brg.SetGlobalBounds(new Bounds(Vector3.zero, new Vector3(Cells * CellSize, 4000f, Cells * CellSize)));
        }

        /// <summary>A matrix's top three rows, column by column (BatchRendererGroup's
        /// float3x4), into three float4s at <paramref name="at"/>.</summary>
        static void Pack(NativeArray<float4> gpu, int at, in Matrix4x4 m)
        {
            gpu[at] = new float4(m.m00, m.m10, m.m20, m.m01);
            gpu[at + 1] = new float4(m.m11, m.m21, m.m02, m.m12);
            gpu[at + 2] = new float4(m.m22, m.m03, m.m13, m.m23);
        }

        /// <summary>Each frame: the last pick, once done, is what the views draw; a new
        /// one starts if the camera has moved or zoomed enough since.</summary>
        public void Update(Camera cam)
        {
            if (brg == null || cam == null)
                return;
            if (pickRunning && (picking.IsCompleted || !picked))
            {
                picking.Complete();
                (front, back) = (back, front);
                pickRunning = false;
                picked = true;
            }
            float k = QualitySettings.lodBias / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var eye = cam.transform.position;
            if (pickRunning || (picked && (eye - pickedAt).sqrMagnitude < MoveAgain * MoveAgain && Mathf.Abs(k / pickedK - 1f) < ZoomAgain))
                return;
            (pickedAt, pickedK) = (eye, k);
            picking = new PickJob
            {
                Centers = centers, Sizes = sizes, Kinds = kindOf, Cells = cellOf,
                KindGroup = kindGroup, KindLevels = kindLevels, MinHeight = minHeight, GroupBase = groupBase, RangeBase = rangeBase,
                Eye = eye, K = k, ShadowReach = shadowReach,
                Indices = back.Indices, Counts = back.Counts, Ranges = back.Ranges, RangeCounts = back.RangeCounts,
                ShadowIndices = back.ShadowIndices, ShadowCounts = back.ShadowCounts, ShadowRanges = back.ShadowRanges, ShadowRangeCounts = back.ShadowRangeCounts,
            }.Schedule();
            pickRunning = true;
            // The first pick is waited for: nothing would draw until it's done.
            if (!picked)
                Update(cam);
        }

        [BurstCompile]
        struct PickJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Centers;
            [ReadOnly] public NativeArray<float> Sizes;
            [ReadOnly] public NativeArray<byte> Kinds;
            [ReadOnly] public NativeArray<ushort> Cells;
            [ReadOnly] public NativeArray<int> KindGroup, KindLevels, GroupBase, RangeBase;
            [ReadOnly] public NativeArray<float> MinHeight;
            public float3 Eye;
            public float K, ShadowReach;
            public NativeArray<int> Indices, Counts, RangeCounts, ShadowIndices, ShadowCounts, ShadowRangeCounts;
            public NativeArray<int4> Ranges, ShadowRanges;

            public void Execute()
            {
                for (int g = 0; g < Counts.Length; g++)
                    Counts[g] = RangeCounts[g] = ShadowCounts[g] = ShadowRangeCounts[g] = 0;
                for (int i = 0; i < Centers.Length; i++)
                {
                    float d = math.max(math.distance(Eye, Centers[i]), 0.01f);
                    float h = Sizes[i] * K / d;
                    int kind = Kinds[i], first = KindGroup[kind], levels = KindLevels[kind], g = -1;
                    for (int l = 0; l < levels; l++)
                    {
                        if (h >= MinHeight[first + l])
                        {
                            g = first + l;
                            break;
                        }
                    }
                    if (g < 0)
                        continue;
                    int cell = Cells[i];
                    Append(Indices, Counts, Ranges, RangeCounts, g, i, cell);
                    if (d - Sizes[i] < ShadowReach)
                        Append(ShadowIndices, ShadowCounts, ShadowRanges, ShadowRangeCounts, g, i, cell);
                }
            }

            void Append(NativeArray<int> indices, NativeArray<int> counts, NativeArray<int4> ranges, NativeArray<int> rangeCounts, int g, int i, int cell)
            {
                int c = counts[g];
                indices[GroupBase[g] + c] = i;
                counts[g] = c + 1;
                int r = rangeCounts[g], at = RangeBase[g] + r - 1;
                if (r > 0 && ranges[at].x == cell)
                {
                    var range = ranges[at];
                    range.z++;
                    ranges[at] = range;
                }
                else
                {
                    ranges[RangeBase[g] + r] = new int4(cell, c, 1, 0);
                    rangeCounts[g] = r + 1;
                }
            }
        }

        /// <summary>A view's draws (the main thread, during culling): the camera's from the
        /// pick, culled by cell where marked; a light's from the shadow casters, each cell
        /// into the cascades its bounds' sphere touches.</summary>
        JobHandle Cull(BatchRendererGroup group, BatchCullingContext ctx, BatchCullingOutput output, IntPtr user)
        {
            if (!picked || (ctx.cullingLayerMask & 1u) == 0)
                return default;
            bool light = ctx.viewType == BatchCullingViewType.Light;
            if (!light && ctx.viewType != BatchCullingViewType.Camera)
                return default;
            var counts = light ? front.ShadowCounts : front.Counts;
            int total = 0;
            for (int g = 0; g < groups; g++)
                total += counts[g];
            if (total == 0)
                return default;
            int maxCommands = groups * (light ? 15 : 1);
            int align = UnsafeUtility.AlignOf<long>();
            var cmds = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
            var commands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * maxCommands, align, Allocator.TempJob);
            var visible = (int*)UnsafeUtility.Malloc(sizeof(int) * total, align, Allocator.TempJob);
            var ranges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), align, Allocator.TempJob);
            int nCommands = 0, nVisible = 0;
            if (!light)
            {
                var split = ctx.cullingSplits.Length > 0 ? ctx.cullingSplits[0] : new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = ctx.cullingPlanes.Length };
                for (int g = 0; g < groups; g++)
                {
                    int count = front.Counts[g];
                    if (count == 0)
                        continue;
                    int start = nVisible;
                    var src = (int*)front.Indices.GetUnsafeReadOnlyPtr() + groupBase[g];
                    if (!viewCull[g])
                    {
                        UnsafeUtility.MemCpy(visible + nVisible, src, count * sizeof(int));
                        nVisible += count;
                    }
                    else
                    {
                        int rb = rangeBase[g];
                        for (int r = 0; r < front.RangeCounts[g]; r++)
                        {
                            var range = front.Ranges[rb + r];
                            if (!InView(ctx.cullingPlanes, split.cullingPlaneOffset, split.cullingPlaneCount, cellMin[range.x], cellMax[range.x]))
                                continue;
                            UnsafeUtility.MemCpy(visible + nVisible, src + range.y, range.z * sizeof(int));
                            nVisible += range.z;
                        }
                    }
                    if (nVisible > start)
                        commands[nCommands++] = Command(g, start, nVisible - start, 0xff);
                }
            }
            else
            {
                var splits = ctx.cullingSplits;
                int nSplits = Math.Min(splits.Length, 4);
                for (int g = 0; g < groups; g++)
                {
                    if (front.ShadowCounts[g] == 0)
                        continue;
                    var src = (int*)front.ShadowIndices.GetUnsafeReadOnlyPtr() + groupBase[g];
                    int rb = rangeBase[g], rc = front.ShadowRangeCounts[g];
                    if (masks.Length < rc)
                        masks = new int[Math.Max(rc, masks.Length * 2)];
                    int seen = 0;
                    for (int r = 0; r < rc; r++)
                    {
                        var range = front.ShadowRanges[rb + r];
                        masks[r] = Cascades(splits, nSplits, cellMin[range.x], cellMax[range.x]);
                        seen |= 1 << masks[r];
                    }
                    // A command per set of cascades: the ranges whose cells touch exactly those.
                    for (int mask = 1; mask < (1 << nSplits); mask++)
                    {
                        if ((seen & (1 << mask)) == 0)
                            continue;
                        int start = nVisible;
                        for (int r = 0; r < rc; r++)
                        {
                            if (masks[r] != mask)
                                continue;
                            var range = front.ShadowRanges[rb + r];
                            UnsafeUtility.MemCpy(visible + nVisible, src + range.y, range.z * sizeof(int));
                            nVisible += range.z;
                        }
                        commands[nCommands++] = Command(g, start, nVisible - start, (ushort)mask);
                    }
                }
            }
            ranges[0] = new BatchDrawRange
            {
                drawCommandsType = BatchDrawCommandType.Direct,
                drawCommandsBegin = 0,
                drawCommandsCount = (uint)nCommands,
                filterSettings = new BatchFilterSettings
                {
                    renderingLayerMask = 1,
                    layer = 0,
                    motionMode = MotionVectorGenerationMode.Camera,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    staticShadowCaster = false,
                    allDepthSorted = false,
                },
            };
            cmds->drawCommands = commands;
            cmds->drawCommandCount = nCommands;
            cmds->visibleInstances = visible;
            cmds->visibleInstanceCount = nVisible;
            cmds->drawRanges = ranges;
            cmds->drawRangeCount = nCommands > 0 ? 1 : 0;
            cmds->instanceSortingPositions = null;
            cmds->instanceSortingPositionFloatCount = 0;
            cmds->drawCommandPickingEntityIds = null;
            return default;
        }

        BatchDrawCommand Command(int g, int offset, int count, ushort splits) => new BatchDrawCommand
        {
            flags = BatchDrawCommandFlags.None,
            batchID = batch,
            materialID = materials[g],
            meshID = meshes[g],
            submeshIndex = 0,
            splitVisibilityMask = splits,
            lightmapIndex = 0xFFFF,
            sortingPosition = 0,
            visibleOffset = (uint)offset,
            visibleCount = (uint)count,
        };

        /// <summary>The box is at least partly inside every plane.</summary>
        static bool InView(NativeArray<Plane> planes, int offset, int count, float3 min, float3 max)
        {
            float3 c = (min + max) * 0.5f, e = (max - min) * 0.5f;
            for (int i = offset; i < offset + count; i++)
            {
                var p = planes[i];
                float3 n = p.normal;
                if (math.dot(n, c) + p.distance < -math.dot(math.abs(n), e))
                    return false;
            }
            return true;
        }

        /// <summary>Which cascades a box's bounding sphere touches.</summary>
        static int Cascades(NativeArray<CullingSplit> splits, int n, float3 min, float3 max)
        {
            float3 c = (min + max) * 0.5f;
            float r = math.length(max - min) * 0.5f;
            int mask = 0;
            for (int s = 0; s < n; s++)
            {
                var split = splits[s];
                float reach = split.sphereRadius + r;
                if (math.distancesq(c, (float3)split.sphereCenter) <= reach * reach)
                    mask |= 1 << s;
            }
            return mask;
        }

        public void Dispose()
        {
            picking.Complete();
            brg?.Dispose();
            brg = null;
            data?.Dispose();
            front?.Dispose();
            back?.Dispose();
            if (centers.IsCreated)
            {
                centers.Dispose(); sizes.Dispose(); kindOf.Dispose(); cellOf.Dispose(); cellMin.Dispose(); cellMax.Dispose();
                kindGroup.Dispose(); kindLevels.Dispose(); groupBase.Dispose(); rangeBase.Dispose(); minHeight.Dispose();
            }
        }
    }
}
