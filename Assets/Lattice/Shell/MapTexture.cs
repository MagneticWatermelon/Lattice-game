using System.Threading.Tasks;
using Lattice.Native;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The continent as the map draws it (GDD §9.2): the ground by what it's
    /// made of, lit from the north-west and ruled with faint 20 m contours,
    /// and what's built on it (the cover's footprints) darker. One pixel per
    /// height sample's cell (4 m: 2048² for an 8 km continent). Made once a
    /// world, on a thread: only the upload is the main thread's.
    /// </summary>
    public static class MapTexture
    {
        public const int Size = 2048;

        /// <summary>Starts making the picture of <paramref name="world"/>; the task's
        /// result is its pixels (row by row from the south, as a texture has them).</summary>
        public static unsafe Task<Color32[]> Start(LtWorldHandle world)
        {
            var info = world.Info;
            int n = (int)info.samples;
            var heights = new float[n * n];
            world.Heights(0, 0, n, n, heights);
            var mats = new byte[n * n];
            fixed (byte* m = mats)
                world.Materials(0, 0, n, n, m);
            var boxes = world.Boxes();
            float res = info.res;
            return Task.Run(() => Paint(heights, mats, boxes, n, res));
        }

        public static Texture2D Upload(Color32[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "Continent map" };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Color Ground(byte m) => m switch
        {
            LtCode.GroundSand => new Color(0.74f, 0.62f, 0.45f),
            LtCode.GroundGlass => new Color(0.62f, 0.74f, 0.72f),
            LtCode.GroundSalt => new Color(0.86f, 0.84f, 0.78f),
            LtCode.GroundOchre => new Color(0.66f, 0.38f, 0.23f),
            LtCode.GroundBasalt => new Color(0.27f, 0.26f, 0.27f),
            LtCode.GroundPale => new Color(0.80f, 0.76f, 0.68f),
            LtCode.GroundRoad => new Color(0.86f, 0.80f, 0.66f),
            LtCode.GroundPad => new Color(0.58f, 0.57f, 0.54f),
            LtCode.GroundScree => new Color(0.52f, 0.45f, 0.38f),
            // A world that doesn't say (Harrow): its hills.
            _ => new Color(0.45f, 0.52f, 0.36f),
        };

        static Color32[] Paint(float[] h, byte[] mats, LtBox[] boxes, int n, float res)
        {
            var px = new Color32[Size * Size];
            var light = new Vector3(-0.55f, 0.6f, 0.9f).normalized;
            float scale = (n - 1) / (float)Size;
            float H(int x, int y) => h[Mathf.Clamp(y, 0, n - 1) * n + Mathf.Clamp(x, 0, n - 1)];
            // Built-up cells: the cover's footprints, raised above the ground (a deck,
            // a wall, a rock), as how high they stand over it.
            var built = new float[Size * Size];
            float cell = res / scale;
            foreach (var b in boxes)
            {
                float rise = b.max.z - H((int)(b.centerX / res), (int)(b.centerY / res));
                if (rise < 1.2f)
                    continue;
                int x0 = Mathf.Max(0, (int)(b.min.x / cell)), x1 = Mathf.Min(Size - 1, (int)(b.max.x / cell));
                int y0 = Mathf.Max(0, (int)(b.min.y / cell)), y1 = Mathf.Min(Size - 1, (int)(b.max.y / cell));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    // Turned boxes: only the cells inside their own footprint.
                    float wx = (x + 0.5f) * cell - b.centerX, wy = (y + 0.5f) * cell - b.centerY;
                    float along = wx * b.axisX + wy * b.axisY, across = -wx * b.axisY + wy * b.axisX;
                    if (Mathf.Abs(along) > b.halfAlong + cell * 0.5f || Mathf.Abs(across) > b.halfAcross + cell * 0.5f)
                        continue;
                    int k = y * Size + x;
                    built[k] = Mathf.Max(built[k], rise);
                }
            }
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int sx = Mathf.Min((int)(x * scale), n - 1), sy = Mathf.Min((int)(y * scale), n - 1);
                float z = H(sx, sy);
                float dx = (H(sx + 1, sy) - H(sx - 1, sy)) / (2f * res);
                float dy = (H(sx, sy + 1) - H(sx, sy - 1)) / (2f * res);
                var normal = new Vector3(-dx, -dy, 1f).normalized;
                float lit = Mathf.Clamp01(Vector3.Dot(normal, light));
                var c = Ground(mats[sy * n + sx]);
                // The land's lay: lit faces bright, shaded ones dark; the deep a
                // little darker than the heights.
                float shade = 0.5f + 0.62f * lit;
                float depth = Mathf.Lerp(0.82f, 1.06f, Mathf.Clamp01(z / 420f));
                c *= shade * depth;
                // Contours every 20 m, a stronger one every 100 m.
                float z1 = H(sx + 1, sy), z2 = H(sx, sy + 1);
                if (Mathf.Floor(z / 20f) != Mathf.Floor(z1 / 20f) || Mathf.Floor(z / 20f) != Mathf.Floor(z2 / 20f))
                    c *= Mathf.Floor(z / 100f) != Mathf.Floor(Mathf.Max(z1, z2) / 100f) ? 0.8f : 0.9f;
                float rise = built[y * Size + x];
                if (rise > 0f)
                {
                    // Built: a cool gray, darker the taller (towers read as such).
                    var stone = new Color(0.36f, 0.37f, 0.39f) * Mathf.Lerp(1.05f, 0.7f, Mathf.Clamp01(rise / 30f));
                    c = Color.Lerp(c, stone, 0.85f);
                }
                // Muted, so the map's marks stand out.
                float gray = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
                c = Color.Lerp(new Color(gray, gray, gray), c, 0.78f) * 0.82f;
                c.a = 1f;
                px[y * Size + x] = c;
            }
            return px;
        }
    }
}
