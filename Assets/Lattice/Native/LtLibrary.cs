using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lattice.Native
{
    /// <summary>
    /// Loads the plugin (lattice_client.dll, built in lattice-net by
    /// scripts/unity-plugin.sh) and binds its functions into <see cref="Api"/>.
    /// The bindings (LatticeNative.g.cs) come from the same build; a plugin
    /// whose version or layout hash differs is refused.
    ///
    /// In the editor it loads a copy from Temp/LatticePlugin, so a rebuild can
    /// overwrite the DLL in Assets while Unity runs; <see cref="ReloadIfChanged"/>
    /// swaps the new one in when no session is open.
    /// </summary>
    public static unsafe class LtLibrary
    {
        /// <summary>The plugin's functions. Valid while <see cref="Loaded"/>.</summary>
        public static LtApi Api;

        public static bool Loaded => module != IntPtr.Zero;

        /// <summary>Why the last load failed.</summary>
        public static string Error { get; private set; } = "";

        /// <summary>Sessions open: the library can't be swapped under them.</summary>
        public static int OpenSessions { get; internal set; }

        static IntPtr module;
        static DateTime loadedStamp;

        const string FileName = "lattice_client.dll";

        /// <summary>Where the plugin is: Assets/Plugins/x86_64 in the editor, and
        /// the same path under the player's data folder in a build.</summary>
        public static string SourcePath => Path.GetFullPath(Path.Combine(Application.dataPath, "Plugins", "x86_64", FileName));

        public static bool EnsureLoaded()
        {
            return Loaded || Load();
        }

        static bool Load()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var source = SourcePath;
            if (!File.Exists(source))
            {
                Error = $"no plugin at {source}: build it in lattice-net with scripts/unity-plugin.sh";
                return false;
            }
            var stamp = File.GetLastWriteTimeUtc(source);
            var path = source;
#if UNITY_EDITOR
            path = ShadowCopy(source, stamp);
#endif
            var h = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
            if (h == IntPtr.Zero)
            {
                Error = $"couldn't load {path} (Windows error {Marshal.GetLastWin32Error()})";
                return false;
            }
            var api = new LtApi();
            var missing = api.Bind(name => GetProcAddress(h, name));
            if (missing.Length > 0)
            {
                FreeLibrary(h);
                Error = $"the plugin lacks {missing}: rebuild it with scripts/unity-plugin.sh";
                return false;
            }
            uint version = api.lt_version();
            ulong hash = api.lt_layout_hash();
            if (version != LtConst.Version || hash != LtConst.LayoutHash)
            {
                FreeLibrary(h);
                Error = $"the plugin (version {version}, layout {hash:X16}) doesn't match its bindings " +
                        $"(version {LtConst.Version}, layout {LtConst.LayoutHash:X16}): rebuild both with scripts/unity-plugin.sh";
                return false;
            }
            Api = api;
            module = h;
            loadedStamp = stamp;
            Error = "";
            return true;
#else
            Error = "the plugin is built for Windows only";
            return false;
#endif
        }

        /// <summary>Unloads the plugin. Every session must be closed first.</summary>
        public static void Unload()
        {
            if (!Loaded)
                return;
            if (OpenSessions > 0)
            {
                Debug.LogWarning($"Lattice: not unloading the plugin with {OpenSessions} session(s) open");
                return;
            }
            Api = default;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            FreeLibrary(module);
#endif
            module = IntPtr.Zero;
        }

        /// <summary>Swaps in a rebuilt plugin (editor), if nothing uses this one.
        /// Returns whether one is loaded afterwards.</summary>
        public static bool ReloadIfChanged()
        {
            if (Loaded && OpenSessions == 0 && File.Exists(SourcePath) && File.GetLastWriteTimeUtc(SourcePath) != loadedStamp)
                Unload();
            return EnsureLoaded();
        }

        /// <summary>The last error the plugin recorded on this thread.</summary>
        public static string LastPluginError()
        {
            if (!Loaded)
                return Error;
            var buf = stackalloc byte[512];
            int n = Api.lt_last_error(buf, 512);
            return n <= 0 ? "" : System.Text.Encoding.UTF8.GetString(buf, Math.Min(n, 511));
        }

#if UNITY_EDITOR
        static string ShadowCopy(string source, DateTime stamp)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "LatticePlugin"));
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, $"lattice_client-{stamp.Ticks:X}.dll");
            if (!File.Exists(copy))
                File.Copy(source, copy);
            // Older copies: whichever no process holds any more.
            foreach (var old in Directory.GetFiles(dir, "lattice_client-*.dll"))
            {
                if (old == copy)
                    continue;
                try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return copy;
        }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        const uint LoadWithAlteredSearchPath = 0x8;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

        [DllImport("kernel32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
#endif
    }
}
