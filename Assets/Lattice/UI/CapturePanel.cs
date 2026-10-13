using Lattice.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.UI
{
    /// <summary>
    /// The capture panel (GDD §9.3, U5): inside a site, at the top of the
    /// screen, its name and holder, its control meter filling toward whoever
    /// is taking it, a chip per point in its holder's color (flickering while
    /// contested), and a line on what's happening there (capturing, under
    /// attack, locked after a flip, not linked to anything we hold). And a
    /// banner when a site changes hands to or from us. Colors are the
    /// factions' (territory is about which faction, not friend or foe). Text
    /// rebuilt only when what it says changes, in reused buffers.
    /// </summary>
    public sealed class CapturePanel : MonoBehaviour
    {
        const int Chips = 6;
        const float Width = 440f, MeterWidth = 408f, BannerSecs = 4.5f;

        static readonly Color[] FactionInk = { new Color(1f, 0.72f, 0.25f), new Color(0.3f, 0.86f, 0.64f), new Color(0.96f, 0.4f, 0.78f) };
        static readonly string[] FactionName = { "ORDINATE", "FREE COMPACT", "RESONANCE" };
        static readonly Color NoOneInk = new Color(0.62f, 0.66f, 0.68f, 1f);

        RectTransform panel, meterFill, bannerBand;
        Image accent, meterTrack, meterImage, bannerBg;
        TextMeshProUGUI title, holder, status, banner;
        readonly Image[] chips = new Image[Chips];
        readonly TextMeshProUGUI[] chipText = new TextMeshProUGUI[Chips];
        readonly CharBuf titleBuf = new CharBuf(48), holderBuf = new CharBuf(32), statusBuf = new CharBuf(96), bannerBuf = new CharBuf(64);
        LtSite[] sites;
        byte[] owners;
        int shownSite = -2;
        long shownKey = -1;
        float bannerAt = -10f;

        public static CapturePanel Create(Transform canvas)
        {
            var go = new GameObject("Capture panel", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            UiKit.Fill((RectTransform)go.transform);
            var c = go.AddComponent<CapturePanel>();
            c.Build((RectTransform)go.transform);
            return c;
        }

        void Build(RectTransform root)
        {
            panel = UiKit.Rect("Panel", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(Width, 112f));
            var bg = UiKit.Box("Background", panel, UiKit.Panel);
            UiKit.Fill(bg.rectTransform);
            accent = UiKit.Box("Holder", panel, NoOneInk);
            var ar = accent.rectTransform;
            ar.anchorMin = new Vector2(0f, 1f);
            ar.anchorMax = new Vector2(1f, 1f);
            ar.pivot = new Vector2(0.5f, 1f);
            ar.sizeDelta = new Vector2(0f, 3f);
            title = Label("Site", UiKit.Display, 26f, UiKit.Ink, TextAlignmentOptions.TopLeft, new Vector2(16f, -9f), new Vector2(280f, 30f));
            holder = Label("Holder", UiKit.Display, 18f, NoOneInk, TextAlignmentOptions.TopRight, new Vector2(Width - 16f - 200f, -14f), new Vector2(200f, 24f));
            meterTrack = UiKit.Box("Meter track", panel, new Color(1f, 1f, 1f, 0.08f));
            Place(meterTrack.rectTransform, new Vector2(16f, -46f), new Vector2(MeterWidth, 8f));
            meterImage = UiKit.Box("Meter", panel, NoOneInk);
            meterFill = meterImage.rectTransform;
            Place(meterFill, new Vector2(16f, -46f), new Vector2(0f, 8f));
            for (int i = 0; i < Chips; i++)
            {
                chips[i] = UiKit.Box($"Point {i}", panel, NoOneInk);
                Place(chips[i].rectTransform, new Vector2(16f + i * 34f, -62f), new Vector2(28f, 22f));
                chipText[i] = UiKit.Text("Letter", chips[i].transform, UiKit.Mono, 15f, new Color(0.05f, 0.06f, 0.07f, 1f), TextAlignmentOptions.Center);
                UiKit.Fill(chipText[i].rectTransform);
                chipText[i].text = ((char)('A' + i)).ToString();
            }
            status = Label("Status", UiKit.Mono, 15f, UiKit.Muted, TextAlignmentOptions.TopLeft, new Vector2(16f, -90f), new Vector2(Width - 32f, 20f));
            UiKit.Warm(status, 96);
            // The banner: on a dark band (it's read over bright ground).
            bannerBand = UiKit.Rect("Banner", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(560f, 50f));
            bannerBg = UiKit.Box("Band", bannerBand, UiKit.Panel);
            UiKit.Fill(bannerBg.rectTransform);
            banner = UiKit.Text("Text", bannerBand, UiKit.Display, 34f, UiKit.Ink, TextAlignmentOptions.Center);
            UiKit.Fill(banner.rectTransform);
            UiKit.Warm(banner, 64);
            bannerBand.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
        }

        TextMeshProUGUI Label(string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 sz)
        {
            var t = UiKit.Text(name, panel, font, size, color, align);
            Place(t.rectTransform, pos, sz);
            return t;
        }

        static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        static Color Ink(byte faction) => faction < FactionInk.Length ? FactionInk[faction] : NoOneInk;

        static string KindName(int kind) => kind switch
        {
            LtCode.SiteBase => "BASE",
            LtCode.SiteOutpost => "OUTPOST",
            LtCode.SiteLanding => "LANDING",
            LtCode.SiteHollow => "THE HOLLOW",
            LtCode.SiteSpan => "THE SPAN",
            LtCode.SiteCliffhold => "CLIFFHOLD",
            LtCode.SiteGeode => "GEODE",
            LtCode.SiteMesaCitadel => "MESA CITADEL",
            LtCode.SiteSlotHold => "SLOT HOLD",
            LtCode.SiteRiftLedge => "RIFT LEDGE",
            LtCode.SiteCenote => "CENOTE",
            LtCode.SiteReefWarren => "REEF WARREN",
            LtCode.SiteOssuary => "OSSUARY",
            LtCode.SiteGlassVault => "GLASS VAULT",
            LtCode.SiteArch => "ARCH",
            LtCode.SiteVent => "VENT",
            LtCode.SiteTunnelMouth => "TUNNEL MOUTH",
            LtCode.SiteSpirePerch => "SPIRE PERCH",
            LtCode.SiteWellShrine => "WELL SHRINE",
            LtCode.SiteWreck => "WRECK",
            _ => "SITE",
        };

        /// <summary>The site we're in: the one whose center we're nearest, for its size.</summary>
        int Inside(in LtVec3 pos)
        {
            int best = -1;
            float bestR = float.MaxValue;
            for (int i = 0; i < sites.Length; i++)
            {
                float dx = pos.x - sites[i].x, dy = pos.y - sites[i].y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float reach = sites[i].half + 20f;
                if (d < reach && d / reach < bestR)
                    (best, bestR) = (i, d / reach);
            }
            return best;
        }

        unsafe void LateUpdate()
        {
            var f = NetDriver.Frame;
            if (f.SiteCount == 0 || !f.Welcomed)
            {
                Hide();
                return;
            }
            if (sites == null)
            {
                var w = NetDriver.World;
                if (w == null)
                    return;
                sites = w.Sites();
                owners = new byte[sites.Length];
                for (int i = 0; i < owners.Length && i < f.SiteCount; i++)
                    owners[i] = f.Sites[i].owner;
            }
            byte us = f.Own.faction;
            float now = Time.unscaledTime;
            // Sites changing hands to us or from us: the banner.
            for (int i = 0; i < owners.Length && i < f.SiteCount; i++)
            {
                byte o = f.Sites[i].owner;
                if (o == owners[i])
                    continue;
                byte was = owners[i];
                owners[i] = o;
                if (o != us && was != us)
                    continue;
                bannerBuf.Clear().Append(KindName(sites[i].kind)).Append(o == us ? " TAKEN" : " LOST");
                banner.SetCharArray(bannerBuf.Chars, 0, bannerBuf.Length);
                banner.color = o == us ? Ink(us) : UiKit.Warn;
                bannerAt = now;
            }
            float age = now - bannerAt;
            bannerBand.gameObject.SetActive(age < BannerSecs);
            if (age < BannerSecs)
            {
                float fade = Mathf.Clamp01((BannerSecs - age) / 1f);
                var c = banner.color;
                banner.color = new Color(c.r, c.g, c.b, fade);
                bannerBg.color = new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, UiKit.Panel.a * fade);
            }

            int site = Inside(f.Own.pos);
            if (site < 0 || site >= f.SiteCount)
            {
                Hide();
                return;
            }
            panel.gameObject.SetActive(true);
            var s = f.Sites[site];
            int pct = Mathf.FloorToInt(s.meter * 100f);
            int locked = Mathf.CeilToInt(s.lockedSecs);
            bool landing = sites[site].kind == LtCode.SiteLanding;
            // What the text says: rebuilt only when it changes.
            long key = (uint)site | (long)s.owner << 8 | (long)s.attacker << 16 | (long)pct << 24 | (long)Mathf.Min(locked, 4095) << 32
                | (long)(s.contested != 0 ? 1 : 0) << 44 | (long)(s.attackable >> us & 1) << 45 | (long)us << 46 | (long)(s.heldSecs < 5f ? 1 : 0) << 54;
            if (key != shownKey || site != shownSite)
            {
                shownKey = key;
                shownSite = site;
                titleBuf.Clear().Append(KindName(sites[site].kind));
                title.SetCharArray(titleBuf.Chars, 0, titleBuf.Length);
                holderBuf.Clear().Append(s.owner < FactionName.Length ? FactionName[s.owner] : "UNHELD");
                holder.SetCharArray(holderBuf.Chars, 0, holderBuf.Length);
                holder.color = Ink(s.owner);
                accent.color = Ink(s.owner);
                Status(s, us, pct, locked, landing);
                status.SetCharArray(statusBuf.Chars, 0, statusBuf.Length);
            }
            meterFill.sizeDelta = new Vector2(MeterWidth * Mathf.Clamp01(s.meter), 8f);
            meterImage.color = Ink(s.attacker);
            float flick = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(now * 2.5f * 2f * Mathf.PI));
            for (int i = 0; i < Chips; i++)
            {
                bool on = i < s.pointCount;
                chips[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                byte h = s.points[i];
                var c = h < 3 ? Ink(h) : new Color(1f, 1f, 1f, 0.16f);
                if ((s.contested >> i & 1) != 0)
                    c.a *= flick;
                chips[i].color = c;
                chipText[i].color = h < 3 ? new Color(0.05f, 0.06f, 0.07f, 1f) : UiKit.Ink;
            }
        }

        void Status(in LtSiteState s, byte us, int pct, int locked, bool landing)
        {
            var b = statusBuf.Clear();
            if (landing)
            {
                b.Append("A LANDING: IT CAN'T BE ATTACKED");
                return;
            }
            if (locked > 0)
            {
                b.Append(s.heldSecs < 5f ? "CAPTURED" : "SECURED").Append("  ·  LOCKED ").Append(locked / 60).Append(':');
                if (locked % 60 < 10)
                    b.Append('0');
                b.Append(locked % 60);
                return;
            }
            if (s.attacker < 3 && s.meter > 0.001f)
            {
                if (s.attacker == us)
                    b.Append("CAPTURING ");
                else if (s.owner == us)
                    b.Append("UNDER ATTACK ");
                else
                    b.Append(FactionName[s.attacker]).Append(" CAPTURING ");
                b.Append(pct).Append('%');
                if (s.contested != 0)
                    b.Append("  ·  CONTESTED");
                return;
            }
            if (s.contested != 0)
                b.Append("CONTESTED");
            else if (s.owner == us)
                b.Append("HELD");
            else if ((s.attackable >> us & 1) != 0)
                b.Append("TAKE ITS POINTS");
            else
                b.Append("NOT LINKED: TAKE A SITE NEXT TO IT FIRST");
        }

        void Hide()
        {
            if (panel.gameObject.activeSelf)
                panel.gameObject.SetActive(false);
            shownSite = -2;
        }
    }
}
