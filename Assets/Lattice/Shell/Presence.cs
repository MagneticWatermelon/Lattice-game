using Lattice.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Lattice.Shell
{
    /// <summary>
    /// Who's at a site, as the deploy screen's rows and the map's card say it:
    /// from the server's fights (every 5 s: each faction's players there, its
    /// shots a second). "14 of ours vs 3 Ordinate, 2 Resonance · FIGHTING",
    /// each count in its faction's color; and a bar of the same in proportion.
    /// </summary>
    public static class Presence
    {
        static readonly string[] Short = { "Ordinate", "Compact", "Resonance" };
        const string Warn = "#E3824F";

        /// <summary>Each faction's players at site <paramref name="site"/> into
        /// <paramref name="at"/> (3), and its shots a second; false if no one's there.</summary>
        public static unsafe bool At(int site, int[] at, out int heat)
        {
            var f = NetDriver.Frame;
            at[0] = at[1] = at[2] = 0;
            heat = 0;
            for (int k = 0; k < f.FightCount; k++)
            {
                var fight = f.Fights[k];
                if (fight.site != site)
                    continue;
                for (int i = 0; i < 3; i++)
                    at[i] = fight.players[i];
                heat = fight.heat;
                break;
            }
            return at[0] + at[1] + at[2] > 0;
        }

        /// <summary>Who's there, to <paramref name="b"/>: ours first, then each enemy
        /// faction there, in their colors; outnumbered, fighting, or quiet.</summary>
        public static void Append(CharBuf b, int[] at, int us, int heat)
        {
            int ours = us >= 0 && us < 3 ? at[us] : 0;
            int all = at[0] + at[1] + at[2];
            int enemies = all - ours;
            if (all == 0)
            {
                b.Append("quiet");
                return;
            }
            if (ours > 0)
                b.Append("<color=").Append(Names.Hex(us)).Append('>').Append(ours).Append(" of ours</color>");
            if (enemies == 0)
            {
                b.Append(", no enemies");
                return;
            }
            if (ours > 0)
                b.Append(" vs ");
            bool first = true;
            for (int i = 0; i < 3; i++)
            {
                if (i == us || at[i] == 0)
                    continue;
                if (!first)
                    b.Append(", ");
                first = false;
                b.Append("<color=").Append(Names.Hex(i)).Append('>').Append(at[i]).Append(' ').Append(Short[i]).Append("</color>");
            }
            if (ours == 0)
                b.Append(", <color=").Append(Warn).Append(">none of ours</color>");
            else if (enemies > ours)
                b.Append("  ·  <color=").Append(Warn).Append(">OUTNUMBERED</color>");
            if (heat > 0)
                b.Append("  ·  FIGHTING");
        }

        /// <summary>A bar of who's there: a track and a segment a faction (ours first),
        /// each as wide as its share, in its color.</summary>
        public sealed class Bar
        {
            readonly RectTransform track;
            readonly Image[] parts = new Image[3];

            public Bar(Transform parent, float x, float y, float w, float h)
            {
                var t = Widgets.Box(parent, new Color(1f, 1f, 1f, 0.07f), x, y, w, h);
                track = t.rectTransform;
                for (int i = 0; i < 3; i++)
                {
                    parts[i] = UiKit.Box($"Faction {i}", track, Color.white);
                    var r = parts[i].rectTransform;
                    r.offsetMin = r.offsetMax = Vector2.zero;
                }
            }

            public void Show(int[] at, int us)
            {
                int all = at[0] + at[1] + at[2];
                track.gameObject.SetActive(all > 0);
                if (all == 0)
                    return;
                // Ours from the left, then the others.
                float from = 0f;
                for (int k = 0; k < 3; k++)
                {
                    int i = k == 0 ? (us >= 0 && us < 3 ? us : 0) : (us + k) % 3;
                    var r = parts[k].rectTransform;
                    float share = at[i] / (float)all;
                    r.anchorMin = new Vector2(from, 0f);
                    r.anchorMax = new Vector2(from + share, 1f);
                    r.offsetMin = r.offsetMax = Vector2.zero;
                    parts[k].color = Names.Ink(i);
                    from += share;
                }
            }
        }
    }
}
