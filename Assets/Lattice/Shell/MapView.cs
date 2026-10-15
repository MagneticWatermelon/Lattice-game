using System;
using System.Collections.Generic;
using Lattice.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lattice.Shell
{
    /// <summary>
    /// The continent map (GDD §9.2), for the deploy screen and the map (M):
    /// the land (<see cref="MapTexture"/>), each region tinted by who holds it
    /// with its borders and fronts drawn by <c>Lattice/Map</c>; the lattice's
    /// links between sites, each half in its end's holder's ink (a link
    /// between two holders is a front); every site as a mark by its rank
    /// (Landing, great site, base, outpost) in its holder's ink, with its
    /// meter as a ring in the attacker's; where the fighting is, as rings
    /// that grow with it; and us. The wheel zooms at the pointer, a drag
    /// pans; hovering a site tells about it, clicking one picks it.
    /// </summary>
    public sealed class MapView : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Clicked a site (left button).</summary>
        public event Action<int> Clicked;
        /// <summary>The picked site, drawn picked (-1: none).</summary>
        public int Selected = -1;
        /// <summary>Sites drawn with a spawn's ring (ours to deploy at): set by the deploy screen.</summary>
        public Func<int, bool> Lit;
        /// <summary>A line more for a site's card (its transit, say), or null.</summary>
        public Action<int, CharBuf> Extra;
        public int Hovered { get; private set; } = -1;

        const float WorldSize = 8192f;
        const float MaxZoom = 8f;

        RectTransform frame, area;
        RawImage image;
        Material material;
        MapLines lines;
        LtSite[] sites;
        string[] names;
        (int a, int b)[] links;
        Mark[] marks;
        RectTransform own, card;
        Image ownImage;
        float cardAt;
        TextMeshProUGUI cardTitle, cardBody;
        readonly CharBuf cardText = new CharBuf(320), extraText = new CharBuf(160);
        static readonly Vector4[] inks = new Vector4[4];
        readonly TextMeshProUGUI[] gridCols = new TextMeshProUGUI[Names.GridSquares], gridRows = new TextMeshProUGUI[Names.GridSquares];
        byte[] owners;
        Vector4[] siteData;
        float zoom = 1f;
        Vector2 center = new Vector2(0.5f, 0.5f);
        bool viewDirty = true, inside, dragging;
        int shownCard = -2;
        long cardKey;
        static Texture2D picture;
        static System.Threading.Tasks.Task<Color32[]> painting;
        static LtWorldHandle paintedFor;

        struct Mark
        {
            public RectTransform Root;
            public Image Body, Meter, Ring, Fight;
            public TextMeshProUGUI Label;
            public float Size;
            public Names.Rank Rank;
        }

        static Sprite circle, square, diamond, hexagon, ring, thinRing, arrow;

        public static MapView Create(Transform parent, float x, float y, float side)
        {
            var frame = Widgets.At(parent, "Map", x, y, side, side);
            var v = frame.gameObject.AddComponent<MapView>();
            v.Build(frame, side);
            return v;
        }

        void Build(RectTransform root, float side)
        {
            frame = root;
            frame.gameObject.AddComponent<RectMask2D>();
            var back = UiKit.Box("Backdrop", frame, new Color(0.02f, 0.025f, 0.03f, 1f));
            UiKit.Fill(back.rectTransform);
            area = UiKit.Rect("Area", frame, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(side, side));
            image = area.gameObject.AddComponent<RawImage>();
            image.raycastTarget = true;
            var shader = Resources.Load<Shader>("LatticeMap");
            if (shader != null)
            {
                material = new Material(shader) { name = "Map" };
                image.material = material;
            }
            else
                Debug.LogWarning("Lattice: no Lattice/Map shader in Resources");
            image.color = new Color(1f, 1f, 1f, 0f);
            var linesGo = new GameObject("Links", typeof(RectTransform));
            linesGo.transform.SetParent(area, false);
            lines = linesGo.AddComponent<MapLines>();
            lines.raycastTarget = false;
            lines.View = this;
            UiKit.Fill(lines.rectTransform);
            // (Its vertices are in the map's local pixels, from the bottom left.)
            lines.rectTransform.pivot = Vector2.zero;
            MakeSprites();
            // The grid's letters along the top, numbers down the left.
            for (int i = 0; i < Names.GridSquares; i++)
            {
                gridCols[i] = UiKit.Text("Col", frame, UiKit.Mono, 14f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Top);
                gridCols[i].text = ((char)('A' + i)).ToString();
                gridRows[i] = UiKit.Text("Row", frame, UiKit.Mono, 14f, new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Left);
                gridRows[i].text = (i + 1).ToString();
            }
            own = UiKit.Rect("Us", area, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            ownImage = own.gameObject.AddComponent<Image>();
            ownImage.sprite = arrow;
            ownImage.raycastTarget = false;
            own.gameObject.SetActive(false);
            // A site's card, by the pointer.
            card = UiKit.Rect("Card", frame, Vector2.zero, new Vector2(0f, 0f), Vector2.zero, new Vector2(320f, 150f));
            var cardBg = UiKit.Box("Back", card, new Color(0.03f, 0.04f, 0.045f, 0.94f));
            UiKit.Fill(cardBg.rectTransform);
            cardTitle = UiKit.Text("Name", card, UiKit.Display, 22f, UiKit.Ink, TextAlignmentOptions.TopLeft);
            Widgets.Place(cardTitle.rectTransform, 12f, 8f, 276f, 26f);
            cardBody = UiKit.Text("About", card, UiKit.Mono, 15f, UiKit.Muted, TextAlignmentOptions.TopLeft);
            Widgets.Place(cardBody.rectTransform, 12f, 38f, 296f, 108f);
            cardBody.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Warm(cardBody, 320);
            card.gameObject.SetActive(false);
        }

        /// <summary>The mark of a rank of site (the legend's).</summary>
        public static Sprite Shape(Names.Rank rank)
        {
            MakeSprites();
            return rank switch { Names.Rank.Landing => hexagon, Names.Rank.Great => diamond, Names.Rank.Base => square, _ => circle };
        }

        static void MakeSprites()
        {
            if (circle != null)
                return;
            const int s = 64;
            circle = Widgets.MakeSprite(s, (x, y) => Widgets.Edge(Mathf.Sqrt(x * x + y * y) - 0.86f, s));
            square = Widgets.MakeSprite(s, (x, y) => Widgets.Edge(Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) - 0.78f, s));
            diamond = Widgets.MakeSprite(s, (x, y) => Widgets.Edge((Mathf.Abs(x) + Mathf.Abs(y)) * 0.75f - 0.72f, s));
            hexagon = Widgets.MakeSprite(s, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                return Widgets.Edge(Mathf.Max(ay, ax * 0.866f + ay * 0.5f) - 0.82f, s);
            });
            ring = Widgets.MakeSprite(128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return Widgets.Edge(Mathf.Abs(r - 0.86f) - 0.1f, 128);
            });
            thinRing = Widgets.MakeSprite(128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return Widgets.Edge(Mathf.Abs(r - 0.9f) - 0.05f, 128);
            });
            // An arrow pointing east (+x), turned by our yaw.
            arrow = Widgets.MakeSprite(s, (x, y) =>
            {
                // A chevron: inside the triangle (0.9, 0) (-0.7, ±0.65), minus a notch at the back.
                float tri = Mathf.Max(Mathf.Abs(y) * 1.3f + x * 0.75f - 0.68f, -x - 0.7f);
                float notch = -(Mathf.Abs(y) * 1.1f - x - 0.35f);
                return Widgets.Edge(Mathf.Max(tri, notch * 0.8f), s);
            });
        }

        /// <summary>Takes the world (once): its sites, their names and links, and starts its picture.</summary>
        void Bind(LtWorldHandle world)
        {
            sites = world.Sites();
            names = Names.Sites(sites);
            owners = new byte[sites.Length];
            for (int i = 0; i < owners.Length; i++)
                owners[i] = 254;
            siteData = new Vector4[96];
            var lanes = world.Lanes();
            var set = new HashSet<(int, int)>();
            foreach (var l in lanes)
                if (l.a != l.b && l.a < sites.Length && l.b < sites.Length)
                    set.Add((Mathf.Min(l.a, l.b), Mathf.Max(l.a, l.b)));
            links = new (int, int)[set.Count];
            set.CopyTo(links);
            marks = new Mark[sites.Length];
            for (int i = 0; i < sites.Length; i++)
                marks[i] = MakeMark(i);
            if (paintedFor != world)
            {
                paintedFor = world;
                picture = null;
                painting = MapTexture.Start(world);
            }
            viewDirty = true;
        }

        Mark MakeMark(int i)
        {
            var rank = Names.RankOf(sites[i].kind);
            float size = rank switch { Names.Rank.Landing => 26f, Names.Rank.Great => 22f, Names.Rank.Base => 15f, _ => 11f };
            var root = UiKit.Rect($"Site {i}", area, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            Image Img(string name, Sprite sprite, float scale, Color color)
            {
                var img = UiKit.Box(name, root, color);
                img.sprite = sprite;
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(size * scale, size * scale);
                return img;
            }
            var m = new Mark { Root = root, Size = size, Rank = rank };
            m.Fight = Img("Fight", thinRing, 3f, new Color(1f, 0.55f, 0.2f, 0f));
            var shadow = Img("Shadow", rank switch { Names.Rank.Landing => hexagon, Names.Rank.Great => diamond, Names.Rank.Base => square, _ => circle }, 1.35f, new Color(0f, 0f, 0f, 0.6f));
            m.Body = Img("Mark", shadow.sprite, 1f, Names.NoOneInk);
            m.Meter = Img("Meter", ring, 1.9f, Color.white);
            m.Meter.type = Image.Type.Filled;
            m.Meter.fillMethod = Image.FillMethod.Radial360;
            m.Meter.fillOrigin = (int)Image.Origin360.Top;
            m.Meter.fillClockwise = true;
            m.Meter.fillAmount = 0f;
            m.Ring = Img("Spawn", ring, 2.3f, new Color(1f, 1f, 1f, 0f));
            m.Label = UiKit.Text("Name", root, UiKit.Display, rank == Names.Rank.Outpost ? 13f : 15f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Top);
            var lr = m.Label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.anchoredPosition = new Vector2(0f, -2f);
            lr.sizeDelta = new Vector2(200f, 20f);
            m.Label.text = names[i];
            m.Label.outlineWidth = 0.18f;
            m.Label.outlineColor = new Color32(0, 0, 0, 200);
            return m;
        }

        // ---- the view ----------------------------------------------------------

        float Side => area.rect.width;
        Rect ViewRect => new Rect(center - Vector2.one * (0.5f / zoom), Vector2.one / zoom);

        /// <summary>Where a point of the world (game x, y) is on the map, in its local pixels.</summary>
        public Vector2 ToLocal(float x, float y)
        {
            var v = ViewRect;
            return new Vector2((x / WorldSize - v.x) / v.width * Side, (y / WorldSize - v.y) / v.height * Side);
        }

        Vector2 ToWorld01(Vector2 local)
        {
            var v = ViewRect;
            return new Vector2(v.x + local.x / Side * v.width, v.y + local.y / Side * v.height);
        }

        public Vector2 SiteLocal(int i) => ToLocal(sites[i].x, sites[i].y);
        public float MarkSize(int i) => marks[i].Size * MarkScale;
        float MarkScale => 1f + 0.18f * Mathf.Log(zoom, 2f);
        public (int a, int b)[] Links => links;
        public byte Owner(int i) => owners[i];

        /// <summary>Looks at <paramref name="x"/>, <paramref name="y"/> (game space) at <paramref name="z"/>× zoom.</summary>
        public void LookAt(float x, float y, float z)
        {
            zoom = Mathf.Clamp(z, 1f, MaxZoom);
            center = new Vector2(x / WorldSize, y / WorldSize);
            Clamp();
        }

        void Clamp()
        {
            float h = 0.5f / zoom;
            center = new Vector2(Mathf.Clamp(center.x, h, 1f - h), Mathf.Clamp(center.y, h, 1f - h));
            viewDirty = true;
        }

        bool Local(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera ?? e.enterEventCamera, out local);

        public void OnScroll(PointerEventData e)
        {
            if (!Local(e, out var local) || sites == null)
                return;
            var before = ToWorld01(local);
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.0015f, e.scrollDelta.y * (Mathf.Abs(e.scrollDelta.y) < 10f ? 120f : 1f)), 1f, MaxZoom);
            // Keep the point under the pointer where it was.
            var after = ToWorld01(local);
            center += before - after;
            Clamp();
        }

        public void OnBeginDrag(PointerEventData e) => dragging = e.button == PointerEventData.InputButton.Left || e.button == PointerEventData.InputButton.Right;
        public void OnEndDrag(PointerEventData e) => dragging = false;

        public void OnDrag(PointerEventData e)
        {
            if (!dragging)
                return;
            center -= e.delta / Side / zoom / (frame.lossyScale.x > 0f ? frame.lossyScale.x : 1f);
            Clamp();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && !e.dragging && Hovered >= 0)
                Clicked?.Invoke(Hovered);
        }

        public void OnPointerEnter(PointerEventData e) => inside = true;
        public void OnPointerExit(PointerEventData e) => inside = false;

        // ---- each frame -----------------------------------------------------

        void LateUpdate()
        {
            if (sites == null)
            {
                if (NetDriver.World != null)
                    Bind(NetDriver.World);
                else
                    return;
            }
            if (picture == null && painting != null && painting.IsCompleted)
            {
                if (painting.IsCompletedSuccessfully)
                    picture = MapTexture.Upload(painting.Result);
                else
                    Debug.LogException(painting.Exception);
                painting = null;
            }
            if (picture != null && image.texture != picture)
            {
                image.texture = picture;
                image.color = Color.white;
            }
            var f = NetDriver.Frame;
            bool ownersChanged = false;
            for (int i = 0; i < sites.Length; i++)
            {
                byte o = i < f.SiteCount ? f.Sites[i].owner : (byte)LtCode.NoOne;
                if (o != owners[i])
                {
                    owners[i] = o;
                    ownersChanged = true;
                }
            }
            if (ownersChanged || viewDirty)
                Regions();
            if (viewDirty)
            {
                viewDirty = false;
                image.uvRect = ViewRect;
                lines.SetVerticesDirty();
                Grid();
            }
            else if (ownersChanged)
                lines.SetVerticesDirty();
            Marks(f);
            Us(f);
            Hover();
        }

        void Regions()
        {
            if (material == null)
                return;
            int n = Mathf.Min(sites.Length, siteData.Length);
            for (int i = 0; i < n; i++)
                siteData[i] = new Vector4(sites[i].x / WorldSize, sites[i].y / WorldSize, owners[i] < 3 ? owners[i] : 3, 0f);
            material.SetVectorArray("_Sites", siteData);
            material.SetFloat("_SiteCount", n);
            (inks[0], inks[1], inks[2], inks[3]) = (Names.Ink(0), Names.Ink(1), Names.Ink(2), Names.NoOneInk);
            material.SetVectorArray("_Inks", inks);
        }

        void Grid()
        {
            var v = ViewRect;
            for (int i = 0; i < Names.GridSquares; i++)
            {
                float u = (i + 0.5f) / Names.GridSquares;
                float lx = (u - v.x) / v.width * Side;
                var c = gridCols[i].rectTransform;
                c.anchorMin = c.anchorMax = new Vector2(0f, 1f);
                c.pivot = new Vector2(0.5f, 1f);
                c.anchoredPosition = new Vector2(lx, -4f);
                c.sizeDelta = new Vector2(30f, 20f);
                gridCols[i].gameObject.SetActive(lx > 10f && lx < Side - 10f);
                // Rows count from the north.
                float w = 1f - u;
                float ly = (w - v.y) / v.height * Side;
                var r = gridRows[i].rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
                r.pivot = new Vector2(0f, 0.5f);
                r.anchoredPosition = new Vector2(6f, ly);
                r.sizeDelta = new Vector2(30f, 20f);
                gridRows[i].gameObject.SetActive(ly > 10f && ly < Side - 10f);
            }
        }

        void Marks(NetFrame f)
        {
            float t = Time.unscaledTime;
            float scale = MarkScale;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
            for (int i = 0; i < marks.Length; i++)
            {
                var m = marks[i];
                var p = SiteLocal(i);
                bool on = p.x > -40f && p.y > -40f && p.x < Side + 40f && p.y < Side + 40f;
                if (m.Root.gameObject.activeSelf != on)
                    m.Root.gameObject.SetActive(on);
                if (!on)
                    continue;
                m.Root.anchoredPosition = p;
                m.Root.localScale = new Vector3(scale, scale, 1f);
                var s = i < f.SiteCount ? f.Sites[i] : default;
                byte o = owners[i];
                var ink = Names.Ink(o);
                bool contested = s.contested != 0 || (s.attacker < 3 && s.meter > 0.001f);
                m.Body.color = contested ? Color.Lerp(ink, Color.white, 0.35f * pulse) : ink;
                m.Meter.fillAmount = s.attacker < 3 ? s.meter : 0f;
                m.Meter.color = Names.Ink(s.attacker);
                bool lit = Lit != null && Lit(i);
                bool picked = i == Selected;
                float ringA = picked ? 1f : lit ? 0.45f : 0f;
                m.Ring.color = new Color(1f, 1f, 1f, ringA);
                m.Ring.rectTransform.localScale = Vector3.one * (picked ? 1f + 0.06f * pulse : 1f);
                bool label = picked || i == Hovered || m.Rank >= Names.Rank.Great || (m.Rank == Names.Rank.Base && zoom >= 1.7f) || zoom >= 2.8f;
                if (m.Label.gameObject.activeSelf != label)
                    m.Label.gameObject.SetActive(label);
                m.Label.rectTransform.localScale = Vector3.one / scale;
            }
            // Fights: a ring that grows with who's there and pulses with the firing.
            for (int i = 0; i < marks.Length; i++)
                marks[i].Fight.color = new Color(1f, 0.55f, 0.2f, 0f);
            for (int k = 0; k < f.FightCount; k++)
            {
                var fight = f.Fights[k];
                if (fight.site >= marks.Length)
                    continue;
                int total;
                unsafe
                {
                    total = fight.players[0] + fight.players[1] + fight.players[2];
                }
                if (total < 2 && fight.heat == 0)
                    continue;
                var m = marks[fight.site];
                float r = (18f + 9f * Mathf.Sqrt(total)) / m.Size;
                float beat = 0.5f + 0.5f * Mathf.Sin(t * (2f + Mathf.Min(fight.heat, 40) * 0.15f) + fight.site);
                m.Fight.rectTransform.localScale = Vector3.one * (r / 3f);
                float a = Mathf.Clamp01(0.25f + 0.04f * fight.heat) * (0.55f + 0.45f * beat);
                m.Fight.color = new Color(1f, 0.55f, 0.2f, a);
            }
        }

        void Us(NetFrame f)
        {
            bool on = f.Welcomed && f.Shell.away == 0;
            if (own.gameObject.activeSelf != on)
                own.gameObject.SetActive(on);
            if (!on)
                return;
            own.anchoredPosition = ToLocal(f.Own.pos.x, f.Own.pos.y);
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            float yaw = p != null ? p.Yaw : 0f;
            own.localEulerAngles = new Vector3(0f, 0f, yaw * Mathf.Rad2Deg);
            ownImage.color = f.Own.dead != 0 ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        }

        void Hover()
        {
            int best = -1;
            if (inside && !dragging && UnityEngine.InputSystem.Mouse.current != null)
            {
                var pos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(area, pos, null, out var local))
                {
                    float bestD = float.MaxValue;
                    for (int i = 0; i < sites.Length; i++)
                    {
                        float d = (SiteLocal(i) - local).magnitude;
                        float reach = MarkSize(i) * 0.5f + 8f;
                        if (d < reach && d < bestD)
                            (best, bestD) = (i, d);
                    }
                }
            }
            Hovered = best;
            if (best < 0)
            {
                if (card.gameObject.activeSelf)
                    card.gameObject.SetActive(false);
                shownCard = -2;
                return;
            }
            Card(best);
        }

        void Card(int i)
        {
            var f = NetDriver.Frame;
            var s = i < f.SiteCount ? f.Sites[i] : default;
            int pct = Mathf.FloorToInt(s.meter * 100f);
            int players = PlayersAt(i, out int ours);
            long key = (long)(uint)i | (long)s.owner << 16 | (long)s.attacker << 24 | (long)pct << 32 | (long)Mathf.Min(players, 255) << 40 | (long)Mathf.Min(ours, 255) << 48;
            // (Rewritten when what it says changes, and twice a second for its extra line.)
            if (shownCard != i || key != cardKey || Time.unscaledTime >= cardAt)
            {
                shownCard = i;
                cardKey = key;
                cardAt = Time.unscaledTime + 0.5f;
                extraText.Clear();
                Extra?.Invoke(i, extraText);
                cardTitle.text = names[i];
                cardText.Clear();
                cardText.Append("<color=").Append(Names.Hex(s.owner)).Append('>').Append(s.owner < 3 ? Names.Faction(s.owner) : "NO ONE").Append("</color>");
                if (sites[i].kind == LtCode.SiteLanding)
                    cardText.Append("  ·  CAN'T BE TAKEN");
                else if (s.lockedSecs > 0f)
                    cardText.Append("  ·  LOCKED");
                else if (s.attacker < 3 && pct > 0)
                    cardText.Append("  ·  <color=").Append(Names.Hex(s.attacker)).Append('>').Append(pct).Append("%</color>");
                if (s.contested != 0)
                    cardText.Append("  ·  CONTESTED");
                cardText.Append('\n');
                Presence.At(i, cardCounts, out int heat);
                Presence.Append(cardText, cardCounts, NetDriver.Frame.Own.faction, heat);
                if (extraText.Length > 0)
                {
                    cardText.Append('\n');
                    for (int c = 0; c < extraText.Length; c++)
                        cardText.Append(extraText.Chars[c]);
                }
                cardBody.SetCharArray(cardText.Chars, 0, cardText.Length);
                cardTitle.color = Names.Ink(s.owner);
            }
            var p = SiteLocal(i);
            float x = p.x + 18f, y = p.y + 14f;
            if (x + 320f > Side) x = p.x - 338f;
            if (y + 150f > Side) y = p.y - 164f;
            card.anchoredPosition = new Vector2(x, y);
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);
            card.SetAsLastSibling();
        }

        /// <summary>Everyone at site <paramref name="i"/> by the latest fights, and how many of them are ours.</summary>
        readonly int[] cardCounts = new int[3];

        public unsafe int PlayersAt(int i, out int ours)
        {
            var f = NetDriver.Frame;
            ours = 0;
            for (int k = 0; k < f.FightCount; k++)
            {
                var fight = f.Fights[k];
                if (fight.site != i)
                    continue;
                if (f.Own.faction < 3)
                    ours = fight.players[f.Own.faction];
                return fight.players[0] + fight.players[1] + fight.players[2];
            }
            return 0;
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }

    /// <summary>The lattice's links on the map: each half in its end's holder's ink
    /// over a dark casing, so a front (two holders) reads as two colors meeting.
    /// Rebuilt only when the view or a holder changes.</summary>
    public sealed class MapLines : MaskableGraphic
    {
        public MapView View;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (View == null || View.Links == null)
                return;
            // Casings first, then the inks over them.
            for (int pass = 0; pass < 2; pass++)
            foreach (var (a, b) in View.Links)
            {
                var pa = View.SiteLocal(a);
                var pb = View.SiteLocal(b);
                var d = pb - pa;
                float len = d.magnitude;
                if (len < 1f)
                    continue;
                var dir = d / len;
                // From the edge of each mark, not its middle.
                var s = pa + dir * (View.MarkSize(a) * 0.62f);
                var e = pb - dir * (View.MarkSize(b) * 0.62f);
                if (Vector2.Dot(e - s, dir) <= 0f)
                    continue;
                var mid = (s + e) * 0.5f;
                byte oa = View.Owner(a), ob = View.Owner(b);
                bool front = oa != ob;
                if (pass == 0)
                    Segment(vh, s, e, front ? 5f : 3.5f, new Color(0f, 0f, 0f, 0.55f));
                else
                {
                    float w = front ? 2.6f : 1.6f;
                    float alpha = front ? 1f : 0.6f;
                    var ca = Names.Ink(oa);
                    var cb = Names.Ink(ob);
                    Segment(vh, s, mid, w, new Color(ca.r, ca.g, ca.b, alpha));
                    Segment(vh, mid, e, w, new Color(cb.r, cb.g, cb.b, alpha));
                }
            }
        }

        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (width * 0.5f);
            int k = vh.currentVertCount;
            Color32 c = color;
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(k, k + 1, k + 2);
            vh.AddTriangle(k, k + 2, k + 3);
        }
    }
}
