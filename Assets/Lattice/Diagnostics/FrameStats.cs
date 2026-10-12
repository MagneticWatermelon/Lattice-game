using System;
using Unity.Profiling;
using UnityEngine;

namespace Lattice.Diagnostics
{
    /// <summary>
    /// Frame times and managed allocations, measured every frame without
    /// allocating: a ring of recent frame times (the net graph's sparkline),
    /// a histogram of the whole session (the report's percentiles), the GPU's
    /// and the main and render threads' own times (FrameTimingManager: what a
    /// frame costs when it's faster than the display), and the bytes the
    /// managed heap took per frame (GDD §19: nothing allocates on the managed
    /// heap during a fight).
    /// </summary>
    public sealed class FrameStats : IDisposable
    {
        public const int Recent = 120;
        /// <summary>Histogram buckets: 0.05 ms each, up to 200 ms.</summary>
        const float BucketMs = 0.05f;
        const int Buckets = 4000;

        public readonly float[] RecentMs = new float[Recent];
        public int RecentCount;
        readonly long[] histogram = new long[Buckets + 1];
        public long Frames;
        public float MaxMs;
        /// <summary>When the longest frame ended (seconds after <see cref="Since"/>), and how
        /// many frames took over 20 ms (hitches: a held frame, a stall).</summary>
        public float MaxAt;
        public long Hitches;
        public float Fps, P50, P99;

        /// <summary>Frames (after warm-up) that allocated on the managed heap,
        /// the bytes they took, and the most in one frame.</summary>
        public long AllocFrames, AllocBytes, AllocMax;
        /// <summary>Frames measured for allocations (after warm-up).</summary>
        public long AllocMeasured;
        public long LastFrameAlloc;
        /// <summary>Whether this player can count allocations per frame.</summary>
        public bool AllocAvailable => gcRecorder.Valid || fallback;
        /// <summary>How they're counted: the profiler's counter, or the heap's growth.</summary>
        public string AllocSource => gcRecorder.Valid ? "GC Allocated In Frame" : "heap growth";
        /// <summary>The first allocating frames: (seconds since startup, bytes).</summary>
        public readonly (float at, long bytes)[] AllocLog = new (float, long)[32];

        /// <summary>The plugin's share of the main thread: session histogram, ms.</summary>
        readonly long[] pluginHistogram = new long[Buckets + 1];
        public float PluginMaxMs;

        /// <summary>The GPU's, the main thread's and the render thread's time per
        /// frame (ms, session histograms), when the player measures them.</summary>
        readonly long[] gpuHistogram = new long[Buckets + 1], mainHistogram = new long[Buckets + 1], renderHistogram = new long[Buckets + 1];
        public long TimedFrames;
        /// <summary>The last frame's GPU time (ms), 0 if unknown.</summary>
        public float GpuMs;
        readonly FrameTiming[] timing = new FrameTiming[1];

        ProfilerRecorder gcRecorder;
        readonly bool fallback;
        long lastTotal;
        float warmupUntil;
        float sortedAt = -1f;
        readonly float[] scratch = new float[Recent];

        public FrameStats(float warmupSeconds)
        {
            gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            fallback = !gcRecorder.Valid;
            lastTotal = GC.GetTotalMemory(false);
            warmupUntil = Time.realtimeSinceStartup + warmupSeconds;
        }

        /// <summary>When the session's numbers start: since startup, or since
        /// <see cref="Restart"/> (seconds since startup).</summary>
        public float Since { get; private set; }

        /// <summary>Starts the session's numbers over from now (once the world is
        /// built: loading is slow by design, and allocates), allocations after a
        /// further warm-up.</summary>
        public void Restart(float warmupSeconds)
        {
            Frames = TimedFrames = 0;
            MaxMs = PluginMaxMs = MaxAt = 0f;
            Hitches = 0;
            Array.Clear(histogram, 0, histogram.Length);
            Array.Clear(pluginHistogram, 0, pluginHistogram.Length);
            Array.Clear(gpuHistogram, 0, gpuHistogram.Length);
            Array.Clear(mainHistogram, 0, mainHistogram.Length);
            Array.Clear(renderHistogram, 0, renderHistogram.Length);
            AllocFrames = AllocBytes = AllocMax = AllocMeasured = 0;
            Since = Time.realtimeSinceStartup;
            warmupUntil = Since + warmupSeconds;
        }

        /// <summary>Once per frame, with the last frame's duration.</summary>
        public void Record(float dtSeconds, float pluginMs)
        {
            float ms = dtSeconds * 1000f;
            if (ms <= 0f)
                return;
            Frames++;
            if (ms > MaxMs)
                (MaxMs, MaxAt) = (ms, Time.realtimeSinceStartup - Since);
            if (ms > 20f)
                Hitches++;
            histogram[Mathf.Min((int)(ms / BucketMs), Buckets)]++;
            pluginHistogram[Mathf.Min((int)(pluginMs / BucketMs), Buckets)]++;
            PluginMaxMs = Mathf.Max(PluginMaxMs, pluginMs);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0.0)
            {
                GpuMs = (float)timing[0].gpuFrameTime;
                TimedFrames++;
                gpuHistogram[Mathf.Min((int)(GpuMs / BucketMs), Buckets)]++;
                mainHistogram[Mathf.Min((int)((float)timing[0].cpuMainThreadFrameTime / BucketMs), Buckets)]++;
                renderHistogram[Mathf.Min((int)((float)timing[0].cpuRenderThreadFrameTime / BucketMs), Buckets)]++;
            }
            if (RecentCount < Recent)
                RecentMs[RecentCount++] = ms;
            else
            {
                Array.Copy(RecentMs, 1, RecentMs, 0, Recent - 1);
                RecentMs[Recent - 1] = ms;
            }

            long alloc;
            if (gcRecorder.Valid)
                alloc = gcRecorder.LastValue;
            else
            {
                long total = GC.GetTotalMemory(false);
                alloc = Math.Max(0, total - lastTotal);
                lastTotal = total;
            }
            LastFrameAlloc = alloc;
            if (Time.realtimeSinceStartup >= warmupUntil)
            {
                AllocMeasured++;
                if (alloc > 0)
                {
                    if (AllocFrames < AllocLog.Length)
                        AllocLog[AllocFrames] = (Time.realtimeSinceStartup, alloc);
                    AllocFrames++;
                    AllocBytes += alloc;
                    AllocMax = Math.Max(AllocMax, alloc);
                }
            }

            // Recent percentiles, four times a second.
            float now = Time.realtimeSinceStartup;
            if (now - sortedAt >= 0.25f)
            {
                sortedAt = now;
                Array.Copy(RecentMs, scratch, RecentCount);
                Sort(scratch, RecentCount);
                float sum = 0f;
                for (int i = 0; i < RecentCount; i++)
                    sum += scratch[i];
                P50 = scratch[Mathf.Min(RecentCount - 1, RecentCount / 2)];
                P99 = scratch[Mathf.Min(RecentCount - 1, (int)(RecentCount * 0.99f))];
                Fps = sum > 0f ? RecentCount * 1000f / sum : 0f;
            }
        }

        /// <summary>Insertion sort: Array.Sort makes a comparison delegate per call (an
        /// allocation four times a second), and this is 120 values.</summary>
        static void Sort(float[] a, int n)
        {
            for (int i = 1; i < n; i++)
            {
                float v = a[i];
                int j = i - 1;
                while (j >= 0 && a[j] > v)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = v;
            }
        }

        /// <summary>The session's frame time at quantile <paramref name="q"/> (0..1), ms.</summary>
        public float Quantile(float q) => Quantile(histogram, Frames, q);

        public float PluginQuantile(float q) => Quantile(pluginHistogram, Frames, q);

        public float GpuQuantile(float q) => Quantile(gpuHistogram, TimedFrames, q);

        public float MainQuantile(float q) => Quantile(mainHistogram, TimedFrames, q);

        public float RenderQuantile(float q) => Quantile(renderHistogram, TimedFrames, q);

        static float Quantile(long[] h, long frames, float q)
        {
            if (frames == 0)
                return 0f;
            long want = (long)Math.Ceiling(frames * (double)q);
            long seen = 0;
            for (int i = 0; i <= Buckets; i++)
            {
                seen += h[i];
                if (seen >= want)
                    return (i + 0.5f) * BucketMs;
            }
            return Buckets * BucketMs;
        }

        public void Dispose()
        {
            if (gcRecorder.Valid)
                gcRecorder.Dispose();
        }
    }
}
