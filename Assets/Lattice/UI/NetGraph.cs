using Lattice.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using TMPro;
using UnityEngine;

namespace Lattice.UI
{
    /// <summary>
    /// The net graph (N), as the Bevy client's: frame times, the connection,
    /// the server, the render timeline, smoothness per tier, corrections, and
    /// sparklines of frame time and snapshot arrival gaps. Text four times a
    /// second, built in a reused buffer (no allocations).
    /// </summary>
    public sealed class NetGraph : MonoBehaviour
    {
        public FrameStats Frames;

        TextMeshProUGUI text;
        Sparkline frameLine, gapLine;
        RectTransform panel;
        readonly CharBuf buf = new CharBuf(2048);
        readonly float[] gaps = new float[120];
        float textAt = -1f, rateAt;
        ulong bytesIn, bytesOut;
        readonly ulong[] lastFrames = new ulong[12];
        float downKbps, upKbps;
        readonly float[] interp = { 1f, 1f, 1f };

        public static NetGraph Create(Transform canvas, FrameStats frames)
        {
            var root = UiKit.Rect("Net graph", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(640f, 300f));
            var g = root.gameObject.AddComponent<NetGraph>();
            g.Frames = frames;
            g.panel = root;
            var bg = UiKit.Box("Panel", root, UiKit.Panel);
            UiKit.Fill(bg.rectTransform);
            g.text = UiKit.Text("Text", root, UiKit.Mono, 15f, UiKit.Ink);
            UiKit.Fill(g.text.rectTransform, 10f);
            g.text.lineSpacing = -6f;
            var fl = UiKit.Text("Frame label", root, UiKit.Mono, 12f, UiKit.Muted);
            fl.rectTransform.anchorMin = fl.rectTransform.anchorMax = new Vector2(0f, 0f);
            fl.rectTransform.pivot = new Vector2(0f, 0f);
            fl.rectTransform.anchoredPosition = new Vector2(10f, 78f);
            fl.rectTransform.sizeDelta = new Vector2(400f, 16f);
            fl.text = "frame time (0-33 ms)";
            g.frameLine = Line(root, new Vector2(10f, 50f), UiKit.Good, 33.3f, 1000f / 60f);
            var gl = UiKit.Text("Gap label", root, UiKit.Mono, 12f, UiKit.Muted);
            gl.rectTransform.anchorMin = gl.rectTransform.anchorMax = new Vector2(0f, 0f);
            gl.rectTransform.pivot = new Vector2(0f, 0f);
            gl.rectTransform.anchoredPosition = new Vector2(285f, 78f);
            gl.rectTransform.sizeDelta = new Vector2(260f, 16f);
            gl.text = "snapshot gaps (0-100 ms)";
            g.gapLine = Line(root, new Vector2(285f, 50f), UiKit.Accent, 100f, 70f);
            return g;
        }

        static Sparkline Line(RectTransform parent, Vector2 pos, Color color, float max, float warn)
        {
            var r = UiKit.Rect("Sparkline", parent, new Vector2(0f, 0f), new Vector2(0f, 0f), pos - new Vector2(0f, 40f), new Vector2(265f, 64f));
            var s = r.gameObject.AddComponent<Sparkline>();
            s.color = color;
            s.Max = max;
            s.Warn = warn;
            s.raycastTarget = false;
            return s;
        }

        void LateUpdate()
        {
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            bool show = p == null || p.NetGraph;
            if (panel.gameObject.activeSelf != show)
                panel.gameObject.SetActive(show);
            if (!show)
                return;
            var f = NetDriver.Frame;
            var s = NetDriver.Session;
            frameLine.SetData(Frames.RecentMs, Frames.RecentCount);
            if (s != null && s.IsOpen)
                gapLine.SetData(gaps, s.SnapshotGaps(gaps));

            float now = Time.realtimeSinceStartup;
            if (now - textAt < 0.25f)
                return;
            textAt = now;
            ref var st = ref f.Stats;
            if (now - rateAt >= 1f)
            {
                float secs = rateAt <= 0f ? 1f : now - rateAt;
                downKbps = (st.bytesReceived - System.Math.Min(bytesIn, st.bytesReceived)) * 8f / 1000f / secs;
                upKbps = (st.bytesSent - System.Math.Min(bytesOut, st.bytesSent)) * 8f / 1000f / secs;
                (bytesIn, bytesOut) = (st.bytesReceived, st.bytesSent);
                unsafe
                {
                    fixed (ulong* fr = st.frames)
                    {
                        for (int t = 0; t < 3; t++)
                        {
                            ulong total = 0;
                            for (int h = 0; h < 4; h++)
                                total += fr[t * 4 + h] - System.Math.Min(lastFrames[t * 4 + h], fr[t * 4 + h]);
                            if (total > 0)
                                interp[t] = (fr[t * 4] - System.Math.Min(lastFrames[t * 4], fr[t * 4])) / (float)total;
                        }
                        for (int i = 0; i < 12; i++)
                            lastFrames[i] = fr[i];
                    }
                }
                rateAt = now;
            }

            var b = buf.Clear();
            b.Append("<color=#8EA5FF>NET</color>  ").Append(Frames.Fps, 0).Append(" fps  frame p50 ").Append(Frames.P50, 1)
                .Append(" p99 ").Append(Frames.P99, 1).Append(" ms  plugin ").Append(f.PluginMs, 2).Append(" ms\n");
            b.Append(State(f.State)).Append("  ").Append(NetDriver.Address).Append('\n');
            b.Append("rtt ").Append(st.rttMs, 1).Append(" ms  loss ").Append(st.loss * 100f, 2).Append("%  down ")
                .Append(downKbps, 0).Append(" up ").Append(upKbps, 0).Append(" kbps\n");
            b.Append("server step ").Append(st.serverStep).Append("  level ").Append(st.level).Append("  pace ")
                .Append(st.pace / 1000f, 2).Append("  bandwidth level ").Append(st.clientLevel).Append('\n');
            b.Append("input > applied ~").Append(st.rttMs / 2f + st.waitMs, 0).Append(" ms (rtt/2 + server wait ").Append(st.waitMs, 0).Append(")\n");
            b.Append("render delay near ").Append(st.renderDelayMs, 0).Append(" ms, mid/far +").Append(st.midLagMs, 0)
                .Append(" ms  clock snaps ").Append(st.clockSnaps).Append('\n');
            unsafe
            {
                b.Append("drawn near ").Append(st.drawn[0]).Append(" mid ").Append(st.drawn[1]).Append(" far ").Append(st.drawn[2])
                    .Append("  interpolated ").Append(interp[0] * 100f, 1).Append("% / ").Append(interp[1] * 100f, 1).Append("% / ")
                    .Append(interp[2] * 100f, 1).Append("%\n");
            }
            b.Append(st.corrections > 0 ? "<color=#E2814F>corrections " : "corrections ").Append(st.corrections)
                .Append(" (largest ").Append(st.correctionErrorMax, 3).Append(" m)").Append(st.corrections > 0 ? "</color>" : "")
                .Append("  push ").Append(st.pushCorrections).Append("  resyncs ").Append(st.resyncs)
                .Append("  deaths/respawns ").Append(st.lifeEvents).Append('\n');
            b.Append("others' shots ").Append(st.shotsSeen).Append("  distant: ").Append(st.activityCells).Append(" cells, ")
                .Append(st.activityShots).Append(" shots, ").Append(st.ambient).Append(" ambient\n");
            b.Append("alloc this frame ").Append(Frames.LastFrameAlloc).Append(" B  frames allocating ").Append(Frames.AllocFrames)
                .Append(" of ").Append(Frames.AllocMeasured);
            text.SetCharArray(b.Chars, 0, b.Length);
        }

        static string State(int s) => s switch
        {
            LtCode.StateConnected => "<color=#5CD98C>connected</color>",
            LtCode.StateConnecting => "connecting",
            LtCode.StateDenied => "<color=#E2814F>DENIED (token key or server id?)</color>",
            LtCode.StateTimedOut => "<color=#E2814F>TIMED OUT</color>",
            LtCode.StateDisconnected => "disconnected",
            _ => "<color=#E2814F>no session</color>",
        };
    }
}
