using System;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lattice.Shell
{
    /// <summary>
    /// The shell's controls, made in code on <see cref="UiKit"/>'s look: buttons
    /// (a plain one, and the primary one filled with the accent), sliders,
    /// switches, choices and a text field. Each eases between its states (rest,
    /// hover, pressed) instead of snapping.
    /// </summary>
    public static class Widgets
    {
        public static readonly Color Sheet = new Color(0.035f, 0.047f, 0.052f, 0.94f);
        public static readonly Color Card = new Color(0.07f, 0.09f, 0.098f, 0.9f);
        public static readonly Color Row = new Color(1f, 1f, 1f, 0.035f);
        public static readonly Color RowHover = new Color(1f, 1f, 1f, 0.085f);
        public static readonly Color Rule = new Color(1f, 1f, 1f, 0.1f);
        public static readonly Color Faint = new Color(0.42f, 0.47f, 0.47f, 1f);
        public static readonly Color DarkInk = new Color(0.04f, 0.05f, 0.055f, 1f);

        /// <summary>A rect anchored at its parent's top left, at (x, -y), of this size.</summary>
        public static RectTransform At(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = UiKit.Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
            return r;
        }

        public static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h, float size = 22f, bool mono = false, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = UiKit.Text("Label", parent, mono ? UiKit.Mono : UiKit.Display, size, color ?? UiKit.Ink, align);
            Place(t.rectTransform, x, y, w, h);
            t.text = text;
            return t;
        }

        /// <summary>A heading: small, spaced capitals over a rule.</summary>
        public static TextMeshProUGUI Heading(Transform parent, string text, float x, float y, float w)
        {
            var t = Label(parent, text, x, y, w, 26f, 17f, false, Faint);
            t.characterSpacing = 8f;
            var rule = UiKit.Box("Rule", parent, Rule);
            Place(rule.rectTransform, x, y + 27f, w, 1f);
            return t;
        }

        public static Image Box(Transform parent, Color color, float x, float y, float w, float h)
        {
            var b = UiKit.Box("Box", parent, color);
            Place(b.rectTransform, x, y, w, h);
            return b;
        }

        /// <summary>A clickable panel: a background that eases toward its hover and
        /// pressed tints, and a click.</summary>
        public static Clickable Clickable(Transform parent, string name, float x, float y, float w, float h, Color rest, Color hover, Action onClick)
        {
            var r = At(parent, name, x, y, w, h);
            var img = r.gameObject.AddComponent<Image>();
            img.color = rest;
            img.raycastTarget = true;
            var c = r.gameObject.AddComponent<Clickable>();
            c.Init(img, rest, hover, onClick);
            return c;
        }

        /// <summary>A button: plain (a dark panel with a hairline), or primary (filled
        /// with <paramref name="accent"/>, dark text).</summary>
        public static Clickable Button(Transform parent, string text, float x, float y, float w, float h, Action onClick, bool primary = false, Color? accent = null, float size = 24f)
        {
            var a = accent ?? UiKit.Accent;
            var rest = primary ? a : new Color(1f, 1f, 1f, 0.05f);
            var hover = primary ? Color.Lerp(a, Color.white, 0.25f) : new Color(1f, 1f, 1f, 0.12f);
            var c = Clickable(parent, $"Button {text}", x, y, w, h, rest, hover, onClick);
            if (!primary)
            {
                var line = UiKit.Box("Hairline", c.transform, new Color(1f, 1f, 1f, 0.14f));
                var lr = line.rectTransform;
                lr.anchorMin = new Vector2(0f, 0f);
                lr.anchorMax = new Vector2(1f, 0f);
                lr.pivot = new Vector2(0.5f, 0f);
                lr.sizeDelta = new Vector2(0f, 1f);
            }
            var t = UiKit.Text("Text", c.transform, UiKit.Display, size, primary ? DarkInk : UiKit.Ink, TextAlignmentOptions.Center);
            UiKit.Fill(t.rectTransform);
            t.text = text;
            t.characterSpacing = 4f;
            c.Text = t;
            return c;
        }

        /// <summary>A slider row: its label, the value as <paramref name="format"/>
        /// says, and a track to drag along.</summary>
        public static SliderRow Slider(Transform parent, string label, float x, float y, float w, float min, float max, float value, Func<float, string> format, Action<float> changed, float step = 0f)
        {
            var r = At(parent, $"Slider {label}", x, y, w, 64f);
            var s = r.gameObject.AddComponent<SliderRow>();
            s.Init(label, w, min, max, value, format, changed, step);
            return s;
        }

        /// <summary>A switch row: its label, and a pill that slides on or off.</summary>
        public static SwitchRow Switch(Transform parent, string label, float x, float y, float w, bool value, Action<bool> changed, string note = null)
        {
            var r = At(parent, $"Switch {label}", x, y, w, 48f);
            var s = r.gameObject.AddComponent<SwitchRow>();
            s.Init(label, w, value, changed, note);
            return s;
        }

        /// <summary>A text field: a label over an input box.</summary>
        public static TMP_InputField Field(Transform parent, string label, float x, float y, float w, string value, string placeholder)
        {
            Label(parent, label, x, y, w, 24f, 16f, false, Faint).characterSpacing = 6f;
            var box = Box(parent, new Color(0f, 0f, 0f, 0.35f), x, y + 28f, w, 46f);
            box.raycastTarget = true;
            var line = UiKit.Box("Underline", box.transform, new Color(1f, 1f, 1f, 0.22f));
            var lr = line.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.sizeDelta = new Vector2(0f, 2f);
            var area = UiKit.Rect("Text area", box.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(14f, 4f);
            area.offsetMax = new Vector2(-14f, -4f);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = UiKit.Text("Placeholder", area, UiKit.Mono, 20f, new Color(1f, 1f, 1f, 0.25f), TextAlignmentOptions.MidlineLeft);
            UiKit.Fill(ph.rectTransform);
            ph.text = placeholder;
            var text = UiKit.Text("Text", area, UiKit.Mono, 20f, UiKit.Ink, TextAlignmentOptions.MidlineLeft);
            UiKit.Fill(text.rectTransform);
            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = box;
            field.caretColor = UiKit.Accent;
            field.customCaretColor = true;
            field.caretWidth = 2;
            field.selectionColor = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.35f);
            field.fontAsset = UiKit.Mono;
            field.pointSize = 20f;
            field.text = value ?? "";
            return field;
        }

        /// <summary>A tiny picture made once: alpha from <paramref name="alpha"/> over
        /// [-1, 1]², white (the image's color tints it).</summary>
        public static Sprite MakeSprite(int size, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(alpha(u, v))));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>An antialiased edge: 1 inside, 0 outside, for a signed distance in
        /// the sprite's [-1, 1] units at <paramref name="size"/> pixels.</summary>
        public static float Edge(float d, int size) => Mathf.Clamp01(0.5f - d * size * 0.5f);
    }

    /// <summary>A panel that eases toward its hover and pressed tints, and clicks.</summary>
    public sealed class Clickable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        Image image;
        Color rest, hover;
        Action onClick;
        bool over, down;
        public TextMeshProUGUI Text;
        /// <summary>Off: grayed, and clicks do nothing.</summary>
        public bool Interactable = true;
        /// <summary>Held lit (a selected row).</summary>
        public bool Lit;

        public void Init(Image img, Color restColor, Color hoverColor, Action click)
        {
            (image, rest, hover, onClick) = (img, restColor, hoverColor, click);
        }

        public void SetColors(Color restColor, Color hoverColor) => (rest, hover) = (restColor, hoverColor);

        void Update()
        {
            var want = !Interactable ? new Color(rest.r, rest.g, rest.b, rest.a * 0.35f)
                : down ? Color.Lerp(hover, Color.white, 0.12f)
                : over || Lit ? hover
                : rest;
            image.color = Color.Lerp(image.color, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            if (Text != null)
                Text.alpha = Interactable ? 1f : 0.4f;
        }

        public void OnPointerEnter(PointerEventData e) => over = true;
        public void OnPointerExit(PointerEventData e) => (over, down) = (false, false);
        public void OnPointerDown(PointerEventData e) => down = e.button == PointerEventData.InputButton.Left;
        public void OnPointerUp(PointerEventData e) => down = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && Interactable)
                onClick?.Invoke();
        }
    }

    /// <summary>A labeled slider: drag along its track (or click on it).</summary>
    public sealed class SliderRow : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        float min, max, value, step;
        Func<float, string> format;
        Action<float> changed;
        TextMeshProUGUI shown;
        RectTransform track, fill, knob;
        float width;

        public float Value => value;

        public void Init(string label, float w, float lo, float hi, float v, Func<float, string> fmt, Action<float> onChange, float snap)
        {
            (min, max, format, changed, step, width) = (lo, hi, fmt, onChange, snap, w);
            Widgets.Label(transform, label, 0f, 0f, w * 0.6f, 30f, 22f);
            shown = Widgets.Label(transform, "", w * 0.6f, 0f, w * 0.4f, 30f, 19f, true, UiKit.Accent, TextAlignmentOptions.MidlineRight);
            var hit = Widgets.Box(transform, new Color(0f, 0f, 0f, 0f), 0f, 30f, w, 30f);
            hit.raycastTarget = true;
            track = Widgets.Box(transform, new Color(1f, 1f, 1f, 0.12f), 0f, 43f, w, 4f).rectTransform;
            fill = Widgets.Box(transform, UiKit.Accent, 0f, 43f, 0f, 4f).rectTransform;
            var k = Widgets.Box(transform, UiKit.Ink, 0f, 36f, 6f, 18f);
            knob = k.rectTransform;
            Set(v, false);
        }

        public void Set(float v, bool notify)
        {
            v = Mathf.Clamp(v, min, max);
            if (step > 0f)
                v = Mathf.Round(v / step) * step;
            value = v;
            float t = Mathf.InverseLerp(min, max, v);
            fill.sizeDelta = new Vector2(width * t, 4f);
            knob.anchoredPosition = new Vector2(width * t - 3f, -36f);
            shown.text = format(v);
            if (notify)
                changed?.Invoke(v);
        }

        void At(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(track, e.position, e.pressEventCamera, out var local))
            {
                // The track's pivot is its top left: x from 0 to its width.
                float t = Mathf.Clamp01(local.x / width);
                Set(Mathf.Lerp(min, max, t), true);
            }
        }

        public void OnPointerDown(PointerEventData e) => At(e);
        public void OnDrag(PointerEventData e) => At(e);
    }

    /// <summary>A labeled switch: a pill whose knob slides across.</summary>
    public sealed class SwitchRow : MonoBehaviour, IPointerClickHandler
    {
        bool value;
        Action<bool> changed;
        Image pill;
        RectTransform knob;
        float shown = -1f;

        public bool Value => value;

        public void Init(string label, float w, bool v, Action<bool> onChange, string note)
        {
            (value, changed) = (v, onChange);
            var hit = gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            Widgets.Label(transform, label, 0f, 0f, w - 80f, note == null ? 48f : 30f, 22f);
            if (note != null)
                Widgets.Label(transform, note, 0f, 28f, w - 80f, 20f, 15f, false, Widgets.Faint);
            pill = Widgets.Box(transform, new Color(1f, 1f, 1f, 0.12f), w - 58f, 12f, 58f, 24f);
            knob = Widgets.Box(pill.transform, UiKit.Ink, 3f, 3f, 18f, 18f).rectTransform;
            shown = value ? 1f : 0f;
            Draw();
        }

        public void Set(bool v)
        {
            value = v;
        }

        void Update()
        {
            float want = value ? 1f : 0f;
            if (Mathf.Abs(shown - want) < 0.001f)
                return;
            shown = Mathf.MoveTowards(shown, want, Time.unscaledDeltaTime * 8f);
            Draw();
        }

        void Draw()
        {
            knob.anchoredPosition = new Vector2(3f + 34f * shown, -3f);
            pill.color = Color.Lerp(new Color(1f, 1f, 1f, 0.12f), UiKit.Accent, shown);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left)
                return;
            value = !value;
            changed?.Invoke(value);
        }
    }
}
