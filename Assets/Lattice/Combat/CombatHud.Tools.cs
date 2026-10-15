using Lattice.Diagnostics;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.Players;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.Combat
{
    /// <summary>
    /// The HUD's gadgets (U6): the class's two tools over the ability (their
    /// keys, what they are, how many are left; C4 lying out turns its key into
    /// a detonator); the use key's work under the reticle (what it's doing
    /// and, for a revive or a hack, how far it has got), or a prompt when
    /// there's something to use it on; and a mark over each enemy our side's
    /// recon darts spot.
    /// </summary>
    public sealed partial class CombatHud
    {
        RectTransform toolsPanel;
        readonly TextMeshProUGUI[] toolTexts = new TextMeshProUGUI[2];
        readonly CharBuf[] toolBufs = { new CharBuf(128), new CharBuf(128) };
        readonly int[] shownTool = { -1, -1 };
        Image toolsAccent;

        RectTransform usePanel;
        TextMeshProUGUI useText;
        Image useFill;
        readonly CharBuf useBuf = new CharBuf(96);
        int shownUse = -1;

        const int SpotMarks = 32;
        readonly RectTransform[] spotMarks = new RectTransform[SpotMarks];
        readonly Image[] spotImages = new Image[SpotMarks];
        readonly int[] entityAt = new int[65536];
        readonly int[] entityStamp = new int[65536];
        int stamp;

        static readonly Color Spotted = new Color(1f, 0.32f, 0.2f, 0.95f);

        void BuildTools()
        {
            toolsPanel = UiKit.Rect("Tools", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 150f), new Vector2(300f, 34f));
            var bg = UiKit.Box("Panel", toolsPanel, UiKit.Panel);
            UiKit.Fill(bg.rectTransform);
            toolsAccent = UiKit.Box("Faction", toolsPanel, SoldierColors.Ink(0));
            var ar = toolsAccent.rectTransform;
            ar.anchorMin = new Vector2(1f, 0f);
            ar.anchorMax = new Vector2(1f, 1f);
            ar.pivot = new Vector2(1f, 0.5f);
            ar.sizeDelta = new Vector2(4f, 0f);
            ar.anchoredPosition = Vector2.zero;
            for (int k = 0; k < 2; k++)
            {
                var t = UiKit.Text($"Tool {k}", toolsPanel, UiKit.Display, 17f, UiKit.Ink, TextAlignmentOptions.Left);
                t.characterSpacing = 3f;
                t.richText = true;
                UiKit.Warm(t, 128);
                var r = t.rectTransform;
                r.anchorMin = new Vector2(k * 0.5f, 0f);
                r.anchorMax = new Vector2(k * 0.5f + 0.5f, 1f);
                r.offsetMin = new Vector2(14f, 0f);
                r.offsetMax = new Vector2(-6f, 0f);
                t.verticalAlignment = VerticalAlignmentOptions.Middle;
                toolTexts[k] = t;
            }
            toolsPanel.gameObject.SetActive(false);

            usePanel = UiKit.Rect("Use", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(240f, 40f));
            useText = UiKit.Text("Label", usePanel, UiKit.Display, 20f, UiKit.Ink, TextAlignmentOptions.Center);
            useText.characterSpacing = 6f;
            useText.richText = true;
            UiKit.Warm(useText, 96);
            var ur = useText.rectTransform;
            ur.anchorMin = new Vector2(0f, 1f);
            ur.anchorMax = new Vector2(1f, 1f);
            ur.pivot = new Vector2(0.5f, 1f);
            ur.anchoredPosition = Vector2.zero;
            ur.sizeDelta = new Vector2(0f, 26f);
            var track = UiKit.Box("Track", usePanel, new Color(0f, 0f, 0f, 0.45f));
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.offsetMin = new Vector2(10f, 2f);
            tr.offsetMax = new Vector2(-10f, 8f);
            useFill = UiKit.Box("Fill", tr, UiKit.Good);
            var fr = useFill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = fr.offsetMax = Vector2.zero;
            usePanel.gameObject.SetActive(false);

            for (int i = 0; i < SpotMarks; i++)
            {
                var m = UiKit.Rect($"Spotted {i}", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
                m.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var shadow = UiKit.Box("Shadow", m, new Color(0f, 0f, 0f, 0.55f));
                UiKit.Fill(shadow.rectTransform, -2f);
                var img = UiKit.Box("Mark", m, Spotted);
                UiKit.Fill(img.rectTransform);
                spotMarks[i] = m;
                spotImages[i] = img;
                m.gameObject.SetActive(false);
            }
        }

        void Tools(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, bool shown, bool dead, float now)
        {
            var o = f.Own;
            bool on = shown && !dead && f.Welcomed;
            int kinds = 0;
            unsafe
            {
                kinds = o.toolKinds[0] | (o.toolKinds[1] << 8);
            }
            bool any = on && kinds != 0;
            if (toolsPanel.gameObject.activeSelf != any)
                toolsPanel.gameObject.SetActive(any);
            if (any)
            {
                toolsAccent.color = SoldierColors.Ink(o.faction);
                bool c4Out = false;
                for (int i = 0; i < f.GadgetCount; i++)
                {
                    var g = f.Gadgets[i];
                    if (g.kind == LtCode.GadgetC4 && g.ours != 0 && g.resting != 0)
                        c4Out = true;
                }
                for (int k = 0; k < 2; k++)
                {
                    int kind, left;
                    unsafe
                    {
                        kind = o.toolKinds[k];
                        left = o.tools[k];
                    }
                    bool detonate = kind == LtCode.GadgetC4 && c4Out;
                    int key = kind | (left << 8) | (detonate ? 1 << 16 : 0);
                    if (key == shownTool[k])
                        continue;
                    shownTool[k] = key;
                    var b = toolBufs[k].Clear();
                    if (kind != LtCode.GadgetNone)
                    {
                        b.Append("<color=#8E9A99><size=80%>").Append(k == 0 ? 'G' : 'X').Append("</size></color>  ");
                        if (detonate)
                            b.Append("<color=#FF6A4A>DETONATE</color>");
                        else
                            b.Append(Weapons.Gadget(kind)).Append("  ").Append(left > 0 ? "<color=#DCE3E1>" : "<color=#6B7372>").Append(left).Append("</color>");
                    }
                    toolTexts[k].SetCharArray(b.Chars, 0, b.Length);
                    toolTexts[k].color = left > 0 || detonate ? UiKit.Ink : UiKit.Muted;
                }
            }

            Use(f, p, on, now);
            Spots(f, p, shown);
        }

        /// <summary>The use key: its work under way, or a prompt for what's in reach.</summary>
        void Use(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, bool on, float now)
        {
            var o = f.Own;
            int u = on ? o.@using : LtCode.UsingNone;
            int prompt = on && u == LtCode.UsingNone ? Prompt(f) : LtCode.UsingNone;
            int show = u != LtCode.UsingNone ? u : prompt != LtCode.UsingNone ? prompt + 8 : -1;
            bool visible = show >= 0;
            if (usePanel.gameObject.activeSelf != visible)
                usePanel.gameObject.SetActive(visible);
            if (!visible)
            {
                shownUse = -1;
                return;
            }
            if (show != shownUse)
            {
                shownUse = show;
                var b = useBuf.Clear();
                if (show >= 8)
                    b.Append("<color=#8E9A99>HOLD</color> E  ").Append(UseName(prompt, false));
                else
                    b.Append(UseName(u, true));
                useText.SetCharArray(b.Chars, 0, b.Length);
            }
            bool progress = u == LtCode.UsingRevive || u == LtCode.UsingHack;
            float fill = progress ? o.useProgress : u != LtCode.UsingNone ? 0.55f + 0.45f * Mathf.Sin(now * 6f) : 0f;
            useFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
            useFill.color = u == LtCode.UsingHack ? UiKit.Accent : UiKit.Good;
            useFill.transform.parent.gameObject.SetActive(u != LtCode.UsingNone);
        }

        static string UseName(int u, bool doing) => u switch
        {
            LtCode.UsingHeal => doing ? "HEALING" : "HEAL",
            LtCode.UsingRevive => doing ? "REVIVING" : "REVIVE",
            LtCode.UsingRepair => doing ? "REPAIRING" : "REPAIR",
            LtCode.UsingHack => doing ? "HACKING" : "HACK",
            _ => "",
        };

        /// <summary>What the use key would do now, if there's something for it in reach
        /// (as the server will judge it: within its range, in front of us).</summary>
        int Prompt(NetFrame f)
        {
            var o = f.Own;
            int kit = o.kit;
            if (kit != LtCode.ClassMedic && kit != LtCode.ClassEngineer && kit != LtCode.ClassRecon)
                return LtCode.UsingNone;
            var me = new Vector3(o.pos.x, o.pos.y, o.pos.z);
            float reach = (LtConst.UseRange + 0.5f) * (LtConst.UseRange + 0.5f);
            int best = LtCode.UsingNone;
            if (kit == LtCode.ClassMedic || kit == LtCode.ClassEngineer)
                for (int i = 0; i < f.EntityCount; i++)
                {
                    var e = f.Entities[i];
                    if (e.faction != o.faction || e.id == o.entity)
                        continue;
                    float dx = e.pos.x - me.x, dy = e.pos.y - me.y, dz = e.pos.z - me.z;
                    if (dx * dx + dy * dy + dz * dz > reach)
                        continue;
                    bool fallen = (e.flags & LtCode.FlagDead) != 0;
                    if (kit == LtCode.ClassMedic && fallen)
                        return LtCode.UsingRevive;
                    if (kit == LtCode.ClassMedic && !fallen && e.health < 100 && e.kit != LtCode.ClassExo)
                        best = LtCode.UsingHeal;
                    if (kit == LtCode.ClassEngineer && !fallen && e.kit == LtCode.ClassExo && e.health < 100)
                        best = LtCode.UsingRepair;
                }
            if (kit == LtCode.ClassEngineer || kit == LtCode.ClassRecon)
                for (int i = 0; i < f.GadgetCount; i++)
                {
                    var g = f.Gadgets[i];
                    if (g.kind != LtCode.GadgetTurret)
                        continue;
                    float dx = g.pos.x - me.x, dy = g.pos.y - me.y, dz = g.pos.z - me.z;
                    if (dx * dx + dy * dy + dz * dz > reach)
                        continue;
                    if (kit == LtCode.ClassRecon && g.faction != o.faction)
                        return LtCode.UsingHack;
                    if (kit == LtCode.ClassEngineer && g.faction == o.faction && g.health < LtCode.TurretHealth)
                        best = LtCode.UsingRepair;
                }
            return best;
        }

        /// <summary>A mark over each enemy our darts spot, where it's drawn.</summary>
        void Spots(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, bool shown)
        {
            int used = 0;
            var cam = Gun != null && Gun.Rig != null ? Gun.Rig.Camera : Camera.main;
            if (shown && cam != null && f.SpottedCount > 0)
            {
                stamp++;
                for (int i = 0; i < f.EntityCount; i++)
                {
                    var id = f.Entities[i].id;
                    entityAt[id] = i;
                    entityStamp[id] = stamp;
                }
                for (int s = 0; s < f.SpottedCount && used < SpotMarks; s++)
                {
                    int id = f.Spotted[s];
                    if (entityStamp[id] != stamp)
                        continue;
                    var e = f.Entities[entityAt[id]];
                    if ((e.flags & LtCode.FlagDead) != 0)
                        continue;
                    var world = Coords.ToUnity(e.pos.x, e.pos.y, e.pos.z + 2.15f);
                    var sp = cam.WorldToScreenPoint(world);
                    if (sp.z <= 0.1f)
                        continue;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out var local))
                        continue;
                    var m = spotMarks[used++];
                    if (!m.gameObject.activeSelf)
                        m.gameObject.SetActive(true);
                    m.anchoredPosition = local;
                    float size = Mathf.Lerp(13f, 8f, Mathf.InverseLerp(10f, 150f, sp.z));
                    m.sizeDelta = new Vector2(size, size);
                }
            }
            for (int i = used; i < SpotMarks; i++)
                if (spotMarks[i].gameObject.activeSelf)
                    spotMarks[i].gameObject.SetActive(false);
        }
    }
}
