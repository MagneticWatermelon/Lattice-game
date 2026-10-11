using System.IO;
using Lattice.World;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lattice.EditorTools
{
    /// <summary>The world's assets: terrain layers and material, the look asset, and the render pipeline's settings.</summary>
    public static class LatticeSetupWorld
    {
        public const string LookPath = "Assets/Lattice/World/WorldLook.asset";
        const string TerrainDir = "Assets/Lattice/World/Terrain";

        /// <summary>(folder, tile size m, smoothness scale, normal strength).</summary>
        static readonly (string name, float tile, float smooth, float normal)[] Layers =
        {
            ("Grass", 2.6f, 0.45f, 1.0f),
            ("Meadow", 3.0f, 0.4f, 0.9f),
            ("Soil", 2.2f, 0.55f, 1.0f),
            ("Rock", 4.5f, 0.6f, 1.1f),
        };

        [MenuItem("Lattice/Setup/World")]
        public static void All()
        {
            Pipeline();
            var look = Look();
            look.Layers = new TerrainLayer[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
                look.Layers[i] = Layer(Layers[i].name, Layers[i].tile, Layers[i].smooth, Layers[i].normal);
            look.TerrainMaterial = TerrainMaterial();
            look.PlainCover = PlainCover();
            TerrainShadersInBuilds();
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssets();
            Debug.Log("Lattice setup: world assets");
        }

        public static WorldLook Look()
        {
            var look = AssetDatabase.LoadAssetAtPath<WorldLook>(LookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<WorldLook>();
                AssetDatabase.CreateAsset(look, LookPath);
            }
            return look;
        }

        static TerrainLayer Layer(string name, float tile, float smooth, float normal)
        {
            var dir = $"{TerrainDir}/{name}";
            var path = $"{dir}/{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null)
            {
                l = new TerrainLayer();
                AssetDatabase.CreateAsset(l, path);
            }
            l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_albedo.png");
            l.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_normal.jpg");
            l.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{name}_mask.png");
            l.tileSize = new Vector2(tile, tile);
            l.tileOffset = Vector2.zero;
            l.normalScale = normal;
            l.metallic = 0f;
            l.smoothness = 0.2f;
            // Mask map: R metallic, G AO, B height, A smoothness. The ground reads
            // rough: smoothness scaled down; the height drives the blend.
            l.maskMapRemapMin = new Vector4(0f, 0f, 0f, 0f);
            l.maskMapRemapMax = new Vector4(0f, 1f, 1f, smooth);
            l.diffuseRemapMin = Vector4.zero;
            l.diffuseRemapMax = Vector4.one;
            EditorUtility.SetDirty(l);
            return l;
        }

        /// <summary>
        /// The terrain engine's own hidden shaders, kept in builds: the terrains are made
        /// at runtime, so a build sees no terrain and strips what an instanced terrain
        /// needs to make its normal map (without it the ground draws unlit, near black).
        /// </summary>
        static void TerrainShadersInBuilds()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "Hidden/TerrainEngine/GenerateNormalmap", "Hidden/Nature/Terrain/Utilities" })
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning($"Lattice setup: no shader {name} to keep in builds");
                    continue;
                }
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    present |= list.GetArrayElementAtIndex(i).objectReferenceValue == shader;
                if (present)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material TerrainMaterial()
        {
            var path = "Assets/Lattice/World/Terrain/Terrain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_EnableHeightBlend", 1f);
            m.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            m.SetFloat("_HeightTransition", 0.35f);
            m.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            // Every layer has normal and mask maps. The editor's shader GUI would turn
            // these on; a material made in code must, or the shader ignores the maps.
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_MASKMAP");
            // The terrains draw instanced: without this a build strips the instanced
            // variants and the terrain falls back to its (ungenerated) base map.
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material PlainCover()
        {
            var path = "Assets/Lattice/World/PlainCover.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", new Color(0.46f, 0.46f, 0.44f));
            m.SetFloat("_Smoothness", 0.18f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Screen-space ambient occlusion: contact shading that grounds cover,
        /// soldiers and the folds of the ground.</summary>
        static void Ssao(UniversalRendererData data)
        {
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (var f in data.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion s)
                    ssao = s;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, data);
                data.rendererFeatures.Add(ssao);
                var so = new SerializedObject(data);
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize = data.rendererFeatures.Count;
                for (int i = 0; i < data.rendererFeatures.Count; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i], out _, out long id);
                    map.GetArrayElementAtIndex(i).longValue = id;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var sso = new SerializedObject(ssao);
            var s2 = sso.FindProperty("m_Settings");
            void F(string n, float v) { var p = s2?.FindPropertyRelative(n); if (p != null) p.floatValue = v; else Debug.LogWarning($"Lattice setup: SSAO has no {n}"); }
            void B(string n, bool v) { var p = s2?.FindPropertyRelative(n); if (p != null) p.boolValue = v; else Debug.LogWarning($"Lattice setup: SSAO has no {n}"); }
            F("Intensity", 0.85f);
            F("Radius", 0.45f);
            F("DirectLightingStrength", 0.3f);
            F("Falloff", 60f);
            B("Downsample", false);
            B("AfterOpaque", false);
            sso.ApplyModifiedPropertiesWithoutUndo();
            ssao.SetActive(true);
        }

        /// <summary>URP for a big, sunny outdoor world with crowds: Forward+, the GPU
        /// Resident Drawer, HDR, four shadow cascades over 260 m.</summary>
        [MenuItem("Lattice/Setup/Render Pipeline")]
        public static void Pipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (asset == null)
            {
                Debug.LogError("Lattice setup: no Assets/Settings/PC_RPAsset.asset");
                return;
            }
            asset.supportsHDR = true;
            asset.msaaSampleCount = 1;
            asset.renderScale = 1f;
            asset.shadowDistance = 260f;
            asset.shadowCascadeCount = 4;
            asset.cascade4Split = new Vector3(0.035f, 0.11f, 0.32f);
            asset.cascadeBorder = 0.12f;
            asset.shadowDepthBias = 0.8f;
            asset.shadowNormalBias = 0.7f;
            asset.mainLightShadowmapResolution = 4096;
            asset.useSRPBatcher = true;
            asset.gpuResidentDrawerMode = GPUResidentDrawerMode.InstancedDrawing;
            var so = new SerializedObject(asset);
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;
            so.FindProperty("m_GPUResidentDrawerEnableOcclusionCullingInCameras").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            asset.supportsCameraDepthTexture = true;
            asset.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(asset);
            foreach (var r in asset.rendererDataList)
            {
                if (r is UniversalRendererData u)
                {
                    u.renderingMode = RenderingMode.ForwardPlus;
                    Ssao(u);
                    EditorUtility.SetDirty(u);
                }
            }
            // The GPU Resident Drawer draws through BatchRendererGroup: keep its shader variants.
            var gs = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            gs.FindProperty("m_BrgStripping").intValue = 2; // keep all
            gs.ApplyModifiedPropertiesWithoutUndo();
            int pc = System.Array.IndexOf(QualitySettings.names, "PC");
            if (pc >= 0)
                QualitySettings.SetQualityLevel(pc, false);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = 1.5f;
            QualitySettings.globalTextureMipmapLimit = 0;
            AssetDatabase.SaveAssets();
            Debug.Log("Lattice setup: render pipeline");
        }
    }
}
