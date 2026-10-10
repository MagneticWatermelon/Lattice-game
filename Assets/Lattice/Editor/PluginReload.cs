using Lattice.Native;
using Lattice.Net;
using UnityEditor;
using UnityEngine;

namespace Lattice.EditorTools
{
    /// <summary>
    /// The plugin in the editor: closed before scripts reload (a stale domain
    /// must never call into it), and a rebuilt DLL swapped in when Play mode
    /// starts, or from the menu, without restarting Unity.
    /// </summary>
    [InitializeOnLoad]
    static class PluginReload
    {
        static PluginReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                NetDriver.Stop();
                LtLibrary.Unload();
            };
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode)
                    LtLibrary.ReloadIfChanged();
                else if (s == PlayModeStateChange.ExitingPlayMode)
                    NetDriver.Stop();
            };
            EditorApplication.quitting += () =>
            {
                NetDriver.Stop();
                LtLibrary.Unload();
            };
        }

        [MenuItem("Lattice/Reload Plugin")]
        static void Reload()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Lattice: leave Play mode to reload the plugin");
                return;
            }
            NetDriver.Stop();
            LtLibrary.Unload();
            Debug.Log(LtLibrary.EnsureLoaded() ? "Lattice: plugin reloaded" : $"Lattice: {LtLibrary.Error}");
        }
    }
}
