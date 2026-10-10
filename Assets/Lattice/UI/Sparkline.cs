using UnityEngine;
using UnityEngine.UI;

namespace Lattice.UI
{
    /// <summary>
    /// A bar per sample (the newest on the right), scaled to <see cref="Max"/>:
    /// the net graph's frame times and snapshot gaps. Bars over
    /// <see cref="Warn"/> turn warm. Rebuilt from the same arrays each frame,
    /// without allocating.
    /// </summary>
    public sealed class Sparkline : MaskableGraphic
    {
        public float[] Values;
        public int Count;
        public float Max = 33.3f;
        public float Warn = 20f;
        public Color Hot = new Color(0.95f, 0.55f, 0.3f, 0.95f);
        public int Slots = 120;

        public void SetData(float[] values, int count)
        {
            Values = values;
            Count = count;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Values == null || Count <= 0)
                return;
            var r = GetPixelAdjustedRect();
            float w = r.width / Slots;
            float gap = Mathf.Min(1f, w * 0.25f);
            int first = Mathf.Max(0, Count - Slots);
            for (int i = first; i < Count; i++)
            {
                float v = Values[i];
                float h = Mathf.Clamp(v / Max, 0.02f, 1f) * r.height;
                float x0 = r.xMin + (Slots - (Count - i)) * w;
                var c = v > Warn ? Hot : color;
                int k = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, r.yMin), c, Vector4.zero);
                vh.AddVert(new Vector3(x0, r.yMin + h), c, Vector4.zero);
                vh.AddVert(new Vector3(x0 + w - gap, r.yMin + h), c, Vector4.zero);
                vh.AddVert(new Vector3(x0 + w - gap, r.yMin), c, Vector4.zero);
                vh.AddTriangle(k, k + 1, k + 2);
                vh.AddTriangle(k + 2, k + 3, k);
            }
        }
    }
}
