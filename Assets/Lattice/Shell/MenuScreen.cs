using System;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The menu (Esc): back to the game, the map, the settings, the controls,
    /// quit. And the settings (GDD §9.2): the mouse, the view, the sound, the
    /// colors, each applied as it changes and kept between games.
    /// </summary>
    public sealed class MenuScreen : MonoBehaviour
    {
        public Action Resume, OpenMap, Controls, Quit;
        /// <summary>A self-check set these from its command line: the settings leave them alone.</summary>
        public bool KeepVSync, KeepScale, KeepVolume;

        RectTransform menu, settings;
        TextMeshProUGUI where;
        SwitchRow iff, graph;
        public bool InSettings => settings.gameObject.activeSelf;

        public static MenuScreen Create(Transform parent)
        {
            var root = UiKit.Rect("Menu", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Fill(root);
            var m = root.gameObject.AddComponent<MenuScreen>();
            m.Build(root);
            return m;
        }

        void Build(RectTransform root)
        {
            var back = UiKit.Box("Backdrop", root, new Color(0.015f, 0.02f, 0.024f, 0.78f));
            back.raycastTarget = true;
            UiKit.Fill(back.rectTransform);

            menu = UiKit.Rect("Main", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 560f));
            var title = Widgets.Label(menu, "LATTICE", 0f, 0f, 520f, 80f, 64f, false, UiKit.Ink, TextAlignmentOptions.Center);
            title.characterSpacing = 26f;
            where = Widgets.Label(menu, "", 0f, 84f, 520f, 26f, 16f, true, UiKit.Muted, TextAlignmentOptions.Center);
            float y = 140f;
            Widgets.Button(menu, "RESUME", 60f, y, 400f, 58f, () => Resume?.Invoke(), true);
            Widgets.Button(menu, "MAP", 60f, y += 74f, 400f, 58f, () => OpenMap?.Invoke());
            Widgets.Button(menu, "SETTINGS", 60f, y += 74f, 400f, 58f, () => Show(true));
            Widgets.Button(menu, "CONTROLS", 60f, y += 74f, 400f, 58f, () => Controls?.Invoke());
            Widgets.Button(menu, "QUIT", 60f, y += 74f, 400f, 58f, () => Quit?.Invoke());

            settings = UiKit.Rect("Settings", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 780f));
            var sb = UiKit.Box("Back", settings, Widgets.Sheet);
            UiKit.Fill(sb.rectTransform);
            var st = Widgets.Label(settings, "SETTINGS", 48f, 36f, 600f, 56f, 46f);
            st.characterSpacing = 10f;
            const float col = 500f, left = 48f, right = 632f;
            float ly = 120f, ry = 120f;
            Widgets.Heading(settings, "MOUSE", left, ly, col);
            Widgets.Slider(settings, "Sensitivity", left, ly += 44f, col, 0.2f, 3f, Settings.Sensitivity, v => $"{v:F2}×", v => Set(() => Settings.Sensitivity = v), 0.05f);
            Widgets.Slider(settings, "Down the sights", left, ly += 76f, col, 0.3f, 1.5f, Settings.AdsSensitivity, v => $"{v:F2}×", v => Set(() => Settings.AdsSensitivity = v), 0.05f);
            Widgets.Heading(settings, "VIEW", left, ly += 100f, col);
            Widgets.Slider(settings, "Field of view", left, ly += 44f, col, 70f, 110f, Settings.Fov, v => $"{v:F0}°", v => Set(() => Settings.Fov = v), 1f);
            Widgets.Slider(settings, "Render scale", left, ly += 76f, col, 0.5f, 1f, Settings.RenderScale, v => $"{v * 100f:F0}%", v => Set(() => Settings.RenderScale = v), 0.05f);
            Widgets.Switch(settings, "Vertical sync", left, ly += 80f, col, Settings.VSync, v => Set(() => Settings.VSync = v), "off: frames as fast as they come");

            Widgets.Heading(settings, "SOUND", right, ry, col);
            Widgets.Slider(settings, "Volume", right, ry += 44f, col, 0f, 1f, Settings.Volume, v => $"{v * 100f:F0}%", v => Set(() => Settings.Volume = v), 0.01f);
            Widgets.Heading(settings, "DISPLAY", right, ry += 100f, col);
            iff = Widgets.Switch(settings, "Friend or foe colors", right, ry += 44f, col, true, v =>
            {
                var p = Lattice.LocalPlayer.LocalPlayer.Instance;
                if (p != null)
                    p.Iff = v;
                PlayerPrefs.SetInt(Lattice.LocalPlayer.LocalPlayer.IffPref, v ? 1 : 0);
            }, "off: the factions' own colors (I)");
            graph = Widgets.Switch(settings, "Net graph", right, ry += 64f, col, false, v =>
            {
                var p = Lattice.LocalPlayer.LocalPlayer.Instance;
                if (p != null)
                    p.NetGraph = v;
                PlayerPrefs.SetInt(Lattice.LocalPlayer.LocalPlayer.NetGraphPref, v ? 1 : 0);
            }, "the connection, frame times, what's drawn how (N)");
            Widgets.Button(settings, "BACK", 1180f - 48f - 240f, 780f - 48f - 58f, 240f, 58f, () => Show(false), true);
            settings.gameObject.SetActive(false);
        }

        void Set(Action change)
        {
            change();
            Settings.Apply(KeepVSync, KeepScale, KeepVolume);
            Settings.Save();
        }

        public void Show(bool inSettings)
        {
            menu.gameObject.SetActive(!inSettings);
            settings.gameObject.SetActive(inSettings);
            if (inSettings)
            {
                var p = Lattice.LocalPlayer.LocalPlayer.Instance;
                if (p != null)
                {
                    iff.Set(p.Iff);
                    graph.Set(p.NetGraph);
                }
            }
        }

        void OnEnable()
        {
            if (where != null)
                where.text = NetDriver.Running ? $"KILN  ·  {NetDriver.Address}" : "";
        }
    }
}
