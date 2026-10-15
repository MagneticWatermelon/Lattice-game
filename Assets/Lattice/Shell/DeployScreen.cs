using System;
using System.Collections.Generic;
using Lattice.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.Shell
{
    /// <summary>
    /// The deploy screen (GDD §9.2, §17; U4): the continent map, and beside it
    /// where we may deploy (our faction's spawns, each with its lattice
    /// transit and what's going on there; the fights where we're needed
    /// first), the loadout, and the deploy button, which counts the transit
    /// down once pressed. A spawn lost in transit says so, and we pick
    /// another. Shown with no body (joining, or released from our corpse).
    /// </summary>
    public sealed class DeployScreen : MonoBehaviour
    {
        const int Rows = 8;
        const float Column = 760f, RowH = 62f;

        public MapView Map { get; private set; }
        /// <summary>Self-checks: deploy at the top spawn a second after the screen opens.</summary>
        public bool Auto;
        /// <summary>The class to deploy as (U6: a kit, <see cref="Weapons"/>), remembered between games.</summary>
        public int Kit;
        const string KitPref = "Lattice.Kit";

        RectTransform column;
        TextMeshProUGUI side, xp, status, deployText;
        RecapCard recap;
        RectTransform listTop;
        readonly Row[] rows = new Row[Rows];
        Clickable deploy;
        Image deployFill;
        RectTransform loadout;
        readonly Clickable[] kits = new Clickable[5];
        readonly Image[] kitBars = new Image[5];
        TextMeshProUGUI kitAbout;
        int shownKit = -1;
        readonly List<Option> options = new List<Option>();
        int selected = -1;
        /// <summary>Rows shown: fewer under a recap, so the loadout and the button fit.</summary>
        int rowsShown = Rows;
        float openedAt, refreshAt;
        long listKey = -1;
        uint shownRefusals;
        string refusal = "";
        LtSite[] sites;
        string[] names;
        List<int>[] adjacent;
        float deployTotal;
        int shownFaction = -1, shownTenths = -1, shownSelected = -2;
        uint shownXp = uint.MaxValue;
        string shownStatus;

        struct Option
        {
            public int Site;
            public float Transit;
            public int Here, Ours;
            public bool Front, Attacked, Recommended;
            public float Score;
        }

        struct Row
        {
            public Clickable Hit;
            public Image Bar;
            public TextMeshProUGUI Name, About, Time, Tag;
            // What it says, written into reused buffers only when it changes.
            public CharBuf AboutBuf, TimeBuf;
            public long Key;
        }

        // The texts that change while the screen is open, built without allocating.
        readonly CharBuf xpBuf = new CharBuf(24), deployBuf = new CharBuf(96);

        public static DeployScreen Create(Transform parent)
        {
            var root = UiKit.Rect("Deploy", parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiKit.Fill(root);
            var d = root.gameObject.AddComponent<DeployScreen>();
            d.Build(root);
            return d;
        }

        void Build(RectTransform root)
        {
            var back = UiKit.Box("Backdrop", root, new Color(0.02f, 0.026f, 0.03f, 0.9f));
            back.raycastTarget = true;
            UiKit.Fill(back.rectTransform);
            // The map fills the left: a square as tall as the screen allows.
            Map = MapView.Create(root, 40f, 40f, 1000f);
            Map.Clicked += Pick;
            Map.Lit = i => i < NetDriver.Frame.SpawnCount && NetDriver.Frame.Spawns[i].ok != 0;
            Map.Extra = Describe;
            column = UiKit.Rect("Column", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(Column, 1000f));
            var title = Widgets.Label(column, "DEPLOY", 0f, 0f, 400f, 56f, 52f);
            title.characterSpacing = 10f;
            side = Widgets.Label(column, "", 0f, 58f, 520f, 26f, 20f);
            side.characterSpacing = 5f;
            xp = Widgets.Label(column, "", Column - 240f, 14f, 240f, 30f, 20f, true, UiKit.Muted, TextAlignmentOptions.MidlineRight);
            recap = RecapCard.Create(column, 0f, 104f, Column);
            listTop = Widgets.At(column, "Spawns", 0f, 104f, Column, 30f + Rows * RowH);
            Widgets.Heading(listTop, "WHERE TO", 0f, 0f, Column);
            for (int i = 0; i < Rows; i++)
                rows[i] = MakeRow(i, 38f + i * RowH);
            loadout = Widgets.At(column, "Loadout", 0f, 0f, Column, 110f);
            Widgets.Heading(loadout, "CLASS", 0f, 0f, Column);
            // A tile a class (GDD §4.3): its name and its weapon; the chosen one's numbers below.
            Kit = Mathf.Clamp(PlayerPrefs.GetInt(KitPref, 0), 0, Weapons.Count - 1);
            float tile = (Column - 4 * 8f) / 5f;
            for (int k = 0; k < kits.Length && k < Weapons.Count; k++)
            {
                int kit = k;
                var c = Widgets.Clickable(loadout, $"Class {k}", k * (tile + 8f), 36f, tile, 50f, Widgets.Row, Widgets.RowHover, () => PickKit(kit));
                kitBars[k] = Widgets.Box(c.transform, UiKit.Accent, 0f, 46f, tile, 4f);
                Widgets.Label(c.transform, Weapons.Class(k), 10f, 4f, tile - 20f, 26f, 21f);
                Widgets.Label(c.transform, Weapons.Name(k).ToLowerInvariant(), 10f, 26f, tile - 20f, 18f, 13f, true, UiKit.Muted);
                kits[k] = c;
            }
            kitAbout = Widgets.Label(loadout, "", 0f, 92f, Column, 22f, 15f, true, UiKit.Muted);
            deploy = Widgets.Button(column, "DEPLOY", 0f, 1000f - 84f, Column, 84f, Deploy, true, UiKit.Accent, 30f);
            deployText = deploy.Text;
            deployText.textWrappingMode = TextWrappingModes.NoWrap;
            UiKit.Warm(deployText, 64);
            deployFill = UiKit.Box("Transit", deploy.transform, new Color(1f, 1f, 1f, 0.28f));
            var fr = deployFill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(0f, 1f);
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            deployFill.transform.SetSiblingIndex(0);
            status = Widgets.Label(column, "", 0f, 1000f - 120f, Column, 28f, 18f, false, UiKit.Warn);
        }

        Row MakeRow(int i, float y)
        {
            int k = i;
            var hit = Widgets.Clickable(listTop, $"Row {i}", 0f, y, Column, RowH - 6f, Widgets.Row, Widgets.RowHover, () => PickRow(k));
            var r = new Row { Hit = hit, AboutBuf = new CharBuf(96), TimeBuf = new CharBuf(12), Key = -1 };
            r.Bar = Widgets.Box(hit.transform, UiKit.Accent, 0f, 0f, 4f, RowH - 6f);
            r.Name = Widgets.Label(hit.transform, "", 18f, 6f, 470f, 28f, 24f);
            r.About = Widgets.Label(hit.transform, "", 18f, 33f, 560f, 20f, 14f, true, UiKit.Muted);
            r.Tag = Widgets.Label(hit.transform, "", 420f, 9f, 200f, 20f, 13f, false, UiKit.Accent, TextAlignmentOptions.MidlineRight);
            r.Tag.characterSpacing = 6f;
            r.Time = Widgets.Label(hit.transform, "", Column - 140f, 0f, 124f, RowH - 6f, 26f, true, UiKit.Ink, TextAlignmentOptions.MidlineRight);
            UiKit.Warm(r.About, 80);
            return r;
        }

        void OnEnable()
        {
            openedAt = Time.unscaledTime;
            listKey = -1;
            refreshAt = 0f;
        }

        void Bind()
        {
            var w = NetDriver.World;
            if (w == null)
                return;
            sites = w.Sites();
            names = Names.Sites(sites);
            adjacent = new List<int>[sites.Length];
            for (int i = 0; i < sites.Length; i++)
                adjacent[i] = new List<int>();
            foreach (var l in w.Lanes())
                if (l.a != l.b && l.a < sites.Length && l.b < sites.Length && !adjacent[l.a].Contains(l.b))
                {
                    adjacent[l.a].Add(l.b);
                    adjacent[l.b].Add(l.a);
                }
        }

        /// <summary>The map card's line about deploying at site <paramref name="i"/>.</summary>
        void Describe(int i, CharBuf to)
        {
            var f = NetDriver.Frame;
            if (i >= f.SpawnCount)
                return;
            var o = f.Spawns[i];
            if (o.ok != 0)
                to.Append("<color=#8EA5FF>deploy here: ").Append(Mathf.RoundToInt(o.transitSecs)).Append(" s</color>");
            else if (o.reason == LtCode.RefusedContested)
                to.Append("<color=#E3824F>no spawn: past halfway to its attacker</color>");
            else if (o.reason == LtCode.RefusedNoSpawn)
                to.Append("an outpost: no spawn room");
        }

        void PickKit(int kit)
        {
            Kit = kit;
            PlayerPrefs.SetInt(KitPref, kit);
        }

        /// <summary>The classes' tiles, the chosen one lit; its weapon in numbers.</summary>
        void Kits(Color ink)
        {
            for (int k = 0; k < kits.Length && kits[k] != null; k++)
            {
                bool on = k == Kit;
                kits[k].Lit = on;
                kits[k].SetColors(on ? new Color(ink.r, ink.g, ink.b, 0.14f) : Widgets.Row, on ? new Color(ink.r, ink.g, ink.b, 0.2f) : Widgets.RowHover);
                kitBars[k].color = on ? ink : new Color(1f, 1f, 1f, 0.06f);
            }
            if (Kit == shownKit)
                return;
            shownKit = Kit;
            ref readonly var w = ref Weapons.Of(Kit);
            unsafe
            {
                string dmg = w.damage[0] == w.farDamage[0] ? $"{w.damage[0]}" : $"{w.damage[0]}-{w.farDamage[0]}";
                string pellets = w.pellets > 1 ? $"{w.pellets} x " : "";
                kitAbout.text = $"{Weapons.Roles[Kit]}  ·  {pellets}{dmg} a hit, {1f / w.intervalSecs:0.#}/s  ·  {w.magazine} + {w.reserve}  ·  {w.speed:0} m/s";
            }
        }

        void Pick(int site)
        {
            var f = NetDriver.Frame;
            if (site < f.SpawnCount && f.Spawns[site].ok != 0)
            {
                selected = site;
                listKey = -1;
            }
        }

        void PickRow(int row)
        {
            if (row < options.Count)
            {
                selected = options[row].Site;
                listKey = -1;
            }
        }

        void Deploy()
        {
            if (selected < 0)
                return;
            refusal = "";
            if (NetDriver.Deploy(selected, Kit))
                deployTotal = NetDriver.Frame.Spawns[selected].transitSecs;
        }

        void Update()
        {
            var f = NetDriver.Frame;
            if (sites == null)
            {
                Bind();
                if (sites == null)
                    return;
            }
            byte us = f.Own.faction;
            var ink = Names.Ink(us);
            if (us != shownFaction)
            {
                shownFaction = us;
                side.text = $"<color={Names.Hex(us)}>{Names.Faction(us)}</color>  ·  KILN";
            }
            if (f.Shell.xp != shownXp)
            {
                shownXp = f.Shell.xp;
                xpBuf.Clear().Append("XP ").Append(f.Shell.xp);
                xp.SetCharArray(xpBuf.Chars, 0, xpBuf.Length);
            }
            bool died = f.Recaps > 0 && f.Shell.diedIn != LtCode.NoSite;
            if (recap.gameObject.activeSelf != died)
                recap.gameObject.SetActive(died);
            float listY = died ? 104f + RecapCard.Height + 24f : 104f;
            int shown = died ? 5 : Rows;
            if (shown != rowsShown)
                (rowsShown, listKey) = (shown, -1);
            listTop.anchoredPosition = new Vector2(0f, -listY);
            loadout.anchoredPosition = new Vector2(0f, -(listY + 38f + rowsShown * RowH + 18f));
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + 0.5f;
                Options(f, us);
            }
            Rows_(f, ink);
            Kits(ink);
            Button(f, ink);
            Map.Selected = selected;
            if (f.Shell.refusals != shownRefusals)
            {
                shownRefusals = f.Shell.refusals;
                int site = f.Shell.refusedSite;
                string where = site < names.Length ? names[site] : "there";
                refusal = f.Shell.refusedReason switch
                {
                    LtCode.RefusedNotHeld => $"{where} isn't ours any more: pick another spawn",
                    LtCode.RefusedContested => $"{where} is past halfway to its attacker: pick another spawn",
                    LtCode.RefusedNoSpawn => $"{where} has no spawn room",
                    LtCode.RefusedAlive => "you're alive: no need",
                    _ => "the server won't deploy us there",
                };
                if (selected == site)
                    selected = -1;
            }
            if (!ReferenceEquals(refusal, shownStatus))
            {
                shownStatus = refusal;
                status.text = refusal;
            }
            if (Auto && f.Shell.deploying == 0 && Time.unscaledTime - openedAt > 1f && selected >= 0 && Time.unscaledTime - lastAuto > 3f)
            {
                lastAuto = Time.unscaledTime;
                Deploy();
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) && f.Shell.deploying == 0)
                Deploy();
        }

        float lastAuto = -10f;

        /// <summary>Our spawns, the fights where we're needed first: a fight
        /// there (outnumbered most), then the fronts, then the nearest by transit.</summary>
        unsafe void Options(NetFrame f, byte us)
        {
            options.Clear();
            for (int i = 0; i < f.SpawnCount && i < sites.Length; i++)
            {
                var s = f.Spawns[i];
                if (s.ok == 0)
                    continue;
                var o = new Option { Site = i, Transit = s.transitSecs };
                o.Here = Map.PlayersAt(i, out o.Ours);
                foreach (var j in adjacent[i])
                    if (j < f.SiteCount && f.Sites[j].owner != us)
                        o.Front = true;
                var st = i < f.SiteCount ? f.Sites[i] : default;
                o.Attacked = st.attacker < 3 && st.attacker != us && st.meter > 0.001f;
                int enemies = o.Here - o.Ours;
                // A fight to join: its enemies, more if they outnumber us there.
                float fight = enemies > 0 ? Mathf.Sqrt(enemies) * (enemies > o.Ours ? 1.5f : 1f) : 0f;
                o.Score = fight * 4f + (o.Attacked ? 6f : 0f) + (o.Front ? 3f : 0f) - o.Transit * 0.15f;
                options.Add(o);
            }
            options.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (int k = 0; k < options.Count && k < 2; k++)
            {
                var o = options[k];
                if (o.Here - o.Ours > 0 || o.Attacked)
                {
                    o.Recommended = true;
                    options[k] = o;
                }
            }
            bool still = false;
            foreach (var o in options)
                still |= o.Site == selected;
            if (!still)
                selected = options.Count > 0 ? options[0].Site : -1;
            listKey = -1;
        }

        void Rows_(NetFrame f, Color ink)
        {
            long key = selected * 7919L + options.Count;
            if (key == listKey)
                return;
            listKey = key;
            for (int i = 0; i < Rows; i++)
            {
                ref var r = ref rows[i];
                bool on = i < options.Count && i < rowsShown;
                if (r.Hit.gameObject.activeSelf != on)
                    r.Hit.gameObject.SetActive(on);
                if (!on)
                    continue;
                var o = options[i];
                var st = o.Site < f.SiteCount ? f.Sites[o.Site] : default;
                bool picked = o.Site == selected;
                int pct = o.Attacked ? Mathf.FloorToInt(st.meter * 100f) : -1;
                int kind = o.Attacked ? 0 : sites[o.Site].kind == LtCode.SiteLanding ? 1 : o.Front ? 2 : 3;
                int transit = Mathf.RoundToInt(o.Transit);
                // (Rewritten only when what it says changes.)
                long rowKey = o.Site | (long)(pct + 1) << 8 | (long)kind << 16 | (long)Mathf.Min(o.Here, 1023) << 18 | (long)Mathf.Min(o.Ours, 1023) << 28
                    | (long)Mathf.Min(transit, 255) << 38 | (o.Recommended ? 1L : 0L) << 46 | (picked ? 1L : 0L) << 47;
                if (rowKey == r.Key)
                    continue;
                r.Key = rowKey;
                r.Name.text = names[o.Site];
                r.AboutBuf.Clear();
                if (kind == 0)
                    r.AboutBuf.Append("<color=#E3824F>UNDER ATTACK ").Append(pct).Append("%</color>");
                else
                    r.AboutBuf.Append(kind == 1 ? "OUR LANDING" : kind == 2 ? "FRONT" : "BEHIND THE LINES");
                r.AboutBuf.Append("  ·  ");
                if (o.Here > 0)
                    r.AboutBuf.Append(o.Here).Append(" here, ").Append(o.Ours).Append(" ours");
                else
                    r.AboutBuf.Append("quiet");
                r.About.SetCharArray(r.AboutBuf.Chars, 0, r.AboutBuf.Length);
                r.TimeBuf.Clear().Append(transit).Append(" s");
                r.Time.SetCharArray(r.TimeBuf.Chars, 0, r.TimeBuf.Length);
                r.Tag.text = o.Recommended ? "RECOMMENDED" : "";
                r.Hit.Lit = picked;
                r.Bar.color = picked ? ink : new Color(ink.r, ink.g, ink.b, 0.25f);
                r.Hit.SetColors(picked ? new Color(ink.r, ink.g, ink.b, 0.12f) : Widgets.Row, picked ? new Color(ink.r, ink.g, ink.b, 0.18f) : Widgets.RowHover);
            }
        }

        void Button(NetFrame f, Color ink)
        {
            bool going = f.Shell.deploying != 0;
            deploy.Interactable = going || selected >= 0;
            deploy.SetColors(ink, Color.Lerp(ink, Color.white, 0.25f));
            // The text only when what it says changes (tenths of a second in transit).
            int tenths = going ? Mathf.CeilToInt(f.Shell.deploySecs * 10f) : -1;
            int what = going ? f.Shell.deploySite : selected;
            if (tenths != shownTenths || what != shownSelected)
            {
                (shownTenths, shownSelected) = (tenths, what);
                deployBuf.Clear();
                if (going)
                    deployBuf.Append("DEPLOYING  ·  ").Append(what < names.Length ? names[what] : "").Append("  ·  ").Append(tenths / 10).Append('.').Append(tenths % 10).Append(" s");
                else if (selected >= 0)
                    deployBuf.Append("DEPLOY  ·  ").Append(names[selected]).Append("  ·  ").Append(Mathf.RoundToInt(f.Spawns[selected].transitSecs)).Append(" s");
                else
                    deployBuf.Append("NO SPAWN");
                deployText.SetCharArray(deployBuf.Chars, 0, deployBuf.Length);
            }
            if (going)
            {
                // (Two at a time: Mathf.Max of three takes a params array, a new one each frame.)
                float total = Mathf.Max(Mathf.Max(deployTotal, f.Shell.deploySecs), 0.1f);
                deployFill.rectTransform.anchorMax = new Vector2(1f - Mathf.Clamp01(f.Shell.deploySecs / total), 1f);
            }
            else
                deployFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        }
    }
}
