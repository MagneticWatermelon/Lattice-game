using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using Debug = UnityEngine.Debug;

namespace Lattice.Net
{
    /// <summary>What reads the player's controls: called each frame, in Update's
    /// first system, to pull the trigger (<see cref="LtSession.Fire"/>) and fill
    /// this frame's intent before it goes to the input clock.</summary>
    public interface IInputSource
    {
        void Intent(LtSession session, ref LtIntent intent);
    }

    /// <summary>This frame's view of the game, filled before LateUpdate.</summary>
    public sealed class NetFrame
    {
        /// <summary>The plugin's clock: seconds since connecting.</summary>
        public double Time;
        public int State = LtCode.StateConnecting;
        public LtOwn Own;
        /// <summary>Everyone else, as drawn this frame (lt_render).</summary>
        public NativeArray<LtEntity> Entities;
        public int EntityCount;
        /// <summary>This frame's combat news, effects and tracers.</summary>
        public NativeArray<LtEvent> Events;
        public int EventCount;
        public NativeArray<LtFx> Fx;
        public int FxCount;
        public NativeArray<LtTracer> Tracers;
        public int TracerCount;
        /// <summary>Every site's state (lt_territory): who holds it, its fight.</summary>
        public NativeArray<LtSiteState> Sites;
        public int SiteCount;
        public LtStats Stats;
        /// <summary>Kiln's clock (lt_clock, K4): the day and the clockwork, from the game step.</summary>
        public LtClock Clock;
        /// <summary>The shell (lt_shell, U4): deploying, recaps, fights, XP.</summary>
        public LtShell Shell;
        /// <summary>Every site as a spawn for us (lt_spawn_options), in the sites' order.</summary>
        public NativeArray<LtSpawnOption> Spawns;
        public int SpawnCount;
        /// <summary>The continent's fights (lt_fights), as the server last said.</summary>
        public NativeArray<LtFight> Fights;
        public int FightCount;
        /// <summary>The newest death recap, and how many have come (0: none).</summary>
        public LtRecap Recap;
        public int Recaps;
        /// <summary>Down the sights (the reticle's cone follows): set by the local player.</summary>
        public bool Ads;
        /// <summary>Time spent in plugin calls this frame, ms.</summary>
        public float PluginMs;
        public bool Welcomed => Own.welcomed != 0;
    }

    /// <summary>
    /// The session and the frame order of GDD §13, inserted into Unity's
    /// PlayerLoop:
    ///
    /// EarlyUpdate (end)        lt_begin_frame, lt_poll
    /// Update (first)           the input source: trigger (lt_fire), intent; lt_inputs
    /// PreLateUpdate (first)    lt_own, lt_render, lt_combat and the drains, lt_territory → <see cref="Frame"/>
    /// PostLateUpdate (first)   lt_flush
    ///
    /// lt_render runs exactly once per frame and is the only source of other
    /// players' positions. Unity's Time never reaches the netcode: the plugin's
    /// clock, read once per frame, is the only "now".
    /// </summary>
    public static class NetDriver
    {
        public static LtSession Session { get; private set; }
        public static NetFrame Frame { get; private set; } = new NetFrame();
        public static IInputSource Input;
        /// <summary>The world, once welcomed.</summary>
        public static LtWorldHandle World { get; private set; }
        /// <summary>Raised once, when the server's Welcome has come and the world is known.</summary>
        public static event Action<LtWorldHandle> Welcomed;
        /// <summary>Raised each frame once <see cref="Frame"/> is filled, before
        /// animation runs: views place what they draw here.</summary>
        public static event Action FrameReady;
        /// <summary>Where we connected, for the HUD and the report.</summary>
        public static string Address { get; private set; } = "";
        /// <summary>The session runs on the plugin's net thread (see NetSettings.netThread).</summary>
        public static bool NetThread { get; private set; }
        public static string LastError { get; private set; } = "";

        struct LatticeEarly { }
        struct LatticeInput { }
        struct LatticeLate { }
        struct LatticeFlush { }

        static readonly Type[] Ours = { typeof(LatticeEarly), typeof(LatticeInput), typeof(LatticeLate), typeof(LatticeFlush) };
        static long frameTicks;
        static float lastYaw, lastPitch;
        static int wantEntities;

        public static bool Running => Session != null;

        /// <summary>Connects and starts running the frame order.</summary>
        public static bool Start(NetSettings settings, out string error)
        {
            Stop();
            var cfg = new LtConfig
            {
                nearDelayMs = settings.nearMs,
                nearDelayMaxMs = settings.nearMaxMs,
                midDelayMs = settings.midMs,
                flags = settings.netThread ? (uint)LtCode.ConfigNetThread : 0u,
            };
            NetThread = settings.netThread;
            LtSession s;
            if (!string.IsNullOrEmpty(settings.invite))
            {
                s = LtSession.ConnectInvite(settings.invite, cfg, out error);
                Address = $"invite {settings.invite}";
            }
            else
            {
                Address = settings.ResolveServer();
                var key = string.IsNullOrWhiteSpace(settings.tokenKey) ? null : settings.tokenKey.Trim();
                s = LtSession.ConnectDev(Address, settings.ResolveUser(), key, settings.serverId, cfg, out error);
            }
            if (s == null)
            {
                LastError = error;
                return false;
            }
            Session = s;
            wantEntities = 0;
            Frame = new NetFrame
            {
                Entities = new NativeArray<LtEntity>(4096, Allocator.Persistent),
                Events = new NativeArray<LtEvent>(256, Allocator.Persistent),
                Fx = new NativeArray<LtFx>(4096, Allocator.Persistent),
                Tracers = new NativeArray<LtTracer>(LtConst.MaxTracers, Allocator.Persistent),
                Sites = new NativeArray<LtSiteState>(256, Allocator.Persistent),
                Spawns = new NativeArray<LtSpawnOption>(256, Allocator.Persistent),
                Fights = new NativeArray<LtFight>(256, Allocator.Persistent),
            };
            Install();
            error = "";
            return true;
        }

        /// <summary>Disconnects (the server hears at once) and leaves the PlayerLoop.</summary>
        public static void Stop()
        {
            Uninstall();
            World?.Dispose();
            World = null;
            Session?.Dispose();
            Session = null;
            var f = Frame;
            if (f.Entities.IsCreated) f.Entities.Dispose();
            if (f.Events.IsCreated) f.Events.Dispose();
            if (f.Fx.IsCreated) f.Fx.Dispose();
            if (f.Tracers.IsCreated) f.Tracers.Dispose();
            if (f.Sites.IsCreated) f.Sites.Dispose();
            if (f.Spawns.IsCreated) f.Spawns.Dispose();
            if (f.Fights.IsCreated) f.Fights.Dispose();
            Frame = new NetFrame();
        }

        /// <summary>Asks to deploy at <paramref name="site"/> (U4); false if not asked.</summary>
        public static bool Deploy(int site, int kit, int weapon) => Session != null && Session.Deploy(site, kit, weapon);

        static void EarlyUpdate()
        {
            var s = Session;
            if (s == null)
                return;
            long t0 = Stopwatch.GetTimestamp();
            frameTicks = 0;
            Frame.Time = s.BeginFrame();
            Frame.State = s.Poll();
            frameTicks += Stopwatch.GetTimestamp() - t0;
        }

        static void UpdateFirst()
        {
            var s = Session;
            if (s == null)
                return;
            long t0 = Stopwatch.GetTimestamp();
            var intent = new LtIntent { yaw = lastYaw, pitch = lastPitch };
            try
            {
                Input?.Intent(s, ref intent);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            (lastYaw, lastPitch) = (intent.yaw, intent.pitch);
            s.Inputs(intent);
            frameTicks += Stopwatch.GetTimestamp() - t0;
        }

        static void PreLate()
        {
            var s = Session;
            if (s == null)
                return;
            long t0 = Stopwatch.GetTimestamp();
            var f = Frame;
            if (wantEntities > f.Entities.Length)
            {
                // More players than the buffer held last frame: grow it (rare).
                f.Entities.Dispose();
                f.Entities = new NativeArray<LtEntity>(Mathf.NextPowerOfTwo(wantEntities + 64), Allocator.Persistent);
            }
            s.Own(f.Ads, out f.Own);
            int n = s.Render(f.Entities);
            wantEntities = n;
            f.EntityCount = Math.Min(n, f.Entities.Length);
            s.Combat();
            f.EventCount = s.Events(f.Events);
            f.FxCount = s.Fx(f.Fx);
            f.TracerCount = Math.Min(s.Tracers(f.Tracers), f.Tracers.Length);
            f.SiteCount = Math.Min(s.Territory(f.Sites), f.Sites.Length);
            s.Stats(out f.Stats);
            s.Clock(out f.Clock);
            s.Shell(out f.Shell);
            f.SpawnCount = Math.Min(s.SpawnOptions(f.Spawns), f.Spawns.Length);
            f.FightCount = Math.Min(s.Fights(f.Fights), f.Fights.Length);
            if (f.Shell.recaps != f.Recaps)
                f.Recaps = s.Recap(ref f.Recap);
            if (World == null && f.Own.welcomed != 0)
            {
                World = s.World();
                if (World != null)
                {
                    try
                    {
                        Welcomed?.Invoke(World);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            frameTicks += Stopwatch.GetTimestamp() - t0;
            try
            {
                FrameReady?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void PostLate()
        {
            var s = Session;
            if (s == null)
                return;
            long t0 = Stopwatch.GetTimestamp();
            s.Flush();
            frameTicks += Stopwatch.GetTimestamp() - t0;
            Frame.PluginMs = (float)(frameTicks * 1000.0 / Stopwatch.Frequency);
        }

        // The PlayerLoop.

        static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            Insert(ref loop, typeof(UnityEngine.PlayerLoop.EarlyUpdate), new PlayerLoopSystem { type = typeof(LatticeEarly), updateDelegate = EarlyUpdate }, false);
            Insert(ref loop, typeof(Update), new PlayerLoopSystem { type = typeof(LatticeInput), updateDelegate = UpdateFirst }, true);
            Insert(ref loop, typeof(PreLateUpdate), new PlayerLoopSystem { type = typeof(LatticeLate), updateDelegate = PreLate }, true);
            Insert(ref loop, typeof(PostLateUpdate), new PlayerLoopSystem { type = typeof(LatticeFlush), updateDelegate = PostLate }, true);
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void Uninstall()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Remove(ref loop))
                PlayerLoop.SetPlayerLoop(loop);
        }

        static void Insert(ref PlayerLoopSystem loop, Type phase, PlayerLoopSystem system, bool first)
        {
            var subs = loop.subSystemList;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type != phase)
                    continue;
                var list = new List<PlayerLoopSystem>(subs[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                if (first)
                    list.Insert(0, system);
                else
                    list.Add(system);
                subs[i].subSystemList = list.ToArray();
                return;
            }
            Debug.LogError($"Lattice: no {phase.Name} phase in the PlayerLoop");
        }

        /// <summary>Takes our systems (by name, so a stale domain's go too) out of the loop.</summary>
        static bool Remove(ref PlayerLoopSystem loop)
        {
            bool removed = false;
            var subs = loop.subSystemList;
            if (subs == null)
                return false;
            for (int i = 0; i < subs.Length; i++)
            {
                var inner = subs[i].subSystemList;
                if (inner == null)
                    continue;
                var kept = new List<PlayerLoopSystem>(inner.Length);
                foreach (var sys in inner)
                {
                    if (sys.type != null && sys.type.FullName != null && sys.type.FullName.StartsWith("Lattice.Net.NetDriver+Lattice"))
                        removed = true;
                    else
                        kept.Add(sys);
                }
                if (kept.Count != inner.Length)
                    subs[i].subSystemList = kept.ToArray();
            }
            return removed;
        }

        /// <summary>A stale domain's systems (the editor, after a script reload) are dropped at once.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Session = null;
            World = null;
            Input = null;
            Welcomed = null;
            FrameReady = null;
            Frame = new NetFrame();
            Uninstall();
        }
    }
}
