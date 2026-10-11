using Lattice.Native;
using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// How soldiers are colored, everywhere they're drawn (full soldiers, the
    /// crowd, our own arms) and named (the HUD). GDD §2 and §9.5: by relation
    /// (IFF, the default for new players: ours blue-white, the two enemies orange
    /// and magenta) or by faction (amber, jade, magenta). For debugging, as the
    /// Bevy client's T: by how the netcode draws each one (near, mid, far tier;
    /// held; new). One material per color: copies of the look's faction material
    /// (the assets never change), recolored when the scheme or our faction does.
    /// </summary>
    public static class SoldierColors
    {
        public enum Scheme { Iff, Faction }

        /// <summary>Keys: 0-2 by faction, then the tier colors.</summary>
        public const int Keys = 8;
        const int Tier0 = 3, Held = 6, New = 7;

        public static Scheme Mode = Scheme.Iff;
        /// <summary>Color by how each soldier is drawn instead (T).</summary>
        public static bool Tiers;

        static readonly Color Friendly = new Color(0.62f, 0.80f, 1.00f);
        static readonly Color[] Enemies = { new Color(0.98f, 0.52f, 0.10f), new Color(0.78f, 0.22f, 0.72f) };
        static readonly Color[] TierColors =
        {
            new Color(0.25f, 0.85f, 0.35f), new Color(0.95f, 0.75f, 0.2f), new Color(0.9f, 0.3f, 0.25f),
            new Color(0.5f, 0.5f, 0.5f), new Color(0.95f, 0.95f, 0.95f),
        };
        /// <summary>For text on the HUD: lifted, and as rich-text colors (made once: no allocations).</summary>
        static readonly Color[] FactionInk = { new Color(1f, 0.72f, 0.25f), new Color(0.3f, 0.86f, 0.64f), new Color(0.96f, 0.4f, 0.78f) };
        static readonly Color FriendlyInk = new Color(0.55f, 0.78f, 1f);
        static readonly Color[] EnemyInk = { new Color(1f, 0.6f, 0.24f), new Color(0.96f, 0.4f, 0.78f) };
        static readonly string[] FactionHex = { "#FFB840", "#4DDBA3", "#F566C7" };
        const string FriendlyHex = "#8CC7FF";
        static readonly string[] EnemyHex = { "#FF993D", "#F566C7" };

        static Material[] bodies;
        static int own = -1;
        static Scheme shown;
        static readonly int tintId = Shader.PropertyToID("_TintColor");

        /// <summary>Makes the materials from <paramref name="look"/>'s (once per game).</summary>
        public static void Init(SoldierLook look)
        {
            Dispose();
            bodies = new Material[Keys];
            for (int k = 0; k < Keys; k++)
            {
                bodies[k] = new Material(look.Factions[k < 3 ? k : 0]) { name = $"Soldier colors {k}" };
                if (k >= Tier0)
                    bodies[k].SetColor(tintId, TierColors[k - Tier0].linear);
            }
            own = -1;
            Recolor();
        }

        public static void Dispose()
        {
            if (bodies == null)
                return;
            foreach (var m in bodies)
                if (m != null)
                    Object.Destroy(m);
            bodies = null;
        }

        /// <summary>Each frame: our faction (once known), so IFF knows who's who.</summary>
        public static void Own(int faction)
        {
            if (faction != own || Mode != shown)
            {
                own = faction;
                Recolor();
            }
        }

        static void Recolor()
        {
            shown = Mode;
            if (bodies == null)
                return;
            for (int f = 0; f < 3; f++)
                bodies[f].SetColor(tintId, Body(f).linear);
        }

        /// <summary>A faction's body color under the scheme.</summary>
        public static Color Body(int faction)
        {
            faction = Mathf.Clamp(faction, 0, 2);
            if (Mode == Scheme.Faction || own < 0)
                return SoldierLook.FactionColors[faction];
            return faction == own ? Friendly : Enemies[Enemy(faction)];
        }

        /// <summary>Which enemy (0 orange, 1 magenta): the lower numbered of the two first.</summary>
        static int Enemy(int faction) => faction == (own == 0 ? 1 : 0) ? 0 : 1;

        /// <summary>A faction's color for HUD text.</summary>
        public static Color Ink(int faction)
        {
            faction = Mathf.Clamp(faction, 0, 2);
            if (Mode == Scheme.Faction || own < 0)
                return FactionInk[faction];
            return faction == own ? FriendlyInk : EnemyInk[Enemy(faction)];
        }

        /// <summary>The same as a rich-text color ("#RRGGBB").</summary>
        public static string Hex(int faction)
        {
            faction = Mathf.Clamp(faction, 0, 2);
            if (Mode == Scheme.Faction || own < 0)
                return FactionHex[faction];
            return faction == own ? FriendlyHex : EnemyHex[Enemy(faction)];
        }

        /// <summary>The color key a soldier is drawn with (an index into <see cref="Material"/>).</summary>
        public static int Key(in LtEntity e)
        {
            if (!Tiers)
                return Mathf.Clamp(e.faction, 0, 2);
            return e.how == LtCode.HowHeld ? Held : e.how == LtCode.HowNew ? New : Tier0 + Mathf.Clamp(e.tier, 0, 2);
        }

        public static Material Material(int key) => bodies?[Mathf.Clamp(key, 0, Keys - 1)];
    }
}
