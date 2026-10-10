using System;
using System.Globalization;
using System.IO;
using System.Text;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;

namespace Lattice.Diagnostics
{
    /// <summary>
    /// The summary a playtester sends back: appended to lattice-report.txt in
    /// the folder the game was started in, however it ends (the same file and
    /// shape as the Bevy client's, with this client's frame and allocation
    /// numbers on top).
    /// </summary>
    public static class Report
    {
        public const string FileName = "lattice-report.txt";

        public static string Build(FrameStats frames, float playedSeconds)
        {
            var sb = new StringBuilder();
            var utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            sb.Append($"== {utc} UTC | Unity client {Application.version} on {NetDriver.Address} | played {playedSeconds:F0} s ==\n");
            sb.Append("== client summary ==\n");
            var ci = CultureInfo.InvariantCulture;
            sb.Append(string.Format(ci, "  frames {0} | frame time p50 {1:F1} p99 {2:F1} max {3:F1} ms | plugin calls p50 {4:F2} p99 {5:F2} max {6:F2} ms per frame\n",
                frames.Frames, frames.Quantile(0.5f), frames.Quantile(0.99f), frames.MaxMs,
                frames.PluginQuantile(0.5f), frames.PluginQuantile(0.99f), frames.PluginMaxMs));
            if (frames.AllocAvailable)
            {
                sb.Append(string.Format(ci, "  managed allocations ({4}): {0} of {1} frames measured allocated ({2} B in all, at most {3} B in one)\n",
                    frames.AllocFrames, frames.AllocMeasured, frames.AllocBytes, frames.AllocMax, frames.AllocSource));
                if (frames.AllocFrames > 0)
                {
                    sb.Append("    first:");
                    for (long i = 0; i < Math.Min(frames.AllocFrames, frames.AllocLog.Length); i++)
                        sb.Append(string.Format(ci, " {0:F1}s/{1}B", frames.AllocLog[i].at, frames.AllocLog[i].bytes));
                    sb.Append('\n');
                }
            }
            else
                sb.Append("  managed allocations: not measurable in this build\n");
            var s = NetDriver.Session;
            if (s != null && s.IsOpen)
                sb.Append("  ").Append(s.Summary().Replace("\n  ", "\n  "));
            else
                sb.Append("  not connected").Append(string.IsNullOrEmpty(NetDriver.LastError) ? "" : $": {NetDriver.LastError}").Append('\n');
            return sb.ToString();
        }

        /// <summary>Appends <paramref name="text"/> to the report file; returns its path, or null.</summary>
        public static string Append(string text)
        {
            var path = Path.GetFullPath(FileName);
            try
            {
                File.AppendAllText(path, text + "\n");
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Lattice: couldn't write {path}: {e.Message}");
                return null;
            }
        }
    }
}
