using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The reward ticker (GDD §8, §9.3): XP as it's earned, beside the reticle
    /// ("+125  KILL  ·  HEADSHOT", "+500  RIFT LEDGE C4 TAKEN"), newest on top,
    /// each fading after a few seconds. Written only when a reward comes.
    /// </summary>
    public sealed class RewardTicker : MonoBehaviour
    {
        const int Lines = 5;
        const float Secs = 3.2f;

        readonly TextMeshProUGUI[] lines = new TextMeshProUGUI[Lines];
        readonly float[] at = new float[Lines];
        string[] names;

        public static RewardTicker Create(Transform parent)
        {
            var root = UiKit.Rect("Rewards", parent, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(70f, -46f), new Vector2(520f, Lines * 30f));
            var t = root.gameObject.AddComponent<RewardTicker>();
            for (int i = 0; i < Lines; i++)
            {
                t.lines[i] = Widgets.Label(root, "", 0f, i * 30f, 520f, 30f, 22f);
                t.lines[i].outlineWidth = 0.2f;
                t.lines[i].outlineColor = new Color32(0, 0, 0, 180);
                t.at[i] = -100f;
            }
            return t;
        }

        void LateUpdate()
        {
            var f = NetDriver.Frame;
            float now = Time.unscaledTime;
            for (int i = 0; i < f.EventCount; i++)
            {
                var e = f.Events[i];
                if (e.kind == LtCode.EventReward)
                    Push(e, f, now);
            }
            for (int i = 0; i < Lines; i++)
            {
                float age = now - at[i];
                bool on = age < Secs;
                if (lines[i].gameObject.activeSelf != on)
                    lines[i].gameObject.SetActive(on);
                if (on)
                    lines[i].alpha = Mathf.Clamp01((Secs - age) / 0.6f) * Mathf.Clamp01(age / 0.08f);
            }
        }

        void Push(in LtEvent e, NetFrame f, float now)
        {
            // Older lines move down one.
            for (int i = Lines - 1; i > 0; i--)
            {
                lines[i].text = lines[i - 1].text;
                at[i] = at[i - 1];
            }
            var ink = Names.Hex(f.Own.faction);
            string what = e.flags switch
            {
                LtCode.RewardKill => e.value > 100 ? "KILL  ·  HEADSHOT" : "KILL",
                LtCode.RewardAssist => "ASSIST",
                LtCode.RewardCapture => $"{SiteName(e.a)} TAKEN",
                LtCode.RewardDefend => $"{SiteName(e.a)} HELD",
                _ => "",
            };
            lines[0].text = $"<color={ink}>+{e.value}</color>  {what}";
            at[0] = now;
        }

        string SiteName(int site)
        {
            if (names == null && NetDriver.World != null)
                names = Names.Sites(NetDriver.World.Sites());
            return names != null && site < names.Length ? names[site] : "SITE";
        }
    }
}
