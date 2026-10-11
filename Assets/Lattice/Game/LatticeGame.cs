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
        /// <summary>The burst's file names, made at start: a shot mid-fight allocates nothing.</summary>
        string[] shotPaths;
        CameraRig rig;
        Lattice.LocalPlayer.LocalPlayer player;
        Lattice.World.WorldView world;
        bool worldReady;

        void Awake()
        {
            // Numbers in logs and reports read the same everywhere ("4.14 s", not "4,14 s").
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Options = Options.Parse(Environment.GetCommandLineArgs());
            if (Options.Errors.Length > 0)
                Debug.LogError($"Lattice: {Options.Errors}");
            Settings.Apply(Options);
            Application.runInBackground = true;
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
            if (Options.NoAudio)
                AudioListener.volume = 0f;
            player.Ghosts = Options.Ghosts;
            player.Iff = !Options.FactionColors && PlayerPrefs.GetInt(Lattice.LocalPlayer.LocalPlayer.IffPref, 1) != 0;
            player.TierColors = Options.TierColors;
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
                    return (new Vector3(o.pos.x, o.pos.y, o.pos.z), lp.Yaw, lp.Pitch, o.grounded == 0, o.dead != 0, lp.Gun != null && lp.Gun.Ads, lp.Mode != ViewMode.FirstPerson || o.dead != 0);
                };
            }

            var hud = UiKit.Canvas("HUD", 10, transform);
            NetGraph.Create(hud.transform, Frames);
            HelpCard.Create(hud.transform);
            world = FindAnyObjectByType<Lattice.World.WorldView>();
        }

        void Start()
        {
            started = Time.realtimeSinceStartup;
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
            // Allocations count from when the world is built (loading allocates).
            if (!worldReady && world != null && world.Ready)
            {
                worldReady = true;
                Frames.RestartAllocations(2f);
            }
            float t = Time.realtimeSinceStartup - started;
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
        }
    }
}
