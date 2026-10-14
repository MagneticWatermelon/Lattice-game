using System;
using System.Globalization;
using Lattice.Diagnostics;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using UnityEngine;

namespace Lattice.Game
{
    /// <summary>
    /// The client's bootstrap: reads the settings and the command line,
    /// builds the local player, the camera rig, the world and the HUD, and
    /// connects. However the game ends, it disconnects at once (the server's
    /// session log closes the session) and appends the report to
    /// lattice-report.txt.
    ///
    /// Self-checks, as the Bevy client's: --autoplay, --autofire, --autoaim, and
    /// --autotarget (aim at the nearest enemy, fire when on it), --hud-test (a scripted
    /// run of hit markers, hurts and kills, for screenshots),
    /// --screenshot PATH --after S (--shots N --shot-every S: a burst), --exit-after S.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class LatticeGame : MonoBehaviour
    {
        public NetSettings Settings = new NetSettings();
        public bool ConnectOnStart = true;

        public static Options Options { get; private set; }
        public FrameStats Frames { get; private set; }

        float started;
        bool reported;
        int shots;
        /// <summary>How many of the --freeze stalls are done.</summary>
        int freezesDone;
        /// <summary>The burst's file names, made at start: a shot mid-fight allocates nothing.</summary>
        string[] shotPaths;
        CameraRig rig;
        ulong lastCorrections;
        /// <summary>When the world was built, and when the first corrections came (s since start).</summary>
        public static float WorldBuiltAt = -1f;
        public static readonly float[] CorrectionTimes = new float[16];
        public static int CorrectionCount;
        Lattice.LocalPlayer.LocalPlayer player;
        Lattice.World.WorldView world;
        bool worldReady;

        /// <summary>The world built at start (<c>--world</c>), held so the plugin keeps it.</summary>
        public static System.Threading.Tasks.Task<Lattice.Native.LtWorldHandle> Prewarmed { get; private set; }

        void Awake()
        {
            // Numbers in logs and reports read the same everywhere ("4.14 s", not "4,14 s").
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Options = Options.Parse(Environment.GetCommandLineArgs());
            (WorldBuiltAt, CorrectionCount) = (-1f, 0);
            if (Options.Errors.Length > 0)
                Debug.LogError($"Lattice: {Options.Errors}");
            Settings.Apply(Options);
            if (!string.IsNullOrEmpty(Options.Profile) && Debug.isDebugBuild)
            {
                // From the first frame: loading and the moment the world appears included.
                UnityEngine.Profiling.Profiler.logFile = System.IO.Path.GetFullPath(Options.Profile);
                UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                UnityEngine.Profiling.Profiler.enabled = true;
            }
            Application.runInBackground = true;
            // The world we expect to play on, built meanwhile (held while we run).
            if (Options.World != 0 && Lattice.Native.LtLibrary.EnsureLoaded())
            {
                ulong world = Options.World;
                Prewarmed = System.Threading.Tasks.Task.Run(() => Lattice.Native.LtWorldHandle.FromSeed(world));
            }
            QualitySettings.vSyncCount = Options.NoVsync ? 0 : 1;
            Application.targetFrameRate = -1;
            Frames = new FrameStats(5f);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 9000f;
            if (!cam.TryGetComponent(out rig))
                rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Camera = cam;

            if (!TryGetComponent(out player))
                player = gameObject.AddComponent<Lattice.LocalPlayer.LocalPlayer>();
            player.Autoplay = Options.Autoplay;
            player.Autofire = Options.Autofire;
            player.Autoaim = Options.Autoaim;
            player.Autotarget = Options.Autotarget;
            player.AutoCrouch = Options.Crouch;
            if (Options.NoAudio)
                AudioListener.volume = 0f;
            player.Ghosts = Options.Ghosts;
            player.Iff = !Options.FactionColors && PlayerPrefs.GetInt(Lattice.LocalPlayer.LocalPlayer.IffPref, 1) != 0;
            player.TierColors = Options.TierColors;
            player.NetGraph = Options.NetGraph || PlayerPrefs.GetInt(Lattice.LocalPlayer.LocalPlayer.NetGraphPref, 0) != 0;
            player.Help = Options.Controls;
            player.Mode = Options.View switch
            {
                "chase" => ViewMode.Chase,
                "spectator" => ViewMode.Spectator,
                _ => ViewMode.FirstPerson,
            };
            if (!string.IsNullOrEmpty(Options.Spectate))
            {
                var v = Options.Spectate.Split(',');
                if (v.Length == 5)
                {
                    float F(int i) => float.Parse(v[i], CultureInfo.InvariantCulture);
                    player.Spectate(new Vector3(F(0), F(1), F(2)), F(3) * Mathf.Deg2Rad, F(4) * Mathf.Deg2Rad);
                }
            }

            var gun = FindAnyObjectByType<Lattice.Combat.Gun>();
            if (gun != null)
            {
                gun.Rig = rig;
                player.Gun = gun;
                var combatHud = gameObject.AddComponent<Lattice.Combat.CombatHud>();
                combatHud.Gun = gun;
                combatHud.Test = Options.HudTest;
            }

            var crowd = FindAnyObjectByType<Lattice.Players.Crowd>();
            if (crowd != null)
            {
                var lp = player;
                crowd.Own = () =>
                {
                    var o = NetDriver.Frame.Own;
                    // Seen from outside in the chase and spectator views, and in the death view.
                    return (new Vector3(o.pos.x, o.pos.y, o.pos.z), lp.Yaw, lp.Pitch, o.grounded == 0, o.dead != 0, lp.Gun != null && lp.Gun.Ads,
                        o.crouched != 0, o.reload > 0f, lp.Mode != ViewMode.FirstPerson || o.dead != 0);
                };
            }

            var hud = UiKit.Canvas("HUD", 10, transform);
            NetGraph.Create(hud.transform, Frames);
            Report.Sections.Add(SessionSection);
            HelpCard.Create(hud.transform);
            CapturePanel.Create(hud.transform);
            world = FindAnyObjectByType<Lattice.World.WorldView>();
            JoinCard.Create(transform, world);
        }

        void Start()
        {
            started = Time.realtimeSinceStartup;
            TurnOff(Options.Off);
            if (Options.RenderScale > 0f && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.renderScale = Mathf.Clamp(Options.RenderScale, 0.1f, 2f);
            if (ConnectOnStart && !NetDriver.Start(Settings, out var error))
                Debug.LogError($"Lattice: couldn't connect to {NetDriver.Address}: {error}");
            if (Options.Screenshot != null && Options.Shots > 0)
            {
                // Relative to where the game was started (a player resolves bare
                // names against its data folder otherwise); a burst numbers them.
                var path = System.IO.Path.GetFullPath(Options.Screenshot);
                shotPaths = new string[Options.Shots];
                for (int i = 0; i < shotPaths.Length; i++)
                    shotPaths[i] = Options.Shots == 1 ? path : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),
                        $"{System.IO.Path.GetFileNameWithoutExtension(path)}_{i}{System.IO.Path.GetExtension(path)}");
                Debug.Log($"Lattice: {shotPaths.Length} screenshot(s) to {shotPaths[0]}, from {Options.After:F1} s every {Options.ShotEvery:F1} s");
            }
        }

        void Update()
        {
            Frames.Record(Time.unscaledDeltaTime, NetDriver.Frame.PluginMs);
            Lattice.Players.SoldierColors.Mode = player.Iff ? Lattice.Players.SoldierColors.Scheme.Iff : Lattice.Players.SoldierColors.Scheme.Faction;
            Lattice.Players.SoldierColors.Tiers = player.TierColors;
            // No control until the world is built (the join card covers it).
            player.Held = world != null && !world.Ready;
            // The session's numbers count from when the world is built (loading is
            // slow by design, and allocates).
            if (!worldReady && world != null && world.Ready)
            {
                worldReady = true;
                WorldBuiltAt = Time.realtimeSinceStartup - started;
                Frames.Restart(2f);
            }
            // When each correction came (the report says; a stall makes them).
            ulong corrections = NetDriver.Frame.Stats.corrections;
            if (corrections > lastCorrections)
            {
                for (ulong c = lastCorrections; c < corrections && CorrectionCount < CorrectionTimes.Length; c++)
                    CorrectionTimes[CorrectionCount++] = Time.realtimeSinceStartup - started;
                lastCorrections = corrections;
            }
            float t = Time.realtimeSinceStartup - started;
            // --freeze: the main thread stalls (no frames, no plugin calls), as a
            // shader compile or a scene load would stall it.
            if (freezesDone < Options.Freezes.Length && t >= Options.Freezes[freezesDone].at)
            {
                int ms = Options.Freezes[freezesDone++].ms;
                Debug.Log($"Lattice: freezing the main thread for {ms} ms at {t:F1} s");
                System.Threading.Thread.Sleep(ms);
            }
            if (shotPaths != null && shots < shotPaths.Length && t >= Options.After + shots * Options.ShotEvery)
                ScreenCapture.CaptureScreenshot(shotPaths[shots++]);
            if (Options.ExitAfter > 0f && t >= Options.ExitAfter)
            {
                Options.ExitAfter = -1f;
                Quit();
            }
        }

        public void Quit()
        {
            WriteReport();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>--off: features turned off, to measure what each costs on the GPU.</summary>
        static void TurnOff(string list)
        {
            if (string.IsNullOrEmpty(list))
                return;
            foreach (var raw in list.Split(','))
            {
                string what = raw.Trim().ToLowerInvariant();
                switch (what)
                {
                    case "grass":
                        Lattice.World.GrassField.Off = true;
                        break;
                    case "shadows":
                        if (RenderSettings.sun != null)
                            RenderSettings.sun.shadows = LightShadows.None;
                        break;
                    case "clouds":
                        if (RenderSettings.sun != null && RenderSettings.sun.TryGetComponent(out Lattice.World.CloudShadows clouds))
                            clouds.enabled = false;
                        break;
                    case "ssao":
                    {
                        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                        var field = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (asset != null && field?.GetValue(asset) is UnityEngine.Rendering.Universal.ScriptableRendererData[] datas)
                            foreach (var d in datas)
                                foreach (var f in d.rendererFeatures)
                                    if (f is UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion)
                                        f.SetActive(false);
                        break;
                    }
                    case "fpview":
                        Lattice.Combat.ViewModelFeature.Off = true;
                        break;
                    case "bloom":
                        foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
                            if (v.sharedProfile != null && v.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
                                bloom.active = false;
                        break;
                    case "cover":
                        Lattice.World.WorldView.NoCover = true;
                        break;
                    case "rocks":
                        Lattice.World.WorldView.NoRocks = true;
                        break;
                    case "day":
                        Lattice.World.WorldView.NoDay = true;
                        break;
                    case "clockwork":
                        Lattice.World.WorldView.NoClockwork = true;
                        break;
                    case "life":
                        Lattice.World.WorldView.NoLife = true;
                        break;
                }
                Debug.Log($"Lattice: off: {what}");
            }
        }


        /// <summary>The report's lines on loading and on corrections' timing.</summary>
        static string SessionSection()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(NetDriver.NetThread ? "  session on the plugin's net thread" : "  session pumped by the frames (--pump)");
            if (Options.Freezes.Length > 0)
            {
                sb.Append("; main thread frozen");
                foreach (var (at, ms) in Options.Freezes)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0} ms at {1:F0} s", ms, at));
            }
            sb.Append('\n');
            // The rifle's rounds: predicted exactly (no corrections), and what the reloads did.
            var gun = FindAnyObjectByType<Lattice.Combat.Gun>();
            if (gun != null && gun.Reloading != null)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  rounds: ammo corrections {0}; reloads {1} ({2} from empty); dry trigger {3}\n",
                    NetDriver.Frame.Stats.ammoCorrections, gun.Reloading.Started, gun.Reloading.StartedEmpty, gun.DryPulls));
            sb.Append(WorldBuiltAt >= 0f
                ? string.Format(CultureInfo.InvariantCulture, "  world built {0:F1} s in (the frame numbers above count from then)\n", WorldBuiltAt)
                : "  world never built\n");
            if (CorrectionCount > 0)
            {
                sb.Append("  corrections at:");
                for (int i = 0; i < CorrectionCount; i++)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F1}s", CorrectionTimes[i]));
                sb.Append(" (since start)\n");
            }
            return sb.ToString();
        }

        void WriteReport()
        {
            if (reported)
                return;
            reported = true;
            var text = Report.Build(Frames, Time.realtimeSinceStartup - started);
            Debug.Log(text);
            var path = Report.Append(text);
            if (path != null)
                Debug.Log($"Lattice: summary appended to {path}");
        }

        void OnApplicationQuit()
        {
            WriteReport();
            NetDriver.Stop();
        }

        void OnDestroy()
        {
            WriteReport();
            NetDriver.Stop();
            Frames?.Dispose();
            Report.Sections.Remove(SessionSection);
            if (Prewarmed != null && Prewarmed.IsCompletedSuccessfully)
                Prewarmed.Result?.Dispose();
            Prewarmed = null;
        }
    }
}
