using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Game
{
    /// <summary>
    /// What's happening until the game can be played, over everything:
    /// connecting, building the world (with how far along), or why we can't
    /// join. It fades out once the world is built (the world assembling tile by
    /// tile is nothing to watch), and comes back if the connection is lost.
    /// Text is set only when the state changes: nothing is allocated in play.
    /// </summary>
    public sealed class JoinCard : MonoBehaviour
    {
        CanvasGroup group;
        TextMeshProUGUI status, detail;
        RectTransform barTrack, bar;
        Lattice.World.WorldView world;
        int shown = int.MinValue;
        float alpha = 1f;

        const int Hidden = -100, Connecting = -101, Building = -102, WorldDiffers = -103;

        public static JoinCard Create(Transform parent, Lattice.World.WorldView world)
        {
            var canvas = UiKit.Canvas("Join", 20, parent);
            var card = canvas.gameObject.AddComponent<JoinCard>();
            card.world = world;
            card.Build((RectTransform)canvas.transform);
            return card;
        }

        void Build(RectTransform root)
        {
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var bg = UiKit.Box("Backdrop", root, new Color(0.03f, 0.04f, 0.045f, 1f));
            UiKit.Fill(bg.rectTransform);

            var title = UiKit.Text("Title", root, UiKit.Display, 72f, UiKit.Ink, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0f, 70f), new Vector2(900f, 90f));
            title.text = "LATTICE";
            title.characterSpacing = 28f;

            status = UiKit.Text("Status", root, UiKit.Display, 24f, UiKit.Muted, TextAlignmentOptions.Center);
            Place(status.rectTransform, new Vector2(0f, -10f), new Vector2(1100f, 34f));
            status.characterSpacing = 6f;
            UiKit.Warm(status, 80);

            barTrack = UiKit.Rect("Bar", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -46f), new Vector2(360f, 3f));
            var track = UiKit.Box("Track", barTrack, UiKit.Panel);
            UiKit.Fill(track.rectTransform);
            var fill = UiKit.Box("Fill", barTrack, UiKit.Accent);
            bar = fill.rectTransform;
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;

            detail = UiKit.Text("Detail", root, UiKit.Mono, 17f, UiKit.Muted, TextAlignmentOptions.Center);
            Place(detail.rectTransform, new Vector2(0f, -84f), new Vector2(1200f, 60f));
            UiKit.Warm(detail, 160);
        }

        static void Place(RectTransform r, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        void LateUpdate()
        {
            var f = NetDriver.Frame;
            int state = NetDriver.Running ? f.State
                : string.IsNullOrEmpty(NetDriver.LastError) ? LtCode.StateConnected : LtCode.StateError;
            bool failed = state == LtCode.StateDenied || state == LtCode.StateTimedOut || state == LtCode.StateDisconnected || state == LtCode.StateError;
            int key = failed ? state
                : NetDriver.Running && !f.Welcomed ? Connecting
                : f.Stats.worldMismatch != 0 ? WorldDiffers
                : world != null && world.Started && !world.Ready ? Building
                : Hidden;
            if (key != shown)
            {
                shown = key;
                Say(key);
            }
            if (key == Building)
                bar.anchorMax = new Vector2(Mathf.Clamp01(world.Progress), 1f);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            alpha = Mathf.MoveTowards(alpha, key == Hidden ? 0f : 1f, dt / (key == Hidden ? 0.6f : 0.25f));
            group.alpha = alpha;
        }

        void Say(int key)
        {
            barTrack.gameObject.SetActive(key == Building);
            switch (key)
            {
                case Connecting:
                    status.text = "CONNECTING";
                    detail.text = NetDriver.Address;
                    break;
                case Building:
                    status.text = "BUILDING THE WORLD";
                    detail.text = NetDriver.Address;
                    break;
                case WorldDiffers:
                    // We'd mispredict wherever our world and the server's differ.
                    status.text = "THE SERVER'S WORLD DIFFERS FROM OURS";
                    detail.text = $"{NetDriver.Address}: this client and the server were built from different versions; update the client (scripts/unity-plugin.sh)";
                    break;
                case LtCode.StateDenied:
                    status.text = "THE SERVER REFUSED US";
                    detail.text = $"{NetDriver.Address}: its token key or server id differs from ours";
                    break;
                case LtCode.StateTimedOut:
                    status.text = NetDriver.Frame.Welcomed ? "CONNECTION LOST" : "NO ANSWER FROM THE SERVER";
                    detail.text = $"{NetDriver.Address}: nothing heard for 5 s. Is the server running?";
                    break;
                case LtCode.StateDisconnected:
                    status.text = "DISCONNECTED";
                    detail.text = NetDriver.Address;
                    break;
                case LtCode.StateError:
                    status.text = "COULDN'T CONNECT";
                    detail.text = NetDriver.LastError;
                    break;
            }
        }
    }
}
