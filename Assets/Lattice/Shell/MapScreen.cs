using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The map (M, GDD §9.2): the continent as the deploy screen shows it,
    /// opened on where we are, with its legend. Who holds what, the fronts,
    /// where the fighting is.
    /// </summary>
    public sealed class MapScreen : MonoBehaviour
    {
        public MapView Map { get; private set; }

        public static MapScreen Create(Transform parent)
        {
            var root = UiKit.Rect("Map screen", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Fill(root);
            var m = root.gameObject.AddComponent<MapScreen>();
            m.Build(root);
            return m;
        }

        void Build(RectTransform root)
        {
            var back = UiKit.Box("Backdrop", root, new Color(0.015f, 0.02f, 0.024f, 0.86f));
            back.raycastTarget = true;
            UiKit.Fill(back.rectTransform);
            var mid = UiKit.Rect("Middle", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 1000f));
            Map = MapView.Create(mid, 0f, 0f, 1000f);
            var title = Widgets.Label(mid, "MAP", 1040f, 0f, 400f, 56f, 52f);
            title.characterSpacing = 10f;
            Widgets.Label(mid, "KILN  ·  8 KM", 1040f, 58f, 600f, 26f, 20f, false, UiKit.Muted).characterSpacing = 5f;
            float y = 130f;
            Widgets.Heading(mid, "LEGEND", 1040f, y, 600f);
            y += 44f;
            for (int f = 0; f < 3; f++)
            {
                Widgets.Box(mid, Names.Ink(f), 1040f, y + 6f, 26f, 14f);
                Widgets.Label(mid, Names.Faction(f), 1080f, y, 400f, 26f, 20f);
                y += 34f;
            }
            Widgets.Box(mid, Names.NoOneInk, 1040f, y + 6f, 26f, 14f);
            Widgets.Label(mid, "NO ONE", 1080f, y, 400f, 26f, 20f);
            y += 50f;
            float x = 1040f;
            foreach (var (rank, label) in new[] { (Names.Rank.Landing, "LANDING"), (Names.Rank.Great, "GREAT SITE"), (Names.Rank.Base, "BASE"), (Names.Rank.Outpost, "OUTPOST") })
            {
                var mark = Widgets.Box(mid, UiKit.Ink, x, y + 3f, 20f, 20f);
                mark.sprite = MapView.Shape(rank);
                var t = Widgets.Label(mid, label, x + 28f, y, 140f, 26f, 18f, false, UiKit.Muted);
                x += 28f + t.GetPreferredValues(label).x + 26f;
            }
            y += 40f;
            Widgets.Label(mid, "A link in two colors is a front: its sites' holders meet there.", 1040f, y, 600f, 24f, 17f, false, UiKit.Muted);
            y += 30f;
            Widgets.Label(mid, "A ring round a site is its meter, in its attacker's color.", 1040f, y, 600f, 24f, 17f, false, UiKit.Muted);
            y += 30f;
            Widgets.Label(mid, "<color=#FF8C33>Orange rings</color>: fighting, larger the more there are.", 1040f, y, 600f, 24f, 17f, false, UiKit.Muted);
            y += 30f;
            Widgets.Label(mid, "The arrow is you. Wheel: zoom.  Drag: move.", 1040f, y, 600f, 24f, 17f, false, UiKit.Muted);
            Widgets.Label(mid, "M  OR  ESC  TO CLOSE", 1040f, 1000f - 30f, 600f, 26f, 18f, false, Widgets.Faint).characterSpacing = 6f;
        }

        void OnEnable()
        {
            if (Map == null)
                return;
            var f = NetDriver.Frame;
            if (f.Welcomed)
                Map.LookAt(f.Own.pos.x, f.Own.pos.y, 2.5f);
        }
    }
}
