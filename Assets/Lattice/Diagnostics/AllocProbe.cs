using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;

namespace Lattice.Diagnostics
{
    /// <summary>
    /// Self-check (--alloc-probe): where managed allocations happen. The heap's
    /// used size is read at the start and end of every phase of Unity's player
    /// loop (each of its systems, with --alloc-probe-systems) and around named
    /// regions in our code (<see cref="Begin"/>, <see cref="End"/>); growth is
    /// summed per phase or region and written to the report. The heap grows in
    /// pages, so a region that allocates a little often is charged a page now
    /// and then: over many frames, the sums point at it.
    /// </summary>
    public static class AllocProbe
    {
        public static bool On { get; private set; }
        static readonly Dictionary<string, long> bytes = new Dictionary<string, long>();
        static readonly Dictionary<string, int> times = new Dictionary<string, int>();
        static long phaseStart;

        /// <summary>Wraps each player loop phase (or, with <paramref name="systems"/>, each of their systems).</summary>
        public static void Install(bool systems)
        {
            On = true;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var phases = loop.subSystemList;
            for (int i = 0; i < phases.Length; i++)
            {
                string name = phases[i].type != null ? phases[i].type.Name : "phase" + i;
                var subs = phases[i].subSystemList ?? new PlayerLoopSystem[0];
                var list = new List<PlayerLoopSystem>();
                if (systems)
                {
                    foreach (var s in subs)
                    {
                        string sn = name + "." + (s.type != null ? s.type.Name : "?");
                        list.Add(Mark(() => phaseStart = Profiler.GetMonoUsedSizeLong()));
                        list.Add(s);
                        list.Add(Mark(() => Charge(sn, phaseStart)));
                    }
                }
                else
                {
                    list.Add(Mark(() => phaseStart = Profiler.GetMonoUsedSizeLong()));
                    list.AddRange(subs);
                    list.Add(Mark(() => Charge(name, phaseStart)));
                }
                phases[i].subSystemList = list.ToArray();
            }
            loop.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(loop);
        }

        static PlayerLoopSystem Mark(PlayerLoopSystem.UpdateFunction f) => new PlayerLoopSystem { type = typeof(AllocProbe), updateDelegate = f };

        public static long Begin() => On ? Profiler.GetMonoUsedSizeLong() : 0;

        public static void End(string region, long start)
        {
            if (On)
                Charge(region, start);
        }

        static void Charge(string name, long start)
        {
            long d = Profiler.GetMonoUsedSizeLong() - start;
            if (d <= 0)
                return;
            bytes.TryGetValue(name, out long b);
            bytes[name] = b + d;
            times.TryGetValue(name, out int t);
            times[name] = t + 1;
        }

        public static string Summary()
        {
            if (!On)
                return "";
            var sb = new StringBuilder("  alloc probe (heap growth by phase or region):\n");
            var keys = new List<string>(bytes.Keys);
            keys.Sort((a, b) => bytes[b].CompareTo(bytes[a]));
            foreach (var k in keys)
                sb.Append("    ").Append(k).Append(": ").Append(bytes[k]).Append(" B in ").Append(times[k]).Append(" frames\n");
            return sb.ToString();
        }
    }
}
