using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The player's settings (GDD §9.2, §9.5), kept between games
    /// (PlayerPrefs) and applied at once: the mouse (hip and down the sights),
    /// the field of view, the render scale, vertical sync, the volume, the
    /// colors (friend or foe, or faction) and the net graph.
    /// </summary>
    public static class Settings
    {
        const string Prefix = "Lattice.";
        public static float Sensitivity = 1f, AdsSensitivity = 1f, Fov = 74f, RenderScale = 1f, Volume = 0.8f;
        public static bool VSync = true;

        /// <summary>The mouse's radians per pixel at a sensitivity of 1.</summary>
        public const float BaseSensitivity = 0.0025f;

        public static void Load()
        {
            Sensitivity = PlayerPrefs.GetFloat(Prefix + "Sensitivity", 1f);
            AdsSensitivity = PlayerPrefs.GetFloat(Prefix + "AdsSensitivity", 1f);
            Fov = PlayerPrefs.GetFloat(Prefix + "Fov", 74f);
            RenderScale = PlayerPrefs.GetFloat(Prefix + "RenderScale", 1f);
            Volume = PlayerPrefs.GetFloat(Prefix + "Volume", 0.8f);
            VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) != 0;
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "Sensitivity", Sensitivity);
            PlayerPrefs.SetFloat(Prefix + "AdsSensitivity", AdsSensitivity);
            PlayerPrefs.SetFloat(Prefix + "Fov", Fov);
            PlayerPrefs.SetFloat(Prefix + "RenderScale", RenderScale);
            PlayerPrefs.SetFloat(Prefix + "Volume", Volume);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>Applies everything but what a command line set for a self-check
        /// (<paramref name="keepVSync"/>: --no-vsync; <paramref name="keepScale"/>:
        /// --render-scale; <paramref name="keepVolume"/>: --no-audio).</summary>
        public static void Apply(bool keepVSync, bool keepScale, bool keepVolume)
        {
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            if (p != null)
            {
                p.Sensitivity = BaseSensitivity * Sensitivity;
                p.AdsSensitivity = AdsSensitivity;
            }
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent(out Lattice.LocalPlayer.CameraRig rig))
                rig.Fov = Fov;
            if (!keepVSync)
                QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (!keepScale && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.renderScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
            if (!keepVolume)
                AudioListener.volume = Volume;
        }
    }
}
