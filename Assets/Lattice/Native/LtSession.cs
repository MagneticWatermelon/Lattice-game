using System;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Lattice.Native
{
    /// <summary>
    /// One connection to a continent server, through the plugin: a thin, safe
    /// face over the lt_* functions. Calls take the caller's buffers
    /// (NativeArray) and allocate nothing, except connecting and the report.
    /// One thread at a time (pump mode: the main thread).
    /// </summary>
    public sealed unsafe class LtSession : IDisposable
    {
        IntPtr handle;

        public bool IsOpen => handle != IntPtr.Zero;

        LtSession(IntPtr h)
        {
            handle = h;
            LtLibrary.OpenSessions++;
        }

        /// <summary>Connects with a dev connect token for <paramref name="user"/>,
        /// minted with the server's key (null: the public dev key).</summary>
        public static LtSession ConnectDev(string address, ulong user, string keyHex, ulong serverId, LtConfig config, out string error)
        {
            error = null;
            if (!LtLibrary.EnsureLoaded())
            {
                error = LtLibrary.Error;
                return null;
            }
            fixed (byte* addr = Utf8(address))
            fixed (byte* key = keyHex == null ? null : Utf8(keyHex))
            {
                var h = LtLibrary.Api.lt_connect_dev(addr, user, key, serverId, &config);
                if (h == IntPtr.Zero)
                {
                    error = LtLibrary.LastPluginError();
                    return null;
                }
                return new LtSession(h);
            }
        }

        /// <summary>Connects with a playtest invite (spends one of its tokens).</summary>
        public static LtSession ConnectInvite(string path, LtConfig config, out string error)
        {
            error = null;
            if (!LtLibrary.EnsureLoaded())
            {
                error = LtLibrary.Error;
                return null;
            }
            fixed (byte* p = Utf8(path))
            {
                var h = LtLibrary.Api.lt_connect_invite(p, &config);
                if (h == IntPtr.Zero)
                {
                    error = LtLibrary.LastPluginError();
                    return null;
                }
                return new LtSession(h);
            }
        }

        static byte[] Utf8(string s)
        {
            var n = Encoding.UTF8.GetByteCount(s);
            var b = new byte[n + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
            return b;
        }

        /// <summary>Reads the plugin's clock once for this frame; returns seconds since connecting.</summary>
        public double BeginFrame() => LtLibrary.Api.lt_begin_frame(handle);

        public int Poll() => LtLibrary.Api.lt_poll(handle);

        public int State => handle == IntPtr.Zero ? LtCode.StateError : LtLibrary.Api.lt_state(handle);

        /// <summary>Pulls the trigger; true if a shot fired (it rides this frame's input).</summary>
        public bool Fire(in LtFire fire, out LtShot shot)
        {
            fixed (LtFire* f = &fire)
            fixed (LtShot* s = &shot)
                return LtLibrary.Api.lt_fire(handle, f, s) == 1;
        }

        public void Inputs(in LtIntent intent)
        {
            fixed (LtIntent* i = &intent)
                LtLibrary.Api.lt_inputs(handle, i);
        }

        public void Own(bool ads, out LtOwn own)
        {
            fixed (LtOwn* o = &own)
                LtLibrary.Api.lt_own(handle, (byte)(ads ? 1 : 0), o);
        }

        /// <summary>Draws everyone at this frame's render step into <paramref name="buffer"/>;
        /// returns how many there are (more than its length: grow it).</summary>
        public int Render(NativeArray<LtEntity> buffer) =>
            LtLibrary.Api.lt_render(handle, (LtEntity*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);

        public int Ghosts(NativeArray<LtEntity> buffer) =>
            LtLibrary.Api.lt_ghosts(handle, (LtEntity*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);

        public void Combat() => LtLibrary.Api.lt_combat(handle);

        public int Events(NativeArray<LtEvent> buffer) =>
            LtLibrary.Api.lt_events(handle, (LtEvent*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);

        public int Fx(NativeArray<LtFx> buffer) =>
            LtLibrary.Api.lt_fx(handle, (LtFx*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);

        public int Tracers(NativeArray<LtTracer> buffer) =>
            LtLibrary.Api.lt_tracers(handle, (LtTracer*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer), buffer.Length);

        public void SetOthersMuzzle(LtVec3 offset) => LtLibrary.Api.lt_set_others_muzzle(handle, &offset);

        public void Flush() => LtLibrary.Api.lt_flush(handle);

        public void Stats(out LtStats stats)
        {
            fixed (LtStats* s = &stats)
                LtLibrary.Api.lt_stats(handle, s);
        }

        /// <summary>The last snapshot arrival gaps (ms, oldest first) into <paramref name="gaps"/>; returns how many.</summary>
        public int SnapshotGaps(float[] gaps)
        {
            fixed (float* g = gaps)
                return LtLibrary.Api.lt_snapshot_gaps(handle, g, gaps.Length);
        }

        /// <summary>The report's connection part (allocates: for exit only).</summary>
        public string Summary()
        {
            int n = LtLibrary.Api.lt_summary(handle, null, 0);
            if (n <= 0)
                return "";
            var buf = new byte[n + 1];
            fixed (byte* b = buf)
                LtLibrary.Api.lt_summary(handle, b, buf.Length);
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        /// <summary>The session's world once welcomed, else null.</summary>
        public LtWorldHandle World()
        {
            var w = LtLibrary.Api.lt_session_world(handle);
            return w == IntPtr.Zero ? null : new LtWorldHandle(w);
        }

        /// <summary>Disconnects (the server hears at once) and frees the session.</summary>
        public void Dispose()
        {
            if (handle == IntPtr.Zero)
                return;
            if (LtLibrary.Loaded)
                LtLibrary.Api.lt_close(handle);
            handle = IntPtr.Zero;
            LtLibrary.OpenSessions--;
        }
    }

    /// <summary>A world (terrain, cover, sites), shared with the plugin.</summary>
    public sealed unsafe class LtWorldHandle : IDisposable
    {
        IntPtr handle;

        internal LtWorldHandle(IntPtr h)
        {
            handle = h;
        }

        /// <summary>The world built from <paramref name="seed"/> (as the server's Welcome names it).</summary>
        public static LtWorldHandle FromSeed(ulong seed)
        {
            if (!LtLibrary.EnsureLoaded())
                return null;
            return new LtWorldHandle(LtLibrary.Api.lt_world_new(seed));
        }

        public LtWorldInfo Info
        {
            get
            {
                LtWorldInfo info;
                LtLibrary.Api.lt_world_info(handle, &info);
                return info;
            }
        }

        /// <summary>Heights (m) of nx × ny samples from sample (x0, y0), rows north.</summary>
        public void Heights(int x0, int y0, int nx, int ny, float[] output)
        {
            if (output.Length < nx * ny)
                throw new ArgumentException("output too small");
            fixed (float* o = output)
                LtLibrary.Api.lt_world_heights(handle, x0, y0, nx, ny, o);
        }

        /// <summary>Heights (m) of nx × ny samples from sample (x0, y0) into native memory.</summary>
        public void Heights(int x0, int y0, int nx, int ny, float* output) =>
            LtLibrary.Api.lt_world_heights(handle, x0, y0, nx, ny, output);

        public LtBox[] Boxes()
        {
            int n = LtLibrary.Api.lt_world_boxes(handle, null, 0);
            var boxes = new LtBox[n];
            fixed (LtBox* b = boxes)
                LtLibrary.Api.lt_world_boxes(handle, b, n);
            return boxes;
        }

        public LtSite[] Sites()
        {
            int n = LtLibrary.Api.lt_world_sites(handle, null, 0);
            var sites = new LtSite[n];
            fixed (LtSite* s = sites)
                LtLibrary.Api.lt_world_sites(handle, s, n);
            return sites;
        }

        /// <summary>The terrain's height at (x, y), as movement samples it.</summary>
        public float Height(float x, float y) => LtLibrary.Api.lt_world_height(handle, x, y);

        public float Ground(float x, float y, float z, float reach) => LtLibrary.Api.lt_world_ground(handle, x, y, z, reach);

        /// <summary>Where the segment from → to first hits the ground or cover (game space).</summary>
        public bool Raycast(LtVec3 from, LtVec3 to, out LtHit hit)
        {
            fixed (LtHit* h = &hit)
                return LtLibrary.Api.lt_world_raycast(handle, &from, &to, h) == 1;
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
                return;
            if (LtLibrary.Loaded)
                LtLibrary.Api.lt_world_free(handle);
            handle = IntPtr.Zero;
        }
    }
}
