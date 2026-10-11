using Lattice.Diagnostics;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.Combat
{
    /// <summary>
    /// The fight's HUD (GDD §9.3, §15), from the plugin's frame: the reticle,
    /// four ticks at the cone of fire's edge (it blooms as you fire, widens as
    /// you move; the holographic sight takes over down the sights); hit
    /// markers (white body, orange head, red kill); damage direction, an arc
    /// toward each shooter and a red edge that pulses with the hit; health;
    /// the kill feed; and, dead, who killed us and the time to respawn.
    /// Built in code on its own canvas; nothing allocates per frame (text is
    /// written into reused buffers, only when it changes).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CombatHud : MonoBehaviour
    {
        public Gun Gun;
        /// <summary>Self-check (--hud-test): a scripted run of hits, hurts and kills fed
        /// to the HUD alone (never the game), so screenshots can show every state.</summary>
        public bool Test;
        float testStart = -1f;
        int testStep;

        static readonly string[] FactionNames = { "Ordinate", "Free Compact", "Resonance" };
        /// <summary>The factions' colors, lifted for text on the HUD (amber, jade, magenta).</summary>
        public static readonly Color[] FactionInk =
        {
            new Color(1f, 0.72f, 0.25f),
            new Color(0.3f, 0.86f, 0.64f),
            new Color(0.96f, 0.4f, 0.78f),
        };
        static readonly string[] FactionHex = { "#FFB840", "#4DDBA3", "#F566C7" };

        static readonly Color MarkBody = new Color(1f, 1f, 1f);
        static readonly Color MarkHead = new Color(1f, 0.62f, 0.12f);
        static readonly Color MarkKill = new Color(1f, 0.22f, 0.16f);
        static readonly Color Hurt = new Color(0.95f, 0.1f, 0.06f);
        /// <summary>The hurt edge: deep red over a darkening, so it reads as pain, not as a pink sky.</summary>
        static readonly Color Blood = new Color(0.55f, 0.02f, 0.015f);

        const float MarkSecs = 0.26f, KillMarkSecs = 0.42f, ArcSecs = 1.4f, FeedSecs = 6f;
        const int Arcs = 4, FeedLines = 5;

        RectTransform root, reticle, marker;
        readonly RectTransform[] ticks = new RectTransform[4];
        readonly Image[] tickImages = new Image[8];
        Image centerDot, centerShadow;
        readonly Image[] markBars = new Image[4];
        readonly Image[] arcs = new Image[Arcs], arcShadows = new Image[Arcs];
        readonly float[] arcAt = new float[Arcs], arcRel = new float[Arcs], arcStrength = new float[Arcs];
        Image vignette, vignetteDark;
        RectTransform healthPanel;
        Image healthFill, healthTrail, healthAccent;
        TextMeshProUGUI healthText;
        readonly CharBuf healthBuf = new CharBuf(8);
        int shownHealth = -1;
        float trail = 1f, trailHold;

        struct Kill
        {
            public float At;
            public ushort Killer, Victim;
            public bool Head;
        }
        readonly Kill[] feed = new Kill[FeedLines];
        int feedCount;
        readonly RectTransform[] feedRows = new RectTransform[FeedLines];
        readonly Image[] feedPanels = new Image[FeedLines];
        readonly TextMeshProUGUI[] feedTexts = new TextMeshProUGUI[FeedLines];
        readonly CharBuf[] feedBufs = new CharBuf[FeedLines];
        bool feedDirty;

        RectTransform deathPanel;
        Image deathDim, deathBar;
        TextMeshProUGUI deathTitle, deathKiller, deathTime;
        readonly CharBuf killerBuf = new CharBuf(96), timeBuf = new CharBuf(32);
        float diedAt = -1f;
        ushort killedBy = LtCode.Nobody;
        bool killedByHead;
        int shownTenths = -1;
        ushort shownKiller = 0xFFFE;

        float markAt = -10f, markDur = MarkSecs, hurtPulse, reticleR = 12f;
        Color markColor;
        int nextArc;

        void Start()
        {
            var canvas = UiKit.Canvas("Combat HUD", 5, transform);
            root = (RectTransform)canvas.transform;
            BuildVignette();
            BuildReticle();
            BuildMarker();
            BuildArcs();
            BuildHealth();
            BuildFeed();
            BuildDeath();
            for (int i = 0; i < FeedLines; i++)
                feedBufs[i] = new CharBuf(160);
        }

        // ---- building -------------------------------------------------------

        void BuildVignette()
        {
            vignetteDark = UiKit.Box("Hurt shade", root, new Color(0f, 0f, 0f, 0f));
            vignetteDark.sprite = Sprites.Vignette;
            UiKit.Fill(vignetteDark.rectTransform);
            vignette = UiKit.Box("Hurt vignette", root, new Color(Blood.r, Blood.g, Blood.b, 0f));
            vignette.sprite = Sprites.Vignette;
            UiKit.Fill(vignette.rectTransform);
        }

        void BuildReticle()
        {
            reticle = UiKit.Rect("Reticle", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                var t = UiKit.Rect($"Tick {i}", reticle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, horizontal ? new Vector2(11f, 4f) : new Vector2(4f, 11f));
                var shadow = UiKit.Box("Shadow", t, new Color(0f, 0f, 0f, 0.38f));
                UiKit.Fill(shadow.rectTransform);
                var tick = UiKit.Box("Tick", t, new Color(1f, 1f, 1f, 0.92f));
                UiKit.Fill(tick.rectTransform, 1f);
                ticks[i] = t;
                tickImages[i * 2] = shadow;
                tickImages[i * 2 + 1] = tick;
            }
            centerShadow = UiKit.Box("Dot shadow", reticle, new Color(0f, 0f, 0f, 0.38f));
            centerShadow.rectTransform.sizeDelta = new Vector2(5f, 5f);
            centerDot = UiKit.Box("Dot", reticle, new Color(1f, 1f, 1f, 0.92f));
            centerDot.rectTransform.sizeDelta = new Vector2(3f, 3f);
        }

        void BuildMarker()
        {
            marker = UiKit.Rect("Hit marker", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + 90f * i) * Mathf.Deg2Rad;
                var bar = UiKit.Box($"Bar {i}", marker, MarkBody);
                var r = bar.rectTransform;
                r.sizeDelta = new Vector2(2.6f, 10f);
                r.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 12.5f;
                r.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i - 90f);
                markBars[i] = bar;
            }
            marker.gameObject.SetActive(false);
        }

        void BuildArcs()
        {
            for (int i = 0; i < Arcs; i++)
            {
                // A dark band under each arc keeps it readable against a bright sky.
                var shadow = UiKit.Box($"Damage arc shadow {i}", root, new Color(0f, 0f, 0f, 0f));
                shadow.sprite = Sprites.Arc;
                var sr = shadow.rectTransform;
                sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
                sr.sizeDelta = new Vector2(392f, 392f);
                var img = UiKit.Box($"Damage arc {i}", root, new Color(Hurt.r, Hurt.g, Hurt.b, 0f));
                img.sprite = Sprites.Arc;
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(380f, 380f);
                arcs[i] = img;
                arcShadows[i] = shadow;
                arcAt[i] = -10f;
            }
        }

        void BuildHealth()
        {
            healthPanel = UiKit.Rect("Health", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(44f, 40f), new Vector2(330f, 58f));
            var bg = UiKit.Box("Panel", healthPanel, UiKit.Panel);
            UiKit.Fill(bg.rectTransform);
            healthAccent = UiKit.Box("Faction", healthPanel, FactionInk[0]);
            var ar = healthAccent.rectTransform;
            ar.anchorMin = new Vector2(0f, 0f);
            ar.anchorMax = new Vector2(0f, 1f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.sizeDelta = new Vector2(4f, 0f);
            ar.anchoredPosition = Vector2.zero;
            var label = UiKit.Text("Label", healthPanel, UiKit.Display, 17f, UiKit.Muted);
            label.text = "HEALTH";
            label.characterSpacing = 6f;
            var lr = label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
            lr.pivot = new Vector2(0f, 1f);
            lr.anchoredPosition = new Vector2(18f, -6f);
            lr.sizeDelta = new Vector2(200f, 22f);
            healthText = UiKit.Text("Value", healthPanel, UiKit.Mono, 30f, UiKit.Ink, TextAlignmentOptions.Right);
            UiKit.Warm(healthText, 16);
            var tr = healthText.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(1f, 1f);
            tr.anchoredPosition = new Vector2(-14f, -2f);
            tr.sizeDelta = new Vector2(120f, 34f);
            var track = UiKit.Box("Track", healthPanel, new Color(1f, 1f, 1f, 0.08f));
            var kr = track.rectTransform;
            kr.anchorMin = new Vector2(0f, 0f);
            kr.anchorMax = new Vector2(1f, 0f);
            kr.pivot = new Vector2(0f, 0f);
            kr.offsetMin = new Vector2(18f, 12f);
            kr.offsetMax = new Vector2(-14f, 20f);
            healthTrail = UiKit.Box("Trail", track.rectTransform, new Color(1f, 0.85f, 0.75f, 0.55f));
            healthFill = UiKit.Box("Fill", track.rectTransform, UiKit.Good);
            foreach (var img in new[] { healthTrail, healthFill })
            {
                var r = img.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = new Vector2(1f, 1f);
                r.offsetMin = r.offsetMax = Vector2.zero;
            }
        }

        void BuildFeed()
        {
            for (int i = 0; i < FeedLines; i++)
            {
                var row = UiKit.Rect($"Feed {i}", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f - 38f * i), new Vector2(520f, 32f));
                var panel = UiKit.Box("Panel", row, UiKit.Panel);
                UiKit.Fill(panel.rectTransform);
                var text = UiKit.Text("Text", row, UiKit.Display, 22f, UiKit.Ink, TextAlignmentOptions.Right);
                UiKit.Warm(text, 200);
                UiKit.Fill(text.rectTransform);
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                text.rectTransform.offsetMax = new Vector2(-12f, 0f);
                text.verticalAlignment = VerticalAlignmentOptions.Middle;
                feedRows[i] = row;
                feedPanels[i] = panel;
                feedTexts[i] = text;
                row.gameObject.SetActive(false);
            }
        }

        void BuildDeath()
        {
            deathDim = UiKit.Box("Death dim", root, new Color(0.02f, 0.01f, 0.01f, 0f));
            deathDim.sprite = Sprites.Vignette;
            UiKit.Fill(deathDim.rectTransform);
            deathPanel = UiKit.Rect("Death", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(700f, 170f));
            deathTitle = UiKit.Text("Title", deathPanel, UiKit.Display, 22f, UiKit.Muted, TextAlignmentOptions.Center);
            deathTitle.text = "KILLED BY";
            deathTitle.characterSpacing = 10f;
            Place(deathTitle.rectTransform, new Vector2(0f, 64f), new Vector2(700f, 30f));
            deathKiller = UiKit.Text("Killer", deathPanel, UiKit.Display, 46f, UiKit.Ink, TextAlignmentOptions.Center);
            UiKit.Warm(deathKiller, 160);
            Place(deathKiller.rectTransform, new Vector2(0f, 22f), new Vector2(700f, 56f));
            deathTime = UiKit.Text("Respawn", deathPanel, UiKit.Mono, 22f, UiKit.Ink, TextAlignmentOptions.Center);
            UiKit.Warm(deathTime, 48);
            Place(deathTime.rectTransform, new Vector2(0f, -30f), new Vector2(700f, 30f));
            var track = UiKit.Box("Track", deathPanel, new Color(1f, 1f, 1f, 0.12f));
            Place(track.rectTransform, new Vector2(0f, -58f), new Vector2(260f, 4f));
            deathBar = UiKit.Box("Bar", track.rectTransform, UiKit.Ink);
            var br = deathBar.rectTransform;
            br.anchorMin = Vector2.zero;
            br.anchorMax = new Vector2(0f, 1f);
            br.pivot = new Vector2(0f, 0.5f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            deathPanel.gameObject.SetActive(false);
        }

        static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        // ---- each frame -----------------------------------------------------

        void LateUpdate()
        {
            if (root == null)
                return;
            var f = NetDriver.Frame;
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool live = f.Welcomed && p != null;
            bool dead = live && f.Own.dead != 0;
            bool body = live && p.Mode != ViewMode.Spectator;

            for (int i = 0; i < f.EventCount; i++)
                Take(f.Events[i], f, p, now);
            if (Test && live)
                Script(f, p, now);

            Reticle(f, p, body && !dead, dt);
            Marker(now);
            DamageArcs(p, now, dt);
            Vitals(f, body, dead, now, dt);
            Feed(f, now);
            Death(f, dead, now, dt);
        }

        /// <summary>The --hud-test script: every 0.6 s the next beat, from 2 s after we're in.
        /// The screenshots of a burst at the same pace land on each beat's look.</summary>
        void Script(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, float now)
        {
            if (testStart < 0f)
                testStart = now + 2f;
            int step = (int)((now - testStart) / 0.6f);
            if (now < testStart || step < testStep)
                return;
            testStep = step + 1;
            ushort me = f.Own.entity;
            byte facing = (byte)Mathf.RoundToInt((p.Yaw / (Mathf.PI * 2f)) * 256f);
            ushort enemy = (ushort)(me + 1), other = (ushort)(me + 2), friend = (ushort)(me + 3);
            switch (step % 8)
            {
                case 0: Take(new LtEvent { kind = LtCode.EventHit, amount = 20, a = enemy }, f, p, now); break;
                case 1: Take(new LtEvent { kind = LtCode.EventHit, flags = (byte)LtCode.EventHead, amount = 40, a = enemy }, f, p, now); break;
                case 2:
                    Take(new LtEvent { kind = LtCode.EventHit, flags = (byte)(LtCode.EventHead | LtCode.EventKilled), amount = 40, a = enemy }, f, p, now);
                    Take(new LtEvent { kind = LtCode.EventKill, flags = (byte)LtCode.EventHead, a = me, b = enemy }, f, p, now);
                    break;
                case 3: Take(new LtEvent { kind = LtCode.EventHurt, amount = 20, dir = (byte)(facing + 64), a = other }, f, p, now); break;
                case 4:
                    Take(new LtEvent { kind = LtCode.EventHurt, amount = 40, dir = (byte)(facing + 160), a = other }, f, p, now);
                    Take(new LtEvent { kind = LtCode.EventKill, a = other, b = friend }, f, p, now);
                    break;
                case 5: Take(new LtEvent { kind = LtCode.EventHurt, amount = 20, dir = facing, a = other }, f, p, now); break;
                case 6: Take(new LtEvent { kind = LtCode.EventKill, a = friend, b = other }, f, p, now); break;
                default: break;
            }
        }

        void Take(in LtEvent e, NetFrame f, Lattice.LocalPlayer.LocalPlayer p, float now)
        {
            switch (e.kind)
            {
                case LtCode.EventHit:
                {
                    bool killed = (e.flags & LtCode.EventKilled) != 0;
                    bool head = (e.flags & LtCode.EventHead) != 0;
                    // The strongest news of the moment wins: a kill, then a head hit.
                    bool fresh = now - markAt > 0.05f;
                    if (fresh || killed || (head && markColor != MarkKill))
                    {
                        markColor = killed ? MarkKill : head ? MarkHead : MarkBody;
                        markDur = killed ? KillMarkSecs : MarkSecs;
                        // The scripted check holds each mark through its beat, so a screenshot finds it.
                        if (Test)
                            markDur = 0.58f;
                        markAt = now;
                    }
                    break;
                }
                case LtCode.EventHurt:
                {
                    int k = nextArc++ % Arcs;
                    arcAt[k] = now;
                    arcRel[k] = e.dir / 256f * 360f;
                    arcStrength[k] = Mathf.Clamp01(0.55f + e.amount / 60f);
                    hurtPulse = Mathf.Min(1f, hurtPulse + 0.35f + e.amount / 80f);
                    break;
                }
                case LtCode.EventKill:
                {
                    if (feedCount == FeedLines)
                    {
                        for (int i = 1; i < FeedLines; i++)
                            feed[i - 1] = feed[i];
                        feedCount--;
                    }
                    feed[feedCount++] = new Kill { At = now, Killer = e.a, Victim = e.b, Head = (e.flags & LtCode.EventHead) != 0 };
                    feedDirty = true;
                    if (e.b == f.Own.entity)
                    {
                        killedBy = e.a;
                        killedByHead = (e.flags & LtCode.EventHead) != 0;
                    }
                    break;
                }
            }
        }

        void Reticle(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, bool shown, float dt)
        {
            float sights = Gun != null && p != null && p.Mode == ViewMode.FirstPerson ? Gun.Blend : 0f;
            float alpha = shown ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.6f, sights)) : 0f;
            reticle.gameObject.SetActive(alpha > 0.01f);
            if (alpha <= 0.01f)
                return;
            var cam = Gun != null && Gun.Rig != null ? Gun.Rig.Camera : Camera.main;
            float halfFov = (cam != null ? cam.fieldOfView : 74f) * 0.5f * Mathf.Deg2Rad;
            float r = Mathf.Tan(f.Own.coneDeg * Mathf.Deg2Rad) / Mathf.Tan(halfFov) * 540f;
            r = Mathf.Max(r, 7f);
            reticleR = Mathf.Lerp(reticleR, r, 1f - Mathf.Exp(-dt / 0.035f));
            ticks[0].anchoredPosition = new Vector2(reticleR + 5.5f, 0f);
            ticks[1].anchoredPosition = new Vector2(-reticleR - 5.5f, 0f);
            ticks[2].anchoredPosition = new Vector2(0f, reticleR + 5.5f);
            ticks[3].anchoredPosition = new Vector2(0f, -reticleR - 5.5f);
            for (int i = 0; i < tickImages.Length; i++)
            {
                var c = tickImages[i].color;
                c.a = (i % 2 == 0 ? 0.38f : 0.92f) * alpha;
                tickImages[i].color = c;
            }
            centerDot.color = new Color(1f, 1f, 1f, 0.92f * alpha);
            centerShadow.color = new Color(0f, 0f, 0f, 0.38f * alpha);
        }

        void Marker(float now)
        {
            float t = (now - markAt) / markDur;
            bool on = t >= 0f && t < 1f;
            if (marker.gameObject.activeSelf != on)
                marker.gameObject.SetActive(on);
            if (!on)
                return;
            // Pops out, settles, fades.
            float pop = 1f + 0.35f * Mathf.Exp(-t * 14f) * (markColor == MarkKill ? 1.6f : 1f);
            float scale = (markColor == MarkKill ? 1.3f : 1f) * pop;
            marker.localScale = new Vector3(scale, scale, 1f);
            float a = 1f - t * t;
            for (int i = 0; i < 4; i++)
                markBars[i].color = new Color(markColor.r, markColor.g, markColor.b, a);
        }

        void DamageArcs(Lattice.LocalPlayer.LocalPlayer p, float now, float dt)
        {
            float viewYaw = p != null ? p.Yaw * Mathf.Rad2Deg : 0f;
            for (int i = 0; i < Arcs; i++)
            {
                float t = (now - arcAt[i]) / ArcSecs;
                var img = arcs[i];
                var shadow = arcShadows[i];
                if (t < 0f || t >= 1f)
                {
                    if (img.enabled)
                        img.enabled = shadow.enabled = false;
                    continue;
                }
                img.enabled = shadow.enabled = true;
                // The shooter's bearing from where we look now: ahead is up, left is left.
                float rel = Mathf.DeltaAngle(viewYaw, arcRel[i]);
                var turn = Quaternion.Euler(0f, 0f, rel);
                img.rectTransform.localRotation = turn;
                shadow.rectTransform.localRotation = turn;
                float a = arcStrength[i] * (1f - t) * (1f - t) * Mathf.Min(1f, (now - arcAt[i]) / 0.04f);
                img.color = new Color(Hurt.r, Hurt.g, Hurt.b, a);
                shadow.color = new Color(0f, 0f, 0f, a * 0.45f);
            }
        }

        void Vitals(NetFrame f, bool shown, bool dead, float now, float dt)
        {
            if (healthPanel.gameObject.activeSelf != shown)
                healthPanel.gameObject.SetActive(shown);
            float hp = f.Welcomed ? f.Own.health / (float)LtConst.MaxHealth : 0f;
            // The red edge: a pulse with each hit, and a glow that stays when low.
            hurtPulse = Mathf.Max(0f, hurtPulse - dt / 0.7f);
            float low = shown && !dead ? Mathf.Clamp01((0.45f - hp) / 0.45f) * 0.5f : 0f;
            float edge = Mathf.Max(hurtPulse * 0.85f, low * (0.75f + 0.25f * Mathf.Sin(now * 5f)));
            vignette.color = new Color(Blood.r, Blood.g, Blood.b, edge * 0.7f);
            vignetteDark.color = new Color(0f, 0f, 0f, edge * 0.45f);
            if (!shown)
                return;
            healthFill.rectTransform.anchorMax = new Vector2(hp, 1f);
            healthFill.color = hp > 0.5f ? UiKit.Good : hp > 0.25f ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.92f, 0.28f, 0.22f);
            // The trail holds what was just lost for a moment, then drains to the bar.
            if (hp >= trail)
                trail = hp;
            else if (now - trailHold > 0.45f)
                trail = Mathf.MoveTowards(trail, hp, dt * 0.9f);
            if (hp < trail && shownHealth != f.Own.health)
                trailHold = now;
            healthTrail.rectTransform.anchorMax = new Vector2(trail, 1f);
            healthAccent.color = FactionInk[Mathf.Clamp(f.Own.faction, 0, 2)];
            if (shownHealth != f.Own.health)
            {
                shownHealth = f.Own.health;
                healthBuf.Clear().Append(shownHealth);
                healthText.SetCharArray(healthBuf.Chars, 0, healthBuf.Length);
            }
        }

        void Feed(NetFrame f, float now)
        {
            int keep = 0;
            for (int i = 0; i < feedCount; i++)
                if (now - feed[i].At < FeedSecs)
                    feed[keep++] = feed[i];
            if (keep != feedCount)
            {
                feedCount = keep;
                feedDirty = true;
            }
            ushort me = f.Own.entity;
            for (int i = 0; i < FeedLines; i++)
            {
                // Newest at the top.
                int k = feedCount - 1 - i;
                bool on = k >= 0;
                if (feedRows[i].gameObject.activeSelf != on)
                    feedRows[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                var e = feed[k];
                float age = now - e.At;
                float a = Mathf.Min(1f, age / 0.12f) * Mathf.Min(1f, (FeedSecs - age) / 0.6f);
                bool mine = e.Killer == me || e.Victim == me;
                var pc = mine ? new Color(0.2f, 0.06f, 0.05f, 0.82f) : UiKit.Panel;
                if (e.Killer == me && e.Victim != me)
                    pc = new Color(0.08f, 0.1f, 0.12f, 0.88f);
                feedPanels[i].color = new Color(pc.r, pc.g, pc.b, pc.a * a);
                feedTexts[i].alpha = a;
                feedRows[i].anchoredPosition = new Vector2(-40f + 30f * (1f - Mathf.Min(1f, age / 0.15f)), -40f - 38f * i);
                if (feedDirty)
                {
                    var b = feedBufs[i].Clear();
                    Name(b, e.Killer, me);
                    // (The display font has no geometric arrows: the angle quote is ours.)
                    b.Append("  <color=#9AA5A3>›</color>  ");
                    Name(b, e.Victim, me);
                    if (e.Head)
                        b.Append("  <size=70%><color=#FFA020>HEAD</color></size>");
                    feedTexts[i].SetCharArray(b.Chars, 0, b.Length);
                }
            }
            feedDirty = false;
        }

        static void Name(CharBuf b, ushort id, ushort me)
        {
            int fac = id % LtConst.Factions;
            b.Append("<color=").Append(FactionHex[fac]).Append('>');
            if (id == me)
                b.Append("YOU");
            else
                b.Append(FactionNames[fac]).Append(" <size=80%>#").Append((int)id).Append("</size>");
            b.Append("</color>");
        }

        void Death(NetFrame f, bool dead, float now, float dt)
        {
            if (dead && diedAt < 0f)
                diedAt = now;
            if (!dead)
            {
                diedAt = -1f;
                killedBy = LtCode.Nobody;
            }
            float dim = deathDim.color.a;
            dim = Mathf.MoveTowards(dim, dead ? 0.55f : 0f, dt / (dead ? 0.9f : 0.25f));
            deathDim.color = new Color(0.02f, 0.01f, 0.01f, dim);
            if (deathPanel.gameObject.activeSelf != dead)
                deathPanel.gameObject.SetActive(dead);
            if (!dead)
                return;
            float total = LtConst.RespawnSteps / (float)LtConst.TickHz;
            float left = Mathf.Max(0f, total - (now - diedAt));
            float fade = Mathf.Clamp01((now - diedAt - 0.35f) / 0.5f);
            deathTitle.alpha = fade;
            deathKiller.alpha = fade;
            deathTime.alpha = fade;
            deathBar.rectTransform.anchorMax = new Vector2(1f - left / total, 1f);
            if (shownKiller != killedBy)
            {
                shownKiller = killedBy;
                killerBuf.Clear();
                if (killedBy == LtCode.Nobody)
                    killerBuf.Append("<color=#C8CFCC>ELIMINATED</color>");
                else
                {
                    Name(killerBuf, killedBy, f.Own.entity);
                    if (killedByHead)
                        killerBuf.Append(" <size=55%><color=#FFA020>HEADSHOT</color></size>");
                }
                deathKiller.SetCharArray(killerBuf.Chars, 0, killerBuf.Length);
                deathTitle.text = killedBy == LtCode.Nobody ? "" : "KILLED BY";
            }
            int tenths = Mathf.CeilToInt(left * 10f);
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                timeBuf.Clear().Append("RESPAWN  ").Append(left, 1).Append(" s");
                deathTime.SetCharArray(timeBuf.Chars, 0, timeBuf.Length);
            }
        }

        /// <summary>The HUD's generated sprites: the damage arc and the screen edge.</summary>
        static class Sprites
        {
            static Sprite arc, vignette;

            /// <summary>A short, thick arc at the top of a ring (it's turned toward the shooter).</summary>
            public static Sprite Arc => arc ??= Make(256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float ang = Mathf.Atan2(x, y) * Mathf.Rad2Deg;   // 0 at the top
                // Thickest in the middle (a pointer, not a band): the outer edge bulges there.
                float outer = 0.93f + 0.04f * Mathf.Clamp01(1f - Mathf.Abs(ang) / 24f);
                float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 0.83f, r)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outer - 0.03f, outer, r)));
                float span = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(16f, 26f, Mathf.Abs(ang)));
                return band * span;
            });

            /// <summary>Clear in the middle, rising to the edges and corners.</summary>
            public static Sprite Vignette => vignette ??= Make(256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x * 0.7f + y * y) / Mathf.Sqrt(1.7f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1.05f, r));
                return a * a;
            });

            static Sprite Make(int size, System.Func<float, float, float> alpha)
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
        }
    }
}
