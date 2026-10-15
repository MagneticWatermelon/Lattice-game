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
    /// The fight's HUD (GDD §9.3, §15), from the plugin's frame: the reticle,
    /// four ticks at the cone of fire's edge (it blooms as you fire, widens as
    /// you move; the holographic sight takes over down the sights); hit
    /// markers (white body, orange head, red kill); damage direction, an arc
    /// toward each shooter and a red edge that pulses with the hit; health;
    /// the kill feed; and, dead, the world dimmed (the shell's death screen
    /// says who killed us, and what next).
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

        static readonly Color MarkBody = new Color(1f, 1f, 1f);
        static readonly Color MarkHead = new Color(1f, 0.62f, 0.12f);
        static readonly Color MarkKill = new Color(1f, 0.22f, 0.16f);
        static readonly Color Hurt = new Color(0.95f, 0.1f, 0.06f);
        /// <summary>The hurt edge: deep red over a darkening, so it reads as pain, not as a pink sky.</summary>
        static readonly Color Blood = new Color(0.55f, 0.02f, 0.015f);

        const float MarkSecs = 0.26f, KillMarkSecs = 0.42f, ArcSecs = 1.4f, FeedSecs = 6f;
        const int Arcs = 4, FeedLines = 5;

        RectTransform root, reticle, marker, scope;
        Graphic[] scopeParts;
        float shownScope = -1f;
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
        // The color scheme the kill feed was written in (IFF or faction: SoldierColors).
        SoldierColors.Scheme feedScheme;

        // The rounds: the cell's count and what's carried, a segment per round (they
        // refill as the reload runs), and a prompt under the reticle when low or out.
        RectTransform ammoPanel;
        Image ammoAccent;
        TextMeshProUGUI ammoLabel, ammoCount, ammoPrompt;
        Image[] rounds;
        readonly CharBuf ammoBuf = new CharBuf(64);
        int shownMag = -1, shownReserve = -1, shownFill = -1, shownAmmoState = -1, shownPrompt = -1, shownRounds = -1;
        static readonly Color Low = new Color(0.98f, 0.72f, 0.22f);
        static readonly Color Out = new Color(0.95f, 0.3f, 0.24f);
        static readonly Color Spent = new Color(1f, 1f, 1f, 0.09f);

        Image deathDim;

        float markAt = -10f, markDur = MarkSecs, hurtPulse, reticleR = 12f;
        Color markColor;
        int nextArc;

        void Start()
        {
            var canvas = UiKit.Canvas("Combat HUD", 5, transform);
            root = (RectTransform)canvas.transform;
            BuildVignette();
            BuildScope();
            BuildReticle();
            BuildMarker();
            BuildArcs();
            BuildHealth();
            BuildAmmo();
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

        /// <summary>The scope's picture, fully down the marksman rifle's sights: its lens
        /// a circle the screen's height, black around it, a crosshair with heavy posts
        /// and an amber dot (the aim, at the screen's center).</summary>
        void BuildScope()
        {
            scope = UiKit.Rect("Scope", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1080f, 1080f));
            var lens = new GameObject("Lens", typeof(RectTransform)).AddComponent<RawImage>();
            lens.transform.SetParent(scope, false);
            lens.texture = ScopeTexture();
            lens.raycastTarget = false;
            UiKit.Fill(lens.rectTransform);
            var left = UiKit.Box("Left", scope, Color.black);
            var right = UiKit.Box("Right", scope, Color.black);
            foreach (var (bar, side) in new[] { (left, 0f), (right, 1f) })
            {
                var r = bar.rectTransform;
                r.anchorMin = new Vector2(side, 0f);
                r.anchorMax = new Vector2(side, 1f);
                r.pivot = new Vector2(1f - side, 0.5f);
                r.anchoredPosition = Vector2.zero;
                r.sizeDelta = new Vector2(4000f, 0f);
            }
            scopeParts = new Graphic[] { lens, left, right };
            scope.gameObject.SetActive(false);
        }

        static Texture2D ScopeTexture()
        {
            const int N = 1024;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Scope", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            const float Px = 1f / N, Edge = 0.475f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v), au = Mathf.Abs(u), av = Mathf.Abs(v);
                // Black past the lens, its rim shading in; a faint shade over the glass.
                float a = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Edge - 0.035f, Edge, r)), 0.05f + 0.3f * Mathf.Pow(Mathf.Min(r / Edge, 1f), 6f));
                // Fine lines through the middle (a gap at the center), heavy posts from the
                // sides and below.
                float fine = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f * Px, 1.6f * Px, Mathf.Min(av + (au < 0.01f ? 1f : 0f), au + (av < 0.01f ? 1f : 0f))));
                float heavy = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f * Px, 3.5f * Px, Mathf.Min(au > 0.22f ? av : 1f, v < -0.22f ? au : 1f)));
                a = Mathf.Max(a, 0.9f * Mathf.Max(fine, heavy));
                var c = new Color32(0, 0, 0, (byte)(a * 255f));
                float dot = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f * Px, 3.2f * Px, r));
                if (dot > 0f)
                    c = Color32.Lerp(c, new Color32(255, 150, 60, 255), dot);
                px[y * N + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void Scope(Lattice.LocalPlayer.LocalPlayer p, bool shown)
        {
            float s = shown && Gun != null && p != null && p.Mode == ViewMode.FirstPerson ? Gun.Scoped : 0f;
            bool on = s > 0.001f;
            if (scope.gameObject.activeSelf != on)
                scope.gameObject.SetActive(on);
            if (!on || s == shownScope)
                return;
            shownScope = s;
            foreach (var g in scopeParts)
            {
                var c = g.color;
                c.a = s;
                g.color = c;
            }
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
            healthAccent = UiKit.Box("Faction", healthPanel, SoldierColors.Ink(0));
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

        /// <summary>The rounds, bottom right: the health panel's mirror.</summary>
        void BuildAmmo()
        {
            ammoPanel = UiKit.Rect("Rounds", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 40f), new Vector2(300f, 58f));
            var bg = UiKit.Box("Panel", ammoPanel, UiKit.Panel);
            UiKit.Fill(bg.rectTransform);
            ammoAccent = UiKit.Box("Faction", ammoPanel, SoldierColors.Ink(0));
            var ar = ammoAccent.rectTransform;
            ar.anchorMin = new Vector2(1f, 0f);
            ar.anchorMax = new Vector2(1f, 1f);
            ar.pivot = new Vector2(1f, 0.5f);
            ar.sizeDelta = new Vector2(4f, 0f);
            ar.anchoredPosition = Vector2.zero;
            ammoLabel = UiKit.Text("Label", ammoPanel, UiKit.Display, 17f, UiKit.Muted);
            ammoLabel.characterSpacing = 6f;
            UiKit.Warm(ammoLabel, 16);
            var lr = ammoLabel.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
            lr.pivot = new Vector2(0f, 1f);
            lr.anchoredPosition = new Vector2(14f, -6f);
            lr.sizeDelta = new Vector2(150f, 22f);
            ammoCount = UiKit.Text("Value", ammoPanel, UiKit.Mono, 30f, UiKit.Ink, TextAlignmentOptions.Right);
            UiKit.Warm(ammoCount, 48);
            var cr = ammoCount.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-18f, -2f);
            cr.sizeDelta = new Vector2(180f, 34f);
            var track = UiKit.Rect("Track", ammoPanel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0f, 0f);
            track.offsetMin = new Vector2(14f, 12f);
            track.offsetMax = new Vector2(-18f, 20f);
            // As many segments as the biggest magazine; a class shows its own (U6).
            int n = 0;
            for (int k = 0; k < Weapons.Count; k++)
                n = Mathf.Max(n, Weapons.Of(k).magazine);
            rounds = new Image[Mathf.Max(1, n)];
            for (int i = 0; i < rounds.Length; i++)
                rounds[i] = UiKit.Box($"Round {i}", track, UiKit.Ink);
            LayRounds(LtConst.Magazine);
            ammoPrompt = UiKit.Text("Prompt", root, UiKit.Display, 19f, Low, TextAlignmentOptions.Center);
            ammoPrompt.characterSpacing = 5f;
            UiKit.Warm(ammoPrompt, 64);
            Place(ammoPrompt.rectTransform, new Vector2(0f, -64f), new Vector2(300f, 26f));
            ammoPrompt.gameObject.SetActive(false);
        }

        /// <summary>The first <paramref name="n"/> segments across the track (a magazine's
        /// rounds), the rest hidden.</summary>
        void LayRounds(int n)
        {
            shownRounds = n = Mathf.Clamp(n, 1, rounds.Length);
            for (int i = 0; i < rounds.Length; i++)
            {
                bool on = i < n;
                rounds[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                var r = rounds[i].rectTransform;
                // A sliver of gap between rounds, as a cell's charge marks.
                float gap = Mathf.Min(0.18f / n, 0.03f);
                r.anchorMin = new Vector2(i / (float)n + gap * 0.5f, 0f);
                r.anchorMax = new Vector2((i + 1) / (float)n - gap * 0.5f, 1f);
                r.offsetMin = r.offsetMax = Vector2.zero;
            }
            shownFill = -1;
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

            if (SoldierColors.Mode != feedScheme)
            {
                feedScheme = SoldierColors.Mode;
                feedDirty = true;
            }
            for (int i = 0; i < f.EventCount; i++)
                Take(f.Events[i], f, p, now);
            if (Test && live)
                Script(f, p, now);

            Scope(p, body && !dead);
            Reticle(f, p, body && !dead, dt);
            Marker(now);
            DamageArcs(p, now, dt);
            Vitals(f, body, dead, now, dt);
            Rounds(f, p, body, dead, now);
            Feed(f, now);
            Death(dead && f.Shell.away == 0, dt);
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
            healthAccent.color = SoldierColors.Ink(f.Own.faction);
            if (shownHealth != f.Own.health)
            {
                shownHealth = f.Own.health;
                healthBuf.Clear().Append(shownHealth);
                healthText.SetCharArray(healthBuf.Chars, 0, healthBuf.Length);
            }
        }

        /// <summary>
        /// The rounds: the cell's count over what's carried; a segment per round, amber
        /// when a quarter or less is left; reloading, the label says so and the
        /// segments refill as it runs. Under the reticle, RELOAD when low, NO AMMO
        /// when there's nothing left to reload with (and as the trigger clicks dry).
        /// </summary>
        void Rounds(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, bool shown, bool dead, float now)
        {
            bool on = shown && !dead;
            if (ammoPanel.gameObject.activeSelf != on)
                ammoPanel.gameObject.SetActive(on);
            int mag = f.Own.mag, reserve = f.Own.reserve;
            bool reloading = f.Own.reload > 0f;
            int magazine = Weapons.Of(f.Own.kit).magazine;
            if (magazine != shownRounds)
                LayRounds(magazine);
            bool low = mag * 4 <= magazine;
            bool empty = mag == 0 && reserve == 0 && !reloading;
            // The prompt: 0 none, 1 RELOAD, 2 NO AMMO.
            bool dry = Gun != null && now - Gun.DryAt < 0.7f;
            int prompt = !on || p == null || p.Mode == ViewMode.Spectator ? 0 : empty || dry ? 2 : low && !reloading && reserve > 0 ? 1 : 0;
            if (prompt != shownPrompt)
            {
                shownPrompt = prompt;
                ammoPrompt.gameObject.SetActive(prompt != 0);
                if (prompt != 0)
                {
                    ammoPrompt.text = prompt == 1 ? "RELOAD  <color=#C8CFCC><size=80%>R</size></color>" : "NO AMMO";
                    ammoPrompt.color = prompt == 1 ? Low : Out;
                }
            }
            if (prompt != 0)
            {
                // Breathes when low; pulses hard as the trigger clicks dry.
                float a = prompt == 2 && dry ? 0.55f + 0.45f * Mathf.Cos((now - Gun.DryAt) * 18f) : 0.7f + 0.3f * Mathf.Sin(now * 4f);
                ammoPrompt.alpha = a;
            }
            if (!on)
                return;
            ammoAccent.color = SoldierColors.Ink(f.Own.faction);
            int state = reloading ? 1 : empty ? 2 : low ? 3 : 0;
            if (state != shownAmmoState)
            {
                shownAmmoState = state;
                ammoLabel.text = state == 1 ? "RELOADING" : state == 2 ? "EMPTY" : "CELL";
                ammoLabel.color = state == 1 ? UiKit.Accent : state == 2 ? Out : UiKit.Muted;
                shownFill = -1;
            }
            if (mag != shownMag || reserve != shownReserve)
            {
                (shownMag, shownReserve) = (mag, reserve);
                ammoBuf.Clear().Append(mag).Append("<size=60%><color=#8E9A99>  / ").Append(reserve).Append("</color></size>");
                ammoCount.SetCharArray(ammoBuf.Chars, 0, ammoBuf.Length);
                ammoCount.color = mag == 0 ? Out : low ? Low : UiKit.Ink;
                shownFill = -1;
            }
            // Reloading, the segments fill as it runs (as far as the reserve goes).
            int n = shownRounds;
            int fill;
            if (reloading)
            {
                var r = Gun != null ? Gun.Reloading : null;
                float progress = r != null && r.Active && r.Total > 0f ? Mathf.Clamp01(r.T / r.Total) : 0f;
                int coming = Mathf.Min(n, mag + reserve);
                fill = 1000 + Mathf.Clamp(Mathf.FloorToInt(progress * coming), 0, coming);
            }
            else
                fill = mag;
            if (fill != shownFill)
            {
                shownFill = fill;
                var full = mag == 0 ? Out : low ? Low : UiKit.Ink;
                for (int i = 0; i < n; i++)
                {
                    Color c;
                    if (fill >= 1000)
                        c = i < fill - 1000 ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.75f) : Spent;
                    else
                        c = i < fill ? full : Spent;
                    rounds[i].color = c;
                }
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
            b.Append("<color=").Append(SoldierColors.Hex(fac)).Append('>');
            if (id == me)
                b.Append("YOU");
            else
                b.Append(FactionNames[fac]).Append(" <size=80%>#").Append((int)id).Append("</size>");
            b.Append("</color>");
        }

        /// <summary>Dead: the world dims at the edges (the shell's death screen says who and how).</summary>
        void Death(bool dead, float dt)
        {
            float dim = deathDim.color.a;
            dim = Mathf.MoveTowards(dim, dead ? 0.55f : 0f, dt / (dead ? 0.9f : 0.25f));
            deathDim.color = new Color(0.02f, 0.01f, 0.01f, dim);
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
