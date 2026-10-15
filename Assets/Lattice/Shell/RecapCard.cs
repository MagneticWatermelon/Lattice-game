using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.Shell
{
    /// <summary>
    /// The death recap (GDD §4.4): who killed us, with what, from how far and
    /// with how much health left, and the damage we took in our last five
    /// seconds, a bar for each who dealt it. Rewritten only when a new recap
    /// comes.
    /// </summary>
    public sealed class RecapCard : MonoBehaviour
    {
        const int Lines = 4;
        public const float Height = 236f;

        TextMeshProUGUI title, killer, how;
        readonly TextMeshProUGUI[] who = new TextMeshProUGUI[Lines], what = new TextMeshProUGUI[Lines];
        readonly Image[] bars = new Image[Lines], tracks = new Image[Lines];
        Image accent;
        float width;
        int shown = -1;

        public static RecapCard Create(Transform parent, float x, float y, float w)
        {
            var r = Widgets.At(parent, "Recap", x, y, w, Height);
            var c = r.gameObject.AddComponent<RecapCard>();
            c.Build(w);
            return c;
        }

        void Build(float w)
        {
            width = w;
            var bg = UiKit.Box("Back", transform, Widgets.Card);
            UiKit.Fill(bg.rectTransform);
            accent = Widgets.Box(transform, UiKit.Warn, 0f, 0f, 4f, Height);
            title = Widgets.Label(transform, "KILLED BY", 22f, 14f, w - 44f, 22f, 16f, false, Widgets.Faint);
            title.characterSpacing = 8f;
            killer = Widgets.Label(transform, "", 22f, 36f, w - 44f, 44f, 36f);
            how = Widgets.Label(transform, "", 22f, 80f, w - 44f, 24f, 16f, true, UiKit.Muted);
            for (int i = 0; i < Lines; i++)
            {
                float y = 116f + i * 28f;
                who[i] = Widgets.Label(transform, "", 22f, y, 190f, 24f, 16f, true, UiKit.Ink);
                tracks[i] = Widgets.Box(transform, new Color(1f, 1f, 1f, 0.07f), 220f, y + 8f, w - 220f - 170f, 8f);
                bars[i] = Widgets.Box(transform, UiKit.Warn, 220f, y + 8f, 0f, 8f);
                what[i] = Widgets.Label(transform, "", w - 160f, y, 140f, 24f, 15f, true, UiKit.Muted, TextAlignmentOptions.MidlineRight);
            }
        }

        static int FactionOf(ushort id) => id % 3;

        void Update()
        {
            var f = NetDriver.Frame;
            if (f.Recaps == shown)
                return;
            shown = f.Recaps;
            Write(f.Recap, f.Own.entity);
        }

        unsafe void Write(LtRecap r, ushort me)
        {
            bool fall = r.cause == LtCode.CauseFall, field = r.cause == LtCode.CauseField;
            bool nobody = r.killer == LtCode.Nobody;
            if (fall)
            {
                title.text = "YOU FELL";
                killer.text = "<color=#C8CFCC>A FATAL DROP</color>";
                how.text = "a fall past 18 m kills; past 8 m it hurts";
                accent.color = Names.NoOneInk;
            }
            else if (field)
            {
                title.text = "YOU DIED";
                killer.text = "<color=#C8CFCC>A SPAWN ROOM'S FIELD</color>";
                how.text = "it hurts anyone whose side doesn't hold its site";
                accent.color = Names.NoOneInk;
            }
            else if (nobody)
            {
                title.text = "YOU DIED";
                killer.text = "<color=#C8CFCC>KILLED</color>";
                how.text = "";
                accent.color = Names.NoOneInk;
            }
            else
            {
                title.text = "KILLED BY";
                int fac = r.killerFaction < 3 ? r.killerFaction : FactionOf(r.killer);
                killer.text = $"<color={Names.Hex(fac)}>{Names.Faction(fac)}</color> <size=70%><color=#9AA4A6>#{r.killer}</color></size>{(r.head != 0 ? "  <size=55%><color=#FFA020>HEADSHOT</color></size>" : "")}";
                how.text = $"{Weapons.Name(r.cause).ToLowerInvariant()}  ·  {r.distance:F0} m  ·  {(r.killerHealth > 0 ? $"they had {r.killerHealth} health" : "they died too")}";
                accent.color = Names.Ink(fac);
            }
            int n = Mathf.Min(r.lines, Lines);
            for (int i = 0; i < Lines; i++)
            {
                bool on = i < n;
                who[i].gameObject.SetActive(on);
                what[i].gameObject.SetActive(on);
                bars[i].gameObject.SetActive(on);
                tracks[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                ushort from = r.from[i];
                int dmg = r.damage[i], hits = r.hits[i], heads = r.heads[i];
                if (from == LtCode.Nobody)
                {
                    who[i].text = fall ? "<color=#C8CFCC>the fall</color>" : field ? "<color=#C8CFCC>the field</color>" : "<color=#C8CFCC>no one</color>";
                    bars[i].color = Names.NoOneInk;
                }
                else
                {
                    int fac = FactionOf(from);
                    who[i].text = $"<color={Names.Hex(fac)}>#{from}</color>{(from == r.killer ? " <size=80%><color=#9AA4A6>killer</color></size>" : "")}";
                    bars[i].color = Names.Ink(fac);
                }
                float span = width - 220f - 170f;
                bars[i].rectTransform.sizeDelta = new Vector2(span * Mathf.Clamp01(dmg / 100f), 8f);
                what[i].text = from == LtCode.Nobody ? $"{dmg}" : $"{dmg}  ·  {hits} hit{(hits == 1 ? "" : "s")}{(heads > 0 ? $", {heads} head" : "")}";
            }
        }
    }
}
