using Lattice.Native;
using UnityEngine;

namespace Lattice.World
{
    /// <summary>
    /// Where the Glasslands' glass reeds grow (K4, docs/kiln.md "The life"):
    /// in fields on the fused glass, patches a couple of hundred meters across,
    /// the same on every client (the plugin's ground and fixed noise). A map a
    /// texel per 4 m height cell (R: how thick the reeds are, as the grass map
    /// has it, so <see cref="GrassField"/> scatters them), and its densities
    /// for the chimes (<see cref="Density"/>). Cosmetic: no reed is tall enough
    /// to hide anyone.
    /// </summary>
    public sealed class Reeds
    {
        public const int Side = TerrainBuilder.GrassMapSize;
        readonly byte[] density;
        public Texture2D Map { get; }

        Reeds(byte[] density, Texture2D map) => (this.density, Map) = (density, map);

        public static unsafe Reeds Build(LtWorldHandle world)
        {
            int n = LtConst.TerrainN;
            var ground = new byte[n * n];
            fixed (byte* g = ground)
                world.Materials(0, 0, n, n, g);
            var density = new byte[Side * Side];
            var texels = new byte[Side * Side * 2];
            int count = 0;
            // (For looking at them: the thickest reeds nearest the Glasslands' middle.)
            var middle = new Vector2(LtConst.WorldSize / 2f + 3100f * Mathf.Cos(30f * Mathf.Deg2Rad), LtConst.WorldSize / 2f + 3100f * Mathf.Sin(30f * Mathf.Deg2Rad));
            var show = Vector2.zero;
            float showDist = float.MaxValue;
            for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                if (ground[y * n + x] != LtCode.GroundGlass || ground[y * n + x + 1] != LtCode.GroundGlass || ground[(y + 1) * n + x] != LtCode.GroundGlass)
                    continue;
                float gx = (x + 0.5f) * LtConst.TerrainRes, gy = (y + 0.5f) * LtConst.TerrainRes;
                // Fields: broad patches, their edges broken up.
                float field = Mathf.PerlinNoise(gx / 230f + 13.7f, gy / 230f + 4.1f) + 0.25f * (Mathf.PerlinNoise(gx / 45f + 7.3f, gy / 45f + 1.9f) - 0.5f);
                float d = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.56f, 0.7f, field));
                if (d <= 0f)
                    continue;
                byte b = (byte)Mathf.RoundToInt(d * 255f);
                density[y * Side + x] = b;
                texels[(y * Side + x) * 2] = b;
                count++;
                if (b == 255 && Vector2.Distance(new Vector2(gx, gy), middle) < showDist)
                    (show, showDist) = (new Vector2(gx, gy), Vector2.Distance(new Vector2(gx, gy), middle));
            }
            var map = new Texture2D(Side, Side, TextureFormat.RG16, false, true)
                { name = "Glass reeds", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            map.SetPixelData(texels, 0);
            map.Apply(false, true);
            Debug.Log($"Lattice: glass reeds on {count * LtConst.TerrainRes * LtConst.TerrainRes / 10000f:F0} ha (thick at ({show.x:F0}, {show.y:F0}))");
            return new Reeds(density, map);
        }

        /// <summary>How thick the reeds are at game (<paramref name="gx"/>, <paramref name="gy"/>), 0-1.</summary>
        public float Density(float gx, float gy)
        {
            int x = (int)(gx / LtConst.TerrainRes), y = (int)(gy / LtConst.TerrainRes);
            if (x < 0 || y < 0 || x >= Side || y >= Side)
                return 0f;
            return density[y * Side + x] / 255f;
        }
    }
}
