using Lattice.Native;
using UnityEngine;

namespace Lattice.UI
{
    /// <summary>
    /// What the shell and the HUD call things: the factions (and their inks:
    /// territory is about which faction, so its colors are the factions'),
    /// the kinds of site, and each site by its kind and the map's grid square
    /// ("RIFT LEDGE C4": the map is ruled in 1 km squares, A to H west to
    /// east, 1 to 8 north to south). Strings are made once per world.
    /// </summary>
    public static class Names
    {
        public static readonly Color[] FactionInks = { new Color(1f, 0.72f, 0.25f), new Color(0.3f, 0.86f, 0.64f), new Color(0.96f, 0.4f, 0.78f) };
        static readonly string[] FactionNames = { "ORDINATE", "FREE COMPACT", "RESONANCE" };
        static readonly string[] FactionHexes = { "#FFB840", "#4DDBA3", "#F566C7" };
        public static readonly Color NoOneInk = new Color(0.62f, 0.66f, 0.68f, 1f);
        /// <summary>The map's grid: squares this big, this many a side.</summary>
        public const float GridSquare = 1024f;
        public const int GridSquares = 8;

        public static Color Ink(int faction) => faction >= 0 && faction < 3 ? FactionInks[faction] : NoOneInk;
        public static string Faction(int faction) => faction >= 0 && faction < 3 ? FactionNames[faction] : "NO ONE";
        public static string Hex(int faction) => faction >= 0 && faction < 3 ? FactionHexes[faction] : "#9EA8AD";

        public static string Kind(int kind) => kind switch
        {
            LtCode.SiteBase => "BASE",
            LtCode.SiteOutpost => "OUTPOST",
            LtCode.SiteLanding => "LANDING",
            LtCode.SiteHollow => "THE HOLLOW",
            LtCode.SiteSpan => "SPAN",
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

        /// <summary>What a kind of site is, for the map's sizes and the spawn list.</summary>
        public enum Rank { Outpost, Base, Great, Landing }

        public static Rank RankOf(int kind) => kind switch
        {
            LtCode.SiteLanding => Rank.Landing,
            LtCode.SiteHollow or LtCode.SiteSpan or LtCode.SiteCliffhold or LtCode.SiteGeode => Rank.Great,
            LtCode.SiteArch or LtCode.SiteVent or LtCode.SiteTunnelMouth or LtCode.SiteSpirePerch or LtCode.SiteWellShrine or LtCode.SiteWreck or LtCode.SiteOutpost => Rank.Outpost,
            _ => Rank.Base,
        };

        /// <summary>The grid square at (x, y), game space: "C4".</summary>
        public static string Grid(float x, float y)
        {
            int col = Mathf.Clamp((int)(x / GridSquare), 0, GridSquares - 1);
            int row = Mathf.Clamp(GridSquares - 1 - (int)(y / GridSquare), 0, GridSquares - 1);
            return $"{(char)('A' + col)}{row + 1}";
        }

        static LtSite[] namedFor;
        static string[] siteNames;

        /// <summary>Every site's name ("RIFT LEDGE C4"; the Hollow and the Landings by
        /// their own names), made once for a world's sites. Two of a kind in one
        /// square are told apart by their order west to east.</summary>
        public static string[] Sites(LtSite[] sites)
        {
            if (namedFor == sites && siteNames != null)
                return siteNames;
            var names = new string[sites.Length];
            for (int i = 0; i < sites.Length; i++)
            {
                var s = sites[i];
                names[i] = s.kind switch
                {
                    LtCode.SiteHollow => "THE HOLLOW",
                    LtCode.SiteLanding => s.sector < 3 ? $"{FactionNames[s.sector]} LANDING" : "LANDING",
                    _ => $"{Kind(s.kind)} {Grid(s.x, s.y)}",
                };
            }
            var plain = (string[])names.Clone();
            for (int i = 0; i < names.Length; i++)
            {
                int west = 0;
                for (int j = 0; j < plain.Length; j++)
                    if (j != i && plain[j] == plain[i] && (sites[j].x < sites[i].x || (sites[j].x == sites[i].x && j < i)))
                        west++;
                if (west > 0)
                    names[i] = $"{plain[i]} {(west == 1 ? "II" : west == 2 ? "III" : "IV")}";
            }
            (namedFor, siteNames) = (sites, names);
            return names;
        }
    }
}
