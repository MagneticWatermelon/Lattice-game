using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

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
                var psi = new ProcessStartInfo("wsl.exe", "-e hostname -I")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null)
                    return null;
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(5000))
                    return null;
                var m = Regex.Match(output, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch (Exception)
            {
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
        }
    }

    /// <summary>
    /// The command line (player builds), as the Bevy client takes it, plus the
    /// self-checks Claude Code drives builds with.
    /// </summary>
    public class Options
    {
        public string Server, TokenKey, Invite, View, Spectate, Screenshot;
        public ulong User, ServerId;
        public float NearMs, NearMaxMs, MidMs;
        public float After = 5f, ExitAfter = -1f, ShotEvery = 1f;
        public int Shots = 1;
        public bool NoVsync, Ghosts, Autoplay, Autofire, Autoaim, Autotarget, HudTest, NoAudio, Windowed;
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
                    case "--server": o.Server = Val(); break;
                    case "--user": o.User = U(); break;
                    case "--token-key": o.TokenKey = Val(); break;
                    case "--server-id": o.ServerId = U(); break;
                    case "--invite": o.Invite = Val(); break;
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
                    case "--hud-test": o.HudTest = true; break;
                    case "--no-audio": o.NoAudio = true; break;
                    case "--windowed": o.Windowed = true; break;
                    case "--screenshot": o.Screenshot = Val(); break;
                    case "--after": o.After = F(); break;
                    case "--shots": o.Shots = System.Math.Max(1, (int)F()); break;
                    case "--shot-every": o.ShotEvery = F(); break;
                    case "--exit-after": o.ExitAfter = F(); break;
                    // Unity's own flags (and their values) pass through.
                    default:
                        if (a.StartsWith("-") && !a.StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                            i++;
                        break;
                }
            }
            return o;
        }
    }
}
