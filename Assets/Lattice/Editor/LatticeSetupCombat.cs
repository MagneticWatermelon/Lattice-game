using System.IO;
using System.Linq;
using Lattice.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.EditorTools
{
    /// <summary>The combat look: the first-person rifle, tracer, flash and impact materials, the viewmodel layer.</summary>
    public static class LatticeSetupCombat
    {
        public const string LookPath = "Assets/Lattice/Combat/CombatLook.asset";
        const string Dir = "Assets/Lattice/Combat";

        [MenuItem("Lattice/Setup/Combat")]
        public static void All()
        {
            Layer(Gun.ViewLayer, "Viewmodel");
            var look = AssetDatabase.LoadAssetAtPath<CombatLook>(LookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<CombatLook>();
                AssetDatabase.CreateAsset(look, LookPath);
            }
            look.ViewRifle = AssetDatabase.LoadAllAssetsAtPath($"{Dir}/Rifle/RifleView.fbx").OfType<Mesh>().First();
            look.ViewRifleMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Rifle/RifleView.mat");
            look.Arms = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Arms/SoldierArms.fbx");
            look.ViewHoloGlass = LatticeSetupPlayers.HoloGlass($"{Dir}/Rifle/HoloGlassView.mat", true);
            look.Tracer = ShaderMaterial($"{Dir}/Tracer.mat", "Lattice/Tracer");
            look.Flash = ShaderMaterial($"{Dir}/Flash.mat", "Lattice/Flash");
            look.Flash.enableInstancing = true;
            var puff = Puff();
            look.Dust = Particles($"{Dir}/ImpactDust.mat", puff, false);
            look.Sparks = Particles($"{Dir}/ImpactSparks.mat", puff, true);
            look.Sparks.SetColor("_BaseColor", new Color(4f, 2.4f, 1.1f, 1f));
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssets();
            Debug.Log("Lattice setup: combat");
        }

        static void Layer(int index, string name)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(index).stringValue = name;
            tags.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material ShaderMaterial(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP's particle shader, alpha-blended or added, soft-edged.</summary>
        static Material Particles(string path, Texture2D tex, bool additive)
        {
            var m = ShaderMaterial(path, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            // (As URP's own validation sets it: anything else and it rewrites the asset.)
            m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive)
                m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A soft, lumpy puff (64 px) for dust and sparks.</summary>
        static Texture2D Puff()
        {
            var path = $"{Dir}/Puff_albedo.png";
            if (!File.Exists(path))
            {
                const int S = 64;
                var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.6f + Mathf.PerlinNoise(x * 0.27f + 7f, y * 0.27f + 3f) * 0.4f;
                    float a = Mathf.Clamp01(1f - r * (1.15f - 0.35f * n));
                    a = a * a * (3f - 2f * a);
                    byte b = (byte)(255 * Mathf.Clamp01(0.85f + 0.15f * n));
                    px[y * S + x] = new Color32(b, b, b, (byte)(255 * a));
                }
                t.SetPixels32(px);
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
