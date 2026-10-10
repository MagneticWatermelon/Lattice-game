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
    /// Self-checks, as the Bevy client's: --autoplay, --autofire, --autoaim,
    /// --screenshot PATH --after S, --exit-after S.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class LatticeGame : MonoBehaviour
    {
        public NetSettings Settings = new NetSettings();
        public bool ConnectOnStart = true;

        public static Options Options { get; private set; }
        public FrameStats Frames { get; private set; }

        float started;
        bool reported, shot;
        CameraRig rig;

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

            if (!TryGetComponent(out Lattice.LocalPlayer.LocalPlayer player))
                player = gameObject.AddComponent<Lattice.LocalPlayer.LocalPlayer>();
            player.Autoplay = Options.Autoplay;
            player.Autofire = Options.Autofire;
            player.Autoaim = Options.Autoaim;
            player.Ghosts = Options.Ghosts;
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

            var crowd = FindAnyObjectByType<Lattice.Players.Crowd>();
            if (crowd != null)
            {
                var lp = player;
                crowd.Own = () =>
                {
                    var o = NetDriver.Frame.Own;
                    return (new Vector3(o.pos.x, o.pos.y, o.pos.z), lp.Yaw, lp.Pitch, o.grounded == 0, o.dead != 0, lp.Gun != null && lp.Gun.Ads, lp.Mode != ViewMode.FirstPerson);
                };
            }

            var hud = UiKit.Canvas("HUD", 10, transform);
            NetGraph.Create(hud.transform, Frames);
        }

        void Start()
        {
            started = Time.realtimeSinceStartup;
            if (ConnectOnStart && !NetDriver.Start(Settings, out var error))
                Debug.LogError($"Lattice: couldn't connect to {NetDriver.Address}: {error}");
        }

        void Update()
        {
            Frames.Record(Time.unscaledDeltaTime, NetDriver.Frame.PluginMs);
            float t = Time.realtimeSinceStartup - started;
            if (!shot && Options.Screenshot != null && t >= Options.After)
            {
                shot = true;
                // Relative to where the game was started (a player resolves bare
                // names against its data folder otherwise).
                var path = System.IO.Path.GetFullPath(Options.Screenshot);
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"Lattice: screenshot to {path}");
            }
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
