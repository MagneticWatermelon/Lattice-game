using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.UI
{
    /// <summary>
    /// The HUD's look and the bits it's built from, made in code (no prefabs):
    /// a screen-space canvas scaled to 1080p, and text in the GDD's own type:
    /// Saira Condensed for display, IBM Plex Mono for numbers.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Ink = new Color(0.86f, 0.89f, 0.88f, 1f);
        public static readonly Color Muted = new Color(0.56f, 0.61f, 0.60f, 1f);
        public static readonly Color Panel = new Color(0.055f, 0.075f, 0.08f, 0.72f);
        public static readonly Color Accent = new Color(0.56f, 0.65f, 1f, 1f);
        public static readonly Color Warn = new Color(0.89f, 0.51f, 0.31f, 1f);
        public static readonly Color Good = new Color(0.36f, 0.85f, 0.55f, 1f);

        static TMP_FontAsset display, mono;

        public static TMP_FontAsset Display => display ??= Load("Fonts/SairaCondensed-SemiBold SDF");
        public static TMP_FontAsset Mono => mono ??= Load("Fonts/IBMPlexMono-Medium SDF");

        static TMP_FontAsset Load(string path)
        {
            var f = Resources.Load<TMP_FontAsset>(path);
            if (f == null)
            {
                Debug.LogWarning($"Lattice: no font {path} (Lattice > Setup > Fonts makes it); using TMP's default");
                f = TMP_Settings.defaultFontAsset;
            }
            return f;
        }

        /// <summary>A full-screen overlay canvas, scaled as if the screen were 1080 px tall.</summary>
        public static Canvas Canvas(string name, int order, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.pixelPerfect = false;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;
            return c;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>Stretches <paramref name="r"/> over its parent, inset by <paramref name="pad"/>.</summary>
        public static void Fill(RectTransform r, float pad = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }
    }
}
