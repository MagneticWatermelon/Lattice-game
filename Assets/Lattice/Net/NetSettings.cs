using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Lattice.Native;

namespace Lattice.Net
{
    /// <summary>
    /// Where and as whom to connect, with the render delays. Defaults suit the
    /// dev setup (a lattice-server in WSL); a player build's command line
    /// overrides them (<see cref="Options"/>).
    /// </summary>
    [Serializable]
    public class NetSettings
    {
        /// <summary>"host:port"; empty: the WSL server on this machine (its
        /// address from `wsl hostname -I`), else 127.0.0.1:40000.</summary>
        public string server = "";
        /// <summary>The dev token's user id; 0: a random one per launch.</summary>
        public ulong user;
        /// <summary>The server's --token-key (64 hex digits); empty: the public dev key.</summary>
        public string tokenKey = "";
        public ulong serverId = 1;
        /// <summary>A playtest invite file; replaces the four above.</summary>
        public string invite = "";
        /// <summary>Render delays, ms (0: the defaults, 67 → 133 near and 200 mid/far).</summary>
        public float nearMs, nearMaxMs, midMs;
        /// <summary>The session runs on the plugin's net thread (U3, GDD §12.3): it
        /// receives, makes inputs from the latest intent and sends about a thousand
        /// times a second, so a stalled frame costs no inputs. Off (--pump): the
        /// frame's own calls do it (U1-U2's pump mode).</summary>
        public bool netThread = true;

        public const int Port = 40000;

        /// <summary>The server's address: as set, or found.</summary>
        public string ResolveServer()
        {
            if (!string.IsNullOrWhiteSpace(server))
                return server.Contains(":") ? server : $"{server}:{Port}";
            var wsl = WslAddress();
            return $"{wsl ?? "127.0.0.1"}:{Port}";
        }

        public ulong ResolveUser()
        {
            if (user != 0)
                return user;
            // Like the Bevy client: the clock's nanoseconds, above any id a
            // login service would hand out.
            return ((ulong)DateTime.UtcNow.Ticks * 100UL) | (1UL << 40);
        }

        /// <summary>WSL's own address (NAT mode), or null: a dev server runs there.</summary>
        public static string WslAddress()
        {
            try
            {
                var wsl = System.IO.Path.Combine(Environment.SystemDirectory, "wsl.exe");
                if (!System.IO.File.Exists(wsl))
                    return null;
                var output = HiddenProcess.Run($"\"{wsl}\" -e hostname -I", 5000);
                if (output == null)
                {
                    UnityEngine.Debug.LogWarning("Lattice: no WSL address (wsl.exe didn't answer)");
                    return null;
                }
                var m = Regex.Match(output, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Lattice: no WSL address ({e.GetType().Name}: {e.Message})");
                return null;
            }
        }

        /// <summary>Applies the connection flags of a command line.</summary>
        public void Apply(Options o)
        {
            if (o.Server != null) server = o.Server;
            if (o.User != 0) user = o.User;
            if (o.TokenKey != null) tokenKey = o.TokenKey;
            if (o.ServerId != 0) serverId = o.ServerId;
            if (o.Invite != null) invite = o.Invite;
            if (o.NearMs > 0) nearMs = o.NearMs;
            if (o.NearMaxMs > 0) nearMaxMs = o.NearMaxMs;
            if (o.MidMs > 0) midMs = o.MidMs;
            if (o.Pump) netThread = false;
        }
    }

    /// <summary>
    /// The command line (player builds), as the Bevy client takes it, plus the
    /// self-checks Claude Code drives builds with.
    /// </summary>
    public class Options
    {
        public string Server, TokenKey, Invite, View, Spectate, Screenshot;
        /// <summary>--show map|menu|settings: open that screen once the world is up (screenshots).</summary>
        public string Show;
        /// <summary>--kit N: deploy as class N (a self-check; -1: the one chosen last).</summary>
        public int Kit = -1;
        /// <summary>--weapon N: carry weapon N (LT_WEAPON_*; a self-check).</summary>
        public int Weapon = -1;
        /// <summary>The command line said where to connect or ran a self-check: no title
        /// screen. --title shows it anyway.</summary>
        public bool Joins, Title;
        /// <summary>--profile PATH: a development build records the profiler from its
        /// first frame into PATH (.raw, for the editor's Profiler to load).</summary>
        public string Profile;
        /// <summary>Features to turn off, to measure what each costs (comma-separated:
        /// grass, ssao, shadows, clouds, bloom, cover, rocks, fpview).</summary>
        public string Off = "";
        /// <summary>The render scale (URP's, 0.1-2): 2 makes the GPU the bottleneck, so
        /// it runs at full clock and its times compare between runs.</summary>
        public float RenderScale;
        public ulong User, ServerId;
        /// <summary>--world kiln|kit|harrow|N|none: the world to build at start, on a thread of
        /// its own, so joining a server on it doesn't stall the session while the
        /// plugin builds it (Kiln takes ~0.6 s). Any world still works: one the server
        /// names that isn't this is built when its Welcome arrives. [kiln]</summary>
        public ulong World = (ulong)LtCode.WorldKiln;
        public float NearMs, NearMaxMs, MidMs;
        public float After = 5f, ExitAfter = -1f, ShotEvery = 1f;
        public int Shots = 1;
        public bool NoVsync, Ghosts, Autoplay, Autofire, Autoaim, Autotarget, HudTest, NoAudio, Windowed, FactionColors, TierColors, Controls, Pump, NetGraph;
        /// <summary>--alloc-probe [systems]: where managed allocations happen, by player loop phase (or system).</summary>
        public bool AllocProbe, AllocProbeSystems;
        /// <summary>--crouch: the self-check plays crouched (with --autotarget, it fights crouched).</summary>
        public bool Crouch;
        /// <summary>--ability: use the class's ability now and then (a self-check).</summary>
        public bool Ability;
        /// <summary>--tools: throw the class's tools now and then and hold the use key (a self-check).</summary>
        public bool Tools;
        /// <summary>Main-thread stalls to stage (--freeze AT:MS,...): at AT seconds after
        /// start, the frame stops for MS milliseconds. U3's self-check.</summary>
        public (float at, int ms)[] Freezes = new (float, int)[0];
        public string Errors = "";

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                string Val()
                {
                    if (i + 1 < args.Length)
                        return args[++i];
                    o.Errors += $"{a} needs a value; ";
                    return "";
                }
                float F() => float.TryParse(Val(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                ulong U() => ulong.TryParse(Val(), out var v) ? v : 0;
                switch (a)
                {
                    case "--server": o.Server = Val(); o.Joins = true; break;
                    case "--title": o.Title = true; break;
                    case "--show": o.Show = Val(); break;
                    case "--kit": o.Kit = (int)F(); break;
                    case "--weapon": o.Weapon = (int)F(); break;
                    case "--world":
                        var w = Val();
                        o.World = w == "kiln" ? (ulong)LtCode.WorldKiln : w == "kit" ? (ulong)LtCode.WorldKit : w == "harrow" ? 1UL : w == "none" ? 0UL : ulong.TryParse(w, out var id) ? id : 0UL;
                        break;
                    case "--user": o.User = U(); break;
                    case "--token-key": o.TokenKey = Val(); break;
                    case "--server-id": o.ServerId = U(); break;
                    case "--invite": o.Invite = Val(); o.Joins = true; break;
                    case "--near-ms": o.NearMs = F(); break;
                    case "--near-max-ms": o.NearMaxMs = F(); break;
                    case "--mid-ms": o.MidMs = F(); break;
                    case "--view": o.View = Val(); break;
                    case "--spectate": o.Spectate = Val(); break;
                    case "--no-vsync": o.NoVsync = true; break;
                    case "--ghosts": o.Ghosts = true; break;
                    case "--autoplay": o.Autoplay = true; break;
                    case "--autofire": o.Autofire = true; break;
                    case "--autoaim": o.Autoaim = true; break;
                    case "--autotarget": o.Autotarget = true; break;
                    case "--crouch": o.Crouch = true; break;
                    case "--ability": o.Ability = true; break;
                    case "--tools": o.Tools = true; break;
                    case "--hud-test": o.HudTest = true; break;
                    case "--no-audio": o.NoAudio = true; break;
                    case "--windowed": o.Windowed = true; break;
                    case "--faction-colors": o.FactionColors = true; break;
                    case "--tier-colors": o.TierColors = true; break;
                    case "--controls": o.Controls = true; break;
                    case "--net-graph": o.NetGraph = true; break;
                    case "--alloc-probe": o.AllocProbe = true; break;
                    case "--alloc-probe-systems": o.AllocProbe = o.AllocProbeSystems = true; break;
                    case "--off": o.Off = Val(); break;
                    case "--render-scale": o.RenderScale = F(); break;
                    case "--pump": o.Pump = true; break;
                    case "--freeze":
                    {
                        var list = new System.Collections.Generic.List<(float, int)>();
                        foreach (var part in Val().Split(','))
                        {
                            var kv = part.Split(':');
                            if (kv.Length == 2 && float.TryParse(kv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var at) && int.TryParse(kv[1], out var ms))
                                list.Add((at, ms));
                            else
                                o.Errors += $"--freeze: {part} isn't AT:MS; ";
                        }
                        o.Freezes = list.ToArray();
                        break;
                    }
                    case "--screenshot": o.Screenshot = Val(); break;
                    case "--after": o.After = F(); break;
                    case "--shots": o.Shots = System.Math.Max(1, (int)F()); break;
                    case "--shot-every": o.ShotEvery = F(); break;
                    case "--exit-after": o.ExitAfter = F(); break;
                    case "--profile": o.Profile = Val(); break;
                    // Unity's own flags (and their values) pass through.
                    default:
                        if (a.StartsWith("-") && !a.StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            i++;
                        break;
                }
            }
            o.Joins |= o.Autoplay || o.Autofire || o.Autoaim || o.Autotarget || o.HudTest || o.Screenshot != null || o.ExitAfter > 0f || o.User != 0;
            return o;
        }
    }
}
