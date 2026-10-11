using Lattice.LocalPlayer;
using TMPro;
using UnityEngine;

namespace Lattice.UI
{
    /// <summary>
    /// The controls (F1), and while the mouse is free, how to take it back. The
    /// text is written once; frames only show or hide it. Self-checks (a player
    /// that aims or moves itself) don't show the prompt.
    /// </summary>
    public sealed class HelpCard : MonoBehaviour
    {
        static readonly (string key, string what)[] Rows =
        {
            ("W A S D", "move"),
            ("Shift", "sprint"),
            ("Space", "jump"),
            ("Left mouse", "fire"),
            ("Right mouse", "aim down sights"),
            ("V", "first person, chase, spectator"),
            ("I", "colors: friend or foe, or faction"),
            ("N", "net graph"),
            ("G", "the server's view (ghosts)"),
            ("T", "color by how the netcode draws"),
            ("Esc", "free the mouse"),
            ("F1", "these controls"),
        };

        RectTransform card, prompt;

        public static HelpCard Create(Transform canvas)
        {
            var go = new GameObject("Help", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            UiKit.Fill((RectTransform)go.transform);
            var h = go.AddComponent<HelpCard>();
            h.Build((RectTransform)go.transform);
            return h;
        }

        void Build(RectTransform root)
        {
            const float rowH = 34f, pad = 28f;
            float height = pad * 2f + 54f + Rows.Length * rowH;
            card = UiKit.Rect("Controls", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, height));
            var bg = UiKit.Box("Panel", card, new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 0.86f));
            UiKit.Fill(bg.rectTransform);
            var title = UiKit.Text("Title", card, UiKit.Display, 34f, UiKit.Ink);
            Place(title.rectTransform, new Vector2(pad, -pad), new Vector2(560f, 44f));
            title.text = "CONTROLS";
            title.characterSpacing = 6f;
            for (int i = 0; i < Rows.Length; i++)
            {
                float y = -pad - 54f - i * rowH;
                // Both columns centered on the row's line (their fonts sit differently).
                var key = UiKit.Text("Key", card, UiKit.Mono, 20f, UiKit.Accent, TextAlignmentOptions.MidlineLeft);
                Place(key.rectTransform, new Vector2(pad, y), new Vector2(190f, rowH));
                key.text = Rows[i].key;
                var what = UiKit.Text("What", card, UiKit.Display, 23f, UiKit.Ink, TextAlignmentOptions.MidlineLeft);
                Place(what.rectTransform, new Vector2(pad + 200f, y), new Vector2(380f, rowH));
                what.text = Rows[i].what;
            }
            card.gameObject.SetActive(false);

            prompt = UiKit.Rect("Prompt", root, new Vector2(0.5f, 0.32f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 50f));
            var pbg = UiKit.Box("Panel", prompt, UiKit.Panel);
            UiKit.Fill(pbg.rectTransform);
            var ptext = UiKit.Text("Text", prompt, UiKit.Display, 24f, UiKit.Ink, TextAlignmentOptions.Center);
            UiKit.Fill(ptext.rectTransform);
            ptext.text = "CLICK TO PLAY  <color=#8EA0A0>·</color>  <color=#8EA5FF>F1</color> CONTROLS";
            ptext.characterSpacing = 3f;
            prompt.gameObject.SetActive(false);
        }

        static void Place(RectTransform r, Vector2 topLeft, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = topLeft;
            r.sizeDelta = size;
        }

        void LateUpdate()
        {
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            if (p == null)
                return;
            bool selfCheck = p.Autoplay || p.Autofire || p.Autoaim || p.Autotarget;
            Show(card, p.Help);
            Show(prompt, !p.Grabbed && !p.Help && !selfCheck);
        }

        static void Show(RectTransform r, bool on)
        {
            if (r.gameObject.activeSelf != on)
                r.gameObject.SetActive(on);
        }
    }
}
