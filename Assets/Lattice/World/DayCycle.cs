using System;
using System.Collections.Generic;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.World
{
    /// <summary>
    /// Kiln's day (K4, docs/kiln.md "Day, night and weather"), from the game
    /// step (<c>lt_clock</c>): the same sky on every client, not a byte on the
    /// wire. A two-hour cycle: 70 minutes of day (the sun rising in the east,
    /// 62° up at noon, setting in the west), 10 of dusk, 30 of night, 10 of
    /// dawn. Nights are blue and bright: Tessel's ring, a band across the sky
    /// from horizon to horizon, lights the land, and is the main light (and its
    /// shadows) once the sun is down. With the light: the sky's colors, its
    /// stars, the ambient and the fog, through golden hours at either end of
    /// the day; the lattice's veins and lights glowing brighter at night, a
    /// surge through them every 20 minutes (as the sandfalls shift), and the
    /// Geodes lighting their sectors like lanterns. Cosmetic: night changes
    /// routes (the living bridges, <see cref="Clockwork"/>), never what can be
    /// seen of a player (GDD: "settings never help").
    /// </summary>
    public sealed class DayCycle : IDisposable
    {
        // Lighting by time of day: the day's (as the scene was made), the golden
        // hours' and the night's; blended by how golden and how dark it is.
        static readonly Color GoldenSun = new Color(1.0f, 0.46f, 0.22f), RingLightColor = new Color(0.64f, 0.73f, 0.95f);
        const float RingIntensity = 0.6f;
        static readonly Color GoldenZenith = new Color(0.30f, 0.28f, 0.40f), GoldenHorizon = new Color(0.55f, 0.36f, 0.24f), GoldenGround = new Color(0.16f, 0.11f, 0.08f);
        static readonly Color NightZenith = new Color(0.20f, 0.26f, 0.46f), NightHorizon = new Color(0.12f, 0.16f, 0.30f), NightGround = new Color(0.06f, 0.07f, 0.10f);
        static readonly Color SkyGoldenZenith = new Color(0.04f, 0.05f, 0.14f), SkyGoldenHorizon = new Color(1.5f, 0.7f, 0.32f), SkyGoldenGlow = new Color(0.6f, 0.28f, 0.10f), SkyGoldenGround = new Color(0.14f, 0.10f, 0.08f);
        static readonly Color SkyNightZenith = new Color(0.004f, 0.008f, 0.03f), SkyNightHorizon = new Color(0.05f, 0.08f, 0.18f), SkyNightGlow = Color.black, SkyNightGround = new Color(0.025f, 0.03f, 0.045f);
        static readonly Color RingByDay = new Color(0.12f, 0.12f, 0.15f), RingByNight = new Color(0.5f, 0.56f, 0.75f);
        static readonly Color FogGolden = new Color(0.62f, 0.47f, 0.40f), FogNight = new Color(0.06f, 0.085f, 0.15f);
        /// <summary>Where the ring lights the land from: high in the south.</summary>
        static readonly Quaternion RingFrom = Quaternion.Euler(50f, 8f, 0f);
        /// <summary>A Geode's lantern at night: its color, reach (m) and strength.</summary>
        static readonly Color Lantern = new Color(0.35f, 0.85f, 0.80f);
        public static float LanternRange = 420f, LanternIntensity = 9000f;

        static readonly int ZenithId = Shader.PropertyToID("_Zenith"), HorizonId = Shader.PropertyToID("_Horizon"),
            GroundId = Shader.PropertyToID("_Ground"), GlowId = Shader.PropertyToID("_Glow"), CycleId = Shader.PropertyToID("_Cycle"),
            SunDirId = Shader.PropertyToID("_SunDir"), SunColorId = Shader.PropertyToID("_SunColor"), RingLightId = Shader.PropertyToID("_RingLight"),
            RingColorId = Shader.PropertyToID("_RingColor"), StarsId = Shader.PropertyToID("_Stars"),
            BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

        readonly Light sun;
        readonly SkyLight ambient;
        readonly Material skyAsset, sky;
        readonly ReflectionProbe probe;
        readonly GameObject root;
        readonly List<Light> lanterns = new List<Light>();
        // As the scene was made: the day's look, put back when this goes.
        readonly Quaternion dayRotation;
        readonly Color dayColor, dayZenith, dayHorizon, dayGround, skyZenith, skyHorizon, skyGlow, skyGround, fogDay;
        readonly float dayIntensity;
        readonly List<(Material m, int id, Color c, float night, float surge)> glowing = new List<(Material, int, Color, float, float)>();
        float probeLook = -1f, probeAt = -100f;
        readonly Material[] glowMats;
        readonly int[] glowIds;
        readonly Color[] glowBase;
        readonly float[] glowNight, glowSurge, glowNow;

        DayCycle(Light sun, WorldLook look, GameObject root)
        {
            this.sun = sun;
            this.root = root;
            (dayRotation, dayColor, dayIntensity) = (sun.transform.rotation, sun.color, sun.intensity);
            ambient = sun.GetComponent<SkyLight>();
            if (ambient != null)
                (dayZenith, dayHorizon, dayGround) = (ambient.Zenith, ambient.Horizon, ambient.Ground);
            skyAsset = RenderSettings.skybox;
            if (skyAsset != null)
            {
                // A copy to change each frame (the asset stays as it was made).
                sky = new Material(skyAsset) { name = "Sky (Kiln's day)" };
                RenderSettings.skybox = sky;
                (skyZenith, skyHorizon, skyGlow, skyGround) = (sky.GetColor(ZenithId), sky.GetColor(HorizonId), sky.GetColor(GlowId), sky.GetColor(GroundId));
            }
            fogDay = RenderSettings.fogColor;
            probe = UnityEngine.Object.FindAnyObjectByType<ReflectionProbe>();
            // The lattice's glow: (material, its color, how much brighter at night, how much a surge adds).
            void Glow(Material m, int id, float night, float surge)
            {
                if (m != null && m.HasProperty(id))
                    glowing.Add((m, id, m.GetColor(id), night, surge));
            }
            Glow(look.VeinMaterial, BaseColorId, 0.9f, 2.2f);
            Glow(look.PointMaterial, BaseColorId, 0.5f, 0.8f);
            Glow(look.LiftMaterial, BaseColorId, 0.5f, 0.6f);
            Glow(look.DropMaterial, BaseColorId, 0.5f, 0.6f);
            if (look.Blockout != null && look.Blockout.Length > 2)
                Glow(look.Blockout[2], EmissionId, 3.0f, 1.5f); // crystal
            glowMats = glowing.ConvertAll(g => g.m).ToArray();
            glowIds = glowing.ConvertAll(g => g.id).ToArray();
            glowBase = glowing.ConvertAll(g => g.c).ToArray();
            glowNight = glowing.ConvertAll(g => g.night).ToArray();
            glowSurge = glowing.ConvertAll(g => g.surge).ToArray();
            glowNow = new float[glowMats.Length];
            for (int i = 0; i < glowNow.Length; i++)
                glowNow[i] = 1f;
        }

        public static DayCycle Build(LtWorldHandle world, WorldLook look, Transform parent)
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                return null;
            var root = new GameObject("Kiln's day");
            root.transform.SetParent(parent, false);
            var day = new DayCycle(sun, look, root);
            foreach (var s in world.Sites())
            {
                if (s.kind != LtCode.SiteGeode)
                    continue;
                var go = new GameObject("Geode lantern");
                go.transform.SetParent(root.transform, false);
                go.transform.position = Coords.ToUnity(s.x, s.y, s.level + 30f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = Lantern;
                l.range = LanternRange;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                l.enabled = false;
                day.lanterns.Add(l);
            }
            return day;
        }

        /// <summary>How dark it is (0 day, 1 night), by minutes into the cycle: falling
        /// from 74 to 84 (dusk into night), lifting from 106 to 114 (into dawn).</summary>
        public static float Night(float m)
        {
            if (m < 74f || m >= 114f)
                return 0f;
            if (m < 84f)
                return Mathf.SmoothStep(0f, 1f, (m - 74f) / 10f);
            if (m < 106f)
                return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (m - 106f) / 8f);
        }

        /// <summary>The sun's elevation (degrees) and where along its path it is (0 at
        /// sunrise, six minutes before the day starts; 1 at sunset, six into dusk).</summary>
        public static (float elevation, float path) Sun(float m)
        {
            float p = ((m >= 110f ? m - 120f : m) + 6f) / 82f;
            return (62f * Mathf.Sin(Mathf.PI * p), p);
        }

        /// <summary>This frame's light and sky, from the clock (after the plugin's calls).</summary>
        public void Update(NetFrame frame)
        {
            if (!frame.Welcomed)
                return;
            var c = frame.Clock;
            float m = c.cycleSecs / 60f;
            float night = Night(m);
            var (elev, path) = Sun(m);
            float golden = (1f - night) * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(2f, 16f, elev));
            float day = Mathf.Clamp01(1f - night - golden);
            // The sun: from the east through the south to the west.
            var sunRot = Quaternion.Euler(Mathf.Max(elev, 3f), Mathf.Lerp(-90f, 90f, Mathf.Clamp01(path)), 0f);
            float sunI = dayIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, 12f, elev));
            var sunColor = Color.Lerp(GoldenSun, dayColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 28f, elev)));
            float ringI = RingIntensity * night;
            // The main light (and its shadows): the sun's, or the ring's once it's the brighter.
            if (sunI >= ringI)
            {
                sun.transform.rotation = sunRot;
                (sun.color, sun.intensity) = (sunColor, sunI);
            }
            else
            {
                sun.transform.rotation = RingFrom;
                (sun.color, sun.intensity) = (RingLightColor, ringI);
            }
            if (ambient != null)
            {
                ambient.Zenith = dayZenith * day + GoldenZenith * golden + NightZenith * night;
                ambient.Horizon = dayHorizon * day + GoldenHorizon * golden + NightHorizon * night;
                ambient.Ground = dayGround * day + GoldenGround * golden + NightGround * night;
                ambient.Apply();
            }
            if (sky != null)
            {
                sky.SetFloat(CycleId, 1f);
                sky.SetVector(SunDirId, -(sunRot * Vector3.forward));
                sky.SetColor(SunColorId, sunColor * sunI);
                sky.SetColor(RingLightId, RingLightColor * ringI);
                sky.SetColor(ZenithId, skyZenith * day + SkyGoldenZenith * golden + SkyNightZenith * night);
                sky.SetColor(HorizonId, skyHorizon * day + SkyGoldenHorizon * golden + SkyNightHorizon * night);
                sky.SetColor(GlowId, skyGlow * day + SkyGoldenGlow * golden + SkyNightGlow * night);
                sky.SetColor(GroundId, skyGround * day + SkyGoldenGround * golden + SkyNightGround * night);
                sky.SetColor(RingColorId, Color.Lerp(RingByDay, RingByNight, night));
                sky.SetFloat(StarsId, night);
            }
            RenderSettings.fogColor = fogDay * day + FogGolden * golden + FogNight * night;
            // The lattice: brighter at night, and a surge through it as each 20
            // minutes turns (the sandfalls shift with it).
            float sinceTurn = (c.cycleStep % (uint)LtConst.FallsSteps) / (float)LtConst.TickHz;
            float surge = sinceTurn < 10f ? Mathf.Exp(-sinceTurn / 1.6f) : 0f;
            // (Plain arrays and a for: a foreach over the tuples allocated each frame in
            // the IL2CPP player; and set only as they change.)
            for (int i = 0; i < glowMats.Length; i++)
            {
                float k = 1f + glowNight[i] * night + glowSurge[i] * surge;
                if (Mathf.Abs(k - glowNow[i]) > 1e-3f)
                {
                    glowMats[i].SetColor(glowIds[i], glowBase[i] * k);
                    glowNow[i] = k;
                }
            }
            foreach (var l in lanterns)
            {
                l.enabled = night > 0.01f;
                l.intensity = LanternIntensity * night;
            }
            // The sky's reflections, again when the look has moved on.
            float lookNow = night + golden + elev / 90f;
            if (probe != null && Mathf.Abs(lookNow - probeLook) > 0.03f && Time.unscaledTime - probeAt > 2f)
            {
                probe.RenderProbe();
                (probeLook, probeAt) = (lookNow, Time.unscaledTime);
            }
        }

        public void Dispose()
        {
            // Back as the scene was made (the materials are assets: in the editor,
            // their colors would otherwise stay as the night left them).
            foreach (var (mat, id, color, _, _) in glowing)
                if (mat != null)
                    mat.SetColor(id, color);
            if (sun != null)
            {
                sun.transform.rotation = dayRotation;
                (sun.color, sun.intensity) = (dayColor, dayIntensity);
            }
            if (ambient != null)
            {
                (ambient.Zenith, ambient.Horizon, ambient.Ground) = (dayZenith, dayHorizon, dayGround);
                ambient.Apply();
            }
            if (sky != null)
            {
                RenderSettings.skybox = skyAsset;
                UnityEngine.Object.Destroy(sky);
            }
            RenderSettings.fogColor = fogDay;
            if (root != null)
                UnityEngine.Object.Destroy(root);
        }
    }
}
