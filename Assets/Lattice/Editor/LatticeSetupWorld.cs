using System.IO;
using System.Linq;
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
            Cover(look);
            Kiln(look);
            Grass(look);
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

        /// <summary>The cover kinds in LtCode.Cover* order: each one's model (World/Cover,
        /// from the Bevy client's props by Tools/Blender/prop_fbx.py: unit boxes, base
        /// at 0, long side x, front +z), how it tiles (as the Bevy client: a wall block
        /// 1.28 times as long as it's tall), and whether its front is an end (the
        /// container's doors are on its z faces; the Bevy client stretches them along
        /// the side).</summary>
        static readonly (string name, float tileAspect, bool frontOnEnd)[] CoverKinds =
        {
            ("Wall", 1.28f, false),
            ("Crate", 0f, false),
            ("Container", 0f, true),
            ("Sandbags", 0f, false),
            ("Post", 0f, false),
            ("Command", 0f, false),
            ("Bunker", 0f, false),
        };

        const string CoverDir = "Assets/Lattice/World/Cover";

        static void Cover(WorldLook look)
        {
            look.Cover = new CoverModel[CoverKinds.Length];
            for (int k = 0; k < CoverKinds.Length; k++)
            {
                var (name, aspect, end) = CoverKinds[k];
                var mesh = AssetDatabase.LoadAllAssetsAtPath($"{CoverDir}/{name}.fbx").OfType<Mesh>().FirstOrDefault();
                if (mesh == null)
                {
                    Debug.LogWarning($"Lattice setup: no cover model {name}");
                    continue;
                }
                var m = ModelMaterial(name);
                // At a distance a plain box of the model's average color stands in.
                var far = LitMaterial($"{CoverDir}/{name}_Far.mat");
                far.SetColor("_BaseColor", AverageColor($"{CoverDir}/{name}_albedo.jpg", mesh));
                far.SetFloat("_Smoothness", 0.25f);
                far.SetFloat("_Metallic", 0f);
                far.SetFloat("_TintStrength", 0f);
                far.enableInstancing = true;
                EditorUtility.SetDirty(far);
                look.Cover[k] = new CoverModel { Mesh = mesh, Materials = new[] { m }, Far = far, TileAspect = aspect, FrontOnEnd = end };
            }
            look.RockMesh = AssetDatabase.LoadAllAssetsAtPath($"{CoverDir}/Rocks.fbx").OfType<Mesh>().FirstOrDefault();
            look.RockMaterial = look.RockMesh != null ? ModelMaterial("Rocks") : null;
        }

        /// <summary>A model's material from its maps beside it (Name_albedo.jpg,
        /// _normal.jpg, _metal.png: URP's metallic in R, smoothness in A).</summary>
        static Material ModelMaterial(string name)
        {
            var m = LitMaterial($"{CoverDir}/{name}.mat");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoverDir}/{name}_albedo.jpg"));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoverDir}/{name}_normal.jpg"));
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{CoverDir}/{name}_metal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_TintStrength", 0f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        const string KilnDir = "Assets/Lattice/World/Kiln";

        /// <summary>Kiln's grounds in <c>LT_GROUND_*</c> order: (name, the texture set it's
        /// made from, its color (sRGB), tile size m, smoothness, normal strength). K0's
        /// blockout recolors the project's own textures (Poly Haven) toward each.</summary>
        static readonly (string name, string from, Color32 color, float tile, float smooth, float normal)[] KilnGrounds =
        {
            ("Sand", "Soil", new Color32(214, 180, 126, 255), 3.0f, 0.25f, 0.8f),
            ("Glass", "Rock", new Color32(66, 100, 98, 255), 5.0f, 0.85f, 0.6f),
            ("Salt", "Soil", new Color32(226, 222, 210, 255), 4.0f, 0.35f, 0.6f),
            ("Ochre", "Rock", new Color32(178, 98, 58, 255), 6.0f, 0.3f, 1.2f),
            ("Basalt", "Rock", new Color32(64, 60, 60, 255), 5.0f, 0.35f, 1.1f),
            ("Pale", "Rock", new Color32(206, 198, 184, 255), 6.0f, 0.4f, 1.0f),
            ("Road", "Gravel", new Color32(136, 116, 92, 255), 2.5f, 0.2f, 0.9f),
            ("Pad", "Gravel", new Color32(160, 150, 134, 255), 2.5f, 0.25f, 0.8f),
            ("Scree", "Gravel", new Color32(152, 118, 88, 255), 3.0f, 0.2f, 1.0f),
        };

        /// <summary>Kiln's blockout kinds (<c>LT_COVER_BLOCK</c> on): (name, color (sRGB), smoothness, glow).</summary>
        static readonly (string name, Color32 color, float smooth, float glow)[] BlockoutKinds =
        {
            ("Block", new Color32(150, 146, 140, 255), 0.2f, 0f),
            ("Rock", new Color32(150, 86, 54, 255), 0.15f, 0f),
            ("Crystal", new Color32(90, 205, 196, 255), 0.85f, 0.35f),
            ("Bone", new Color32(232, 224, 204, 255), 0.3f, 0f),
        };

        /// <summary>Kiln's look: its ground's layers, its blockout's materials, its lanes'
        /// veins and its control points' rings.</summary>
        static void Kiln(WorldLook look)
        {
            Directory.CreateDirectory(KilnDir);
            look.KilnLayers = new TerrainLayer[KilnGrounds.Length];
            for (int i = 0; i < KilnGrounds.Length; i++)
                look.KilnLayers[i] = KilnLayer(KilnGrounds[i]);
            look.Blockout = new Material[BlockoutKinds.Length];
            for (int i = 0; i < BlockoutKinds.Length; i++)
            {
                var (name, color, smooth, glow) = BlockoutKinds[i];
                var m = LitMaterial($"{KilnDir}/Blockout_{name}.mat");
                m.SetColor("_BaseColor", color);
                m.SetTexture("_BaseMap", null);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_TintStrength", 0f);
                if (glow > 0f)
                {
                    m.SetColor("_EmissionColor", (Color)color * glow);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                else
                    m.DisableKeyword("_EMISSION");
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                look.Blockout[i] = m;
            }
            var glowTex = VeinTexture();
            // White: the veins are colored per vertex, by who holds their sites (K3).
            look.VeinMaterial = Additive($"{KilnDir}/Vein.mat", glowTex, new Color(1.6f, 1.6f, 1.6f, 1f));
            look.PointMaterial = Additive($"{KilnDir}/Point.mat", glowTex, new Color(2.2f, 2.2f, 2.2f, 1f));
            // The collision kit's (K1): shields in their faction's colors (as the
            // soldiers': amber, jade, magenta), lifts' columns cool, drop shafts' warm.
            Color[] factions = { new Color(0.92f, 0.62f, 0.10f), new Color(0.12f, 0.66f, 0.50f), new Color(0.78f, 0.22f, 0.72f) };
            look.Shields = new Material[factions.Length];
            for (int i = 0; i < factions.Length; i++)
                look.Shields[i] = Additive($"{KilnDir}/Shield_{i}.mat", glowTex, new Color(factions[i].r * 2f, factions[i].g * 2f, factions[i].b * 2f, 1f));
            look.ShieldNone = Additive($"{KilnDir}/Shield_none.mat", glowTex, new Color(1.25f, 1.3f, 1.35f, 1f));
            look.LiftMaterial = Additive($"{KilnDir}/Lift.mat", glowTex, new Color(0.5f, 1.5f, 2.1f, 1f));
            look.DropMaterial = Additive($"{KilnDir}/Drop.mat", glowTex, new Color(2.1f, 1.1f, 0.45f, 1f));
        }

        static TerrainLayer KilnLayer((string name, string from, Color32 color, float tile, float smooth, float normal) g)
        {
            var path = $"{KilnDir}/{g.name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null)
            {
                l = new TerrainLayer();
                AssetDatabase.CreateAsset(l, path);
            }
            var src = $"{TerrainDir}/{g.from}/{g.from}";
            l.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{src}_albedo.png");
            l.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{src}_normal.jpg");
            l.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{src}_mask.png");
            l.tileSize = new Vector2(g.tile, g.tile);
            l.tileOffset = Vector2.zero;
            l.normalScale = g.normal;
            l.metallic = 0f;
            l.smoothness = g.smooth;
            l.maskMapRemapMin = Vector4.zero;
            l.maskMapRemapMax = new Vector4(0f, 1f, 1f, g.smooth);
            // Recolored: the texture's own mean lands on the target color (linear),
            // its variation kept at 80% around it.
            var mean = MeanColor($"{src}_albedo.png");
            Color target = g.color;
            var lin = target.linear;
            var min = Vector4.zero;
            var max = Vector4.one;
            for (int c = 0; c < 3; c++)
            {
                float t = lin[c], s = Mathf.Max(0.02f, mean.linear[c]);
                min[c] = t * 0.2f;
                max[c] = min[c] + 0.8f * t / s;
            }
            l.diffuseRemapMin = min;
            l.diffuseRemapMax = max;
            EditorUtility.SetDirty(l);
            return l;
        }

        /// <summary>A texture's mean color (sRGB), from its file.</summary>
        static Color MeanColor(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            var px = t.GetPixels32();
            double r = 0, gg = 0, b = 0;
            int n = 0;
            for (int i = 0; i < px.Length; i += 61)
            {
                r += px[i].r;
                gg += px[i].g;
                b += px[i].b;
                n++;
            }
            Object.DestroyImmediate(t);
            return new Color((float)(r / n / 255), (float)(gg / n / 255), (float)(b / n / 255));
        }

        /// <summary>The veins' soft edge: bright in the middle of a ribbon, nothing at
        /// its sides (u), the same all along it (v).</summary>
        static Texture2D VeinTexture()
        {
            var path = $"{KilnDir}/VeinGlow.png";
            const int w = 64, h = 4;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f;
                float a = Mathf.Exp(-u * u * 7f) * 0.75f + Mathf.Exp(-u * u * 60f) * 0.6f;
                for (int y = 0; y < h; y++)
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapModeU = TextureWrapMode.Clamp;
            imp.wrapModeV = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>An additive, unlit material (URP's particles): its color HDR, so
        /// the bloom catches it; vertex colors dim it.</summary>
        static Material Additive(string path, Texture2D tex, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The grass's material and compute shader. Its colors and wind are the
        /// shader's own defaults (Grass.shader), put back on every run.</summary>
        static void Grass(WorldLook look)
        {
            const string path = "Assets/Lattice/World/Grass.mat";
            var shader = Shader.Find("Lattice/Grass");
            if (shader == null)
            {
                Debug.LogWarning("Lattice setup: no Lattice/Grass shader");
                return;
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            LatticeSetup.ShaderDefaults(m);
            EditorUtility.SetDirty(m);
            look.GrassMaterial = m;
            // The scatter's numbers from the code's defaults (the asset keeps old values otherwise).
            var d = ScriptableObject.CreateInstance<WorldLook>();
            (look.GrassDistance, look.GrassThinFrom, look.GrassCell, look.GrassDensity, look.GrassFarKeep, look.GrassHeight, look.GrassWidth) =
                (d.GrassDistance, d.GrassThinFrom, d.GrassCell, d.GrassDensity, d.GrassFarKeep, d.GrassHeight, d.GrassWidth);
            Object.DestroyImmediate(d);
            look.GrassCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Lattice/Shaders/Grass.compute");
            if (look.GrassCompute == null)
                Debug.LogWarning("Lattice setup: no Assets/Lattice/Shaders/Grass.compute");
        }

        static Material LitMaterial(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Lattice/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Lattice/Lit");
            return m;
        }

        /// <summary>The color a model shows on average (sRGB): its texture sampled over
        /// its surface, each triangle by its area (a Meshy atlas spends most of its
        /// space on small parts, so the texture's own mean is far off).</summary>
        static Color AverageColor(string path, Mesh mesh)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            var v = mesh.vertices;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            // Four points per triangle: its centroid and toward each corner.
            Vector3[] bary = { new Vector3(1f / 3, 1f / 3, 1f / 3), new Vector3(2f / 3, 1f / 6, 1f / 6), new Vector3(1f / 6, 2f / 3, 1f / 6), new Vector3(1f / 6, 1f / 6, 2f / 3) };
            double r = 0, g = 0, b = 0, total = 0;
            for (int k = 0; k < tris.Length; k += 3)
            {
                int i0 = tris[k], i1 = tris[k + 1], i2 = tris[k + 2];
                double area = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).magnitude;
                if (area <= 0)
                    continue;
                foreach (var w in bary)
                {
                    var p = uv[i0] * w.x + uv[i1] * w.y + uv[i2] * w.z;
                    var c = t.GetPixelBilinear(p.x, p.y);
                    r += c.r * area;
                    g += c.g * area;
                    b += c.b * area;
                    total += area;
                }
            }
            Object.DestroyImmediate(t);
            if (total <= 0)
                return Color.gray;
            return new Color((float)(r / total), (float)(g / total), (float)(b / total));
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
            var ssao = Feature<ScreenSpaceAmbientOcclusion>(data, "SSAO");
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

        /// <summary>The renderer's feature of type T, added (as a sub-asset) if it has none.</summary>
        static T Feature<T>(UniversalRendererData data, string name) where T : ScriptableRendererFeature
        {
            foreach (var f in data.rendererFeatures)
                if (f is T t)
                    return t;
            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = data.rendererFeatures.Count;
            for (int i = 0; i < data.rendererFeatures.Count; i++)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(data.rendererFeatures[i], out _, out long id);
                map.GetArrayElementAtIndex(i).longValue = id;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return feature;
        }

        /// <summary>The first-person view: drawn by its own pass in the world camera
        /// (<see cref="Lattice.Combat.ViewModelFeature"/>), so the renderer's own
        /// passes leave its layer out.</summary>
        static void ViewModel(UniversalRendererData data)
        {
            Feature<Lattice.Combat.ViewModelFeature>(data, "First-person view").SetActive(true);
            int notView = ~(1 << Lattice.Combat.Gun.ViewLayer);
            data.prepassLayerMask &= notView;
            data.opaqueLayerMask &= notView;
            data.transparentLayerMask &= notView;
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
            // GPU occlusion culling cost more than it saved: open ground hides little
            // (measured in a 1,000-player fight: frame p99 0.15-0.2 ms lower without).
            so.FindProperty("m_GPUResidentDrawerEnableOcclusionCullingInCameras").boolValue = false;
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
                    ViewModel(u);
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
