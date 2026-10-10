using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// The sky's light on everything the sun doesn't reach: an ambient probe
    /// built here from three colors (zenith, horizon, ground), because Unity
    /// only computes one when lighting is baked, and nothing here is baked
    /// (the world arrives from the server). The probe is the projection of a
    /// sky that brightens linearly from the horizon up and darkens toward the
    /// ground: L0 is the mean radiance, (2E + S + G) / 4, and the gradient
    /// lands in L1's vertical term, (S - G) / 3 (checked against
    /// SphericalHarmonicsL2.Evaluate: up S-ish, sideways E-ish, down G-ish).
    /// </summary>
    [ExecuteAlways]
    public sealed class SkyLight : MonoBehaviour
    {
        /// <summary>Linear radiance, relative to the sun's 2.2: daylight shadows stay readable.</summary>
        [ColorUsage(false, true)] public Color Zenith = new Color(0.42f, 0.50f, 0.64f);
        [ColorUsage(false, true)] public Color Horizon = new Color(0.40f, 0.42f, 0.42f);
        [ColorUsage(false, true)] public Color Ground = new Color(0.20f, 0.18f, 0.14f);
        public float Intensity = 1f;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        /// <summary>Sets the ambient. The colors here are linear radiance; Unity takes
        /// trilight colors as gamma-space (and rebuilds its probe from them when
        /// they change), so it's handed their gamma equivalents, and the probe too.</summary>
        public void Apply()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = (Zenith * Intensity).gamma;
            RenderSettings.ambientEquatorColor = (Horizon * Intensity).gamma;
            RenderSettings.ambientGroundColor = (Ground * Intensity).gamma;
            RenderSettings.ambientProbe = Probe(Zenith * Intensity, Horizon * Intensity, Ground * Intensity);
        }

        public static SphericalHarmonicsL2 Probe(Color s, Color e, Color g)
        {
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
            {
                sh[c, 0] = (2f * e[c] + s[c] + g[c]) / 4f;
                sh[c, 1] = (s[c] - g[c]) / 3f; // Unity's L1 order: y, z, x
            }
            return sh;
        }
    }
}
