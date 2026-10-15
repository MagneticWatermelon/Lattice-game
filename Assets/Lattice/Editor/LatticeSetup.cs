using System.IO;
using System.Linq;
using Lattice.Game;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace Lattice.EditorTools
{
    /// <summary>
    /// Builds the project's generated assets and the scene, idempotently, so
    /// nothing is hand-edited YAML. Run from the menu (Lattice > Setup) or
    /// headless: `unity run . -- -executeMethod Lattice.EditorTools.LatticeSetup.All`.
    /// </summary>
    public static class LatticeSetup
    {
        public const string ScenePath = "Assets/Scenes/Lattice.unity";
        const string FontDir = "Assets/Lattice/UI/Resources/Fonts";
        const string SettingsDir = "Assets/Lattice/Settings";

        /// <summary>Everything. TMP's essentials import only once this call returns,
        /// so a fresh project needs a second run (headless: run it twice).</summary>
        [MenuItem("Lattice/Setup/Everything")]
        public static void All()
        {
            Project();
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                TmpEssentials();
                Debug.Log("Lattice setup: TMP essentials are importing; run the setup again for the fonts and the scene");
                return;
            }
            Fonts();
            LatticeSetupWorld.All();
            LatticeSetupPlayers.All();
            LatticeSetupCombat.All();
            LatticeSetupAudio.All();
            Scene();
            AssetDatabase.SaveAssets();
            Debug.Log("Lattice setup: done");
        }

        [MenuItem("Lattice/Setup/Project Settings")]
        public static void Project()
        {
            PlayerSettings.companyName = "Lattice";
            PlayerSettings.productName = "Lattice";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            // IL2CPP: the main thread is mostly C# (URP's render loop, the GPU Resident
            // Drawer, the crowd), and runs it faster than Mono: a 1,000-player
            // fight's frame p99 went from 6.2-6.6 ms to 5.3-5.7 ms.
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.allowUnsafeCode = true;
            // GPU and thread times per frame, for the self-check's report and the net graph.
            PlayerSettings.enableFrameTimingStats = true;
            // Splash: the client starts straight into the game.
            PlayerSettings.SplashScreen.show = false;
            Debug.Log("Lattice setup: project settings");
        }

        [MenuItem("Lattice/Setup/TMP Essentials")]
        public static void TmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
                return;
            var pkg = Directory.GetFiles(Path.GetFullPath("Library/PackageCache"), "TMP Essential Resources.unitypackage", SearchOption.AllDirectories).FirstOrDefault();
            if (pkg == null)
            {
                Debug.LogError("Lattice setup: no TMP Essential Resources package in the package cache");
                return;
            }
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
            Debug.Log("Lattice setup: TMP essentials imported");
        }

        [MenuItem("Lattice/Setup/Fonts")]
        public static void Fonts()
        {
            Directory.CreateDirectory(FontDir);
            MakeFont("Assets/Lattice/UI/Fonts/SairaCondensed-SemiBold.ttf", "SairaCondensed-SemiBold SDF", 72);
            MakeFont("Assets/Lattice/UI/Fonts/SairaCondensed-Bold.ttf", "SairaCondensed-Bold SDF", 72);
            MakeFont("Assets/Lattice/UI/Fonts/IBMPlexMono-Medium.ttf", "IBMPlexMono-Medium SDF", 64);
            MakeFont("Assets/Lattice/UI/Fonts/IBMPlexMono-SemiBold.ttf", "IBMPlexMono-SemiBold SDF", 64);
        }

        /// <summary>Everything the HUD writes, up front: no atlas work mid-fight (and no
        /// font asset that changes whenever the game runs in the editor).</summary>
        const string FontChars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~°±×·–—’“”•…→←↑↓▲▼●○■□‹›«»";

        static void MakeFont(string ttf, string name, int size)
        {
            var path = $"{FontDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                // Made before: top up what it's missing.
                existing.TryAddCharacters(FontChars, out _);
                KeepOnBuild(existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null)
            {
                Debug.LogError($"Lattice setup: no font at {ttf}");
                return;
            }
            var fa = TMP_FontAsset.CreateFontAsset(font, size, size / 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = name;
            fa.TryAddCharacters(FontChars, out _);
            AssetDatabase.CreateAsset(fa, path);
            for (int i = 0; i < fa.atlasTextures.Length; i++)
            {
                fa.atlasTextures[i].name = $"{name} Atlas {i}";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa);
            }
            fa.material.name = $"{name} Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            KeepOnBuild(fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log($"Lattice setup: font {path}");
        }

        /// <summary>A dynamic font asset is emptied before every player build by
        /// default (and saved so): the build would then add each glyph the first time
        /// it's drawn, mid-fight. These keep their atlas.</summary>
        static void KeepOnBuild(TMP_FontAsset fa)
        {
            var so = new SerializedObject(fa);
            so.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Lattice/Setup/Scene")]
        public static void Scene()
        {
            Directory.CreateDirectory(SettingsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var game = new GameObject("Lattice");
            game.AddComponent<LatticeGame>();
            var worldGo = new GameObject("World");
            var world = worldGo.AddComponent<Lattice.World.WorldView>();
            world.Look = LatticeSetupWorld.Look();
            var crowdGo = new GameObject("Crowd");
            var crowd = crowdGo.AddComponent<Lattice.Players.Crowd>();
            crowd.Look = AssetDatabase.LoadAssetAtPath<Lattice.Players.SoldierLook>(LatticeSetupPlayers.LookPath);
            var combatGo = new GameObject("Combat");
            var combatLook = AssetDatabase.LoadAssetAtPath<Lattice.Combat.CombatLook>(LatticeSetupCombat.LookPath);
            var gun = combatGo.AddComponent<Lattice.Combat.Gun>();
            gun.Soldier = crowd.Look;
            gun.Look = combatLook;
            var effects = combatGo.AddComponent<Lattice.Combat.Effects>();
            effects.Look = combatLook;
            effects.Crowd = crowd;
            combatGo.AddComponent<Lattice.Combat.Gadgets>().Look = combatLook;
            var audioGo = new GameObject("Audio");
            var audio = audioGo.AddComponent<Lattice.Audio.BattleAudio>();
            audio.Look = AssetDatabase.LoadAssetAtPath<Lattice.Audio.AudioLook>(LatticeSetupAudio.LookPath);
            audio.Gun = gun;
            audio.Crowd = crowd;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 9000f;
            cam.fieldOfView = 74f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            // SMAA: no ghosting on tracers and small, fast soldiers (TAA smears them).
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            // Dithered before the 8-bit output: the sky's gradient shows no bands.
            data.dithering = true;
            data.renderShadows = true;
            camGo.AddComponent<AudioListener>();

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;
            sunGo.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            var sunData = sunGo.AddComponent<UniversalAdditionalLightData>();
            sunData.usePipelineSettings = true;
            RenderSettings.sun = sun;

            var volGo = new GameObject("Post");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = Profile();

            // Ambient from three colors, not the skybox: nothing is baked, so the
            // sky light builds its own probe (shadows read as daylight, not black).
            sunGo.AddComponent<Lattice.World.SkyLight>();
            // The clouds' shadows, a cookie for the sun drawn from the sky's clouds.
            var clouds = sunGo.AddComponent<Lattice.World.CloudShadows>();
            clouds.Compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Lattice/Shaders/CloudShadow.compute");
            RenderSettings.skybox = Sky();

            // Specular reflections of the sky: rendered once at start (sky only).
            var probeGo = new GameObject("Sky Reflection");
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.cullingMask = 0;
            probe.size = new Vector3(20000f, 4000f, 20000f);
            probe.resolution = 128;
            probe.hdr = true;
            probe.importance = 0;
            probeGo.AddComponent<Lattice.World.RenderOnce>();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.00022f;
            RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.82f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            foreach (var s in scenes)
                s.enabled = s.path == ScenePath;
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"Lattice setup: scene {ScenePath}");
        }

        const string CloudTexture = "Assets/Lattice/World/Sky/Clouds.png";

        /// <summary>The sky (Lattice/Sky: gradient, sun, clouds), its values the shader's
        /// defaults; the cloud noise (Tools/Blender/clouds.py) imported as data that tiles.</summary>
        static Material Sky()
        {
            if (AssetImporter.GetAtPath(CloudTexture) is TextureImporter ti)
            {
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("Lattice/Sky");
            var path = $"{SettingsDir}/Sky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            ShaderDefaults(m);
            m.SetTexture("_CloudTex", AssetDatabase.LoadAssetAtPath<Texture2D>(CloudTexture));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Puts a material's colors, vectors and numbers back to its shader's
        /// defaults: the shader's Properties are where they're tuned.</summary>
        internal static void ShaderDefaults(Material m)
        {
            var shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var id = shader.GetPropertyNameId(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        m.SetVector(id, shader.GetPropertyDefaultVectorValue(i));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        m.SetFloat(id, shader.GetPropertyDefaultFloatValue(i));
                        break;
                }
            }
        }

        static VolumeProfile Profile()
        {
            var path = $"{SettingsDir}/Post.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, path);
            }
            T Get<T>() where T : VolumeComponent
            {
                if (!p.TryGet(out T c))
                {
                    c = p.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, p);
                }
                c.active = true;
                return c;
            }
            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);
            var color = Get<ColorAdjustments>();
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(-6f);
            var wb = Get<WhiteBalance>();
            wb.temperature.Override(4f);
            var vig = Get<Vignette>();
            vig.intensity.Override(0.18f);
            vig.smoothness.Override(0.4f);
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>Builds the Windows player into Builds/Windows (a development build when asked).</summary>
        [MenuItem("Lattice/Build/Windows Player")]
        public static void BuildWindows()
        {
            var dev = System.Environment.GetCommandLineArgs().Contains("-latticeDev");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Lattice.exe",
                target = BuildTarget.StandaloneWindows64,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"Lattice build: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:F0} s -> {s.outputPath}");
            if (s.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
