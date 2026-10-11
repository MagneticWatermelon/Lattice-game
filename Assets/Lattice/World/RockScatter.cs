using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Lattice.World
{
    /// <summary>
    /// Small rock clusters on the open ground, in about every third 32 m cell,
    /// off the sites and clear of cover. Cosmetic: too low to look like cover,
    /// and nothing in the game collides with them. Placed by the Bevy client's
    /// hash (client/src/models.rs, spawn_rocks), so both clients draw them in
    /// the same places. Made a row of cells per step, for the caller's frame budget.
    /// </summary>
    public static class RockScatter
    {
        const float Cell = 32f;
        /// <summary>How far clear of a cover box a rock stays (m).</summary>
        const float Clear = 2f;

        public static IEnumerator Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            if (look.RockMesh == null)
                yield break;
            var clock = Stopwatch.StartNew();
            var root = new GameObject("Rocks");
            root.transform.SetParent(parent, false);
            var sites = world.Sites();
            var boxes = world.Boxes();
            int cells = (int)(LtConst.WorldSize / Cell);
            var near = BoxesByCell(boxes, cells);
            ulong seed = world.Info.seed;
            int n = 0;
            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    ulong h = unchecked((seed ^ 0x5EED0F0CUL) + (((ulong)cx * 0x9E3779B97F4A7C15UL) ^ ((ulong)cy * 0xC2B2AE3D27D4EB4FUL)));
                    if (Next(ref h) > 0.33f)
                        continue;
                    float x = (cx + Next(ref h)) * Cell, y = (cy + Next(ref h)) * Cell;
                    if (OnSite(sites, x, y) || NearBox(boxes, near[cy * cells + cx], x, y))
                        continue;
                    float w = 0.8f + 0.8f * Next(ref h), tall = 0.35f + 0.3f * Next(ref h), yaw = Next(ref h) * 2f * Mathf.PI;
                    float depth = w * (0.8f + 0.4f * Next(ref h));
                    var go = new GameObject("Rock");
                    go.transform.SetParent(root.transform, false);
                    go.transform.SetPositionAndRotation(Coords.ToUnity(x, y, world.Height(x, y) - 0.08f), Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
                    go.transform.localScale = new Vector3(w, tall, depth);
                    go.AddComponent<MeshFilter>().sharedMesh = look.RockMesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = look.RockMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    var lods = go.AddComponent<LODGroup>();
                    lods.SetLODs(new[] { new LOD(look.RockCullHeight, new Renderer[] { mr }) });
                    lods.fadeMode = LODFadeMode.None;
                    lods.RecalculateBounds();
                    n++;
                }
                yield return null;
            }
            Debug.Log($"Lattice: {n} rocks in {clock.Elapsed.TotalSeconds:F2} s");
        }

        /// <summary>The Bevy client's: a splitmix-style step, 24 bits as [0, 1).</summary>
        static float Next(ref ulong h)
        {
            h ^= h >> 33;
            h = unchecked(h * 0xFF51AFD7ED558CCDUL);
            h ^= h >> 29;
            return (h >> 40) / (float)(1UL << 24);
        }

        static bool OnSite(LtSite[] sites, float x, float y)
        {
            foreach (var s in sites)
                if (Mathf.Max(Mathf.Abs(s.x - x), Mathf.Abs(s.y - y)) < s.half + LtConst.SiteBlend)
                    return true;
            return false;
        }

        static bool NearBox(LtBox[] boxes, List<int> list, float x, float y)
        {
            if (list == null)
                return false;
            foreach (int i in list)
            {
                var b = boxes[i];
                if (x >= b.min.x - Clear && x <= b.max.x + Clear && y >= b.min.y - Clear && y <= b.max.y + Clear)
                    return true;
            }
            return false;
        }

        /// <summary>Each cell's boxes (grown by <see cref="Clear"/>), by index.</summary>
        static List<int>[] BoxesByCell(LtBox[] boxes, int cells)
        {
            var grid = new List<int>[cells * cells];
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                int x0 = Mathf.Max(0, (int)((b.min.x - Clear) / Cell)), x1 = Mathf.Min(cells - 1, (int)((b.max.x + Clear) / Cell));
                int y0 = Mathf.Max(0, (int)((b.min.y - Clear) / Cell)), y1 = Mathf.Min(cells - 1, (int)((b.max.y + Clear) / Cell));
                for (int cy = y0; cy <= y1; cy++)
                    for (int cx = x0; cx <= x1; cx++)
                        (grid[cy * cells + cx] ??= new List<int>()).Add(i);
            }
            return grid;
        }
    }
}
