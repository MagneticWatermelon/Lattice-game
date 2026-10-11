using System.Linq;
using Lattice.Players;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lattice.EditorTools
{
    /// <summary>
    /// The soldier's Unity assets, made from the imported model: faction
    /// materials, the rifle's material, the upper-body mask, the animator
    /// controller, and the look asset tying them together.
    /// </summary>
    public static class LatticeSetupPlayers
    {
        const string Dir = "Assets/Lattice/Players/Soldier";
        const string RifleDir = "Assets/Lattice/Combat/Rifle";
        public const string LookPath = "Assets/Lattice/Players/SoldierLook.asset";
        static readonly string[] FactionNames = { "Ordinate", "Compact", "Resonance" };

        [MenuItem("Lattice/Setup/Players")]
        public static void All()
        {
            var look = AssetDatabase.LoadAssetAtPath<SoldierLook>(LookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<SoldierLook>();
                AssetDatabase.CreateAsset(look, LookPath);
            }
            look.Model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/Soldier.fbx");
            look.Controller = Controller();
            look.Factions = new Material[3];
            for (int f = 0; f < 3; f++)
                look.Factions[f] = SoldierMaterial(FactionNames[f], SoldierLook.FactionColors[f]);
            look.Rifle = AssetDatabase.LoadAllAssetsAtPath($"{RifleDir}/Rifle.fbx").OfType<Mesh>().First();
            look.RifleMaterial = RifleMaterial("Rifle", 0.62f);
            RifleMaterial("RifleView", 0.62f);
            look.Holo = AssetDatabase.LoadAllAssetsAtPath($"{RifleDir}/HoloSight.fbx").OfType<Mesh>().First();
            look.HoloBody = Lit($"{RifleDir}/HoloBody.mat");
            look.HoloBody.SetColor("_BaseColor", new Color(0.045f, 0.05f, 0.055f));
            look.HoloBody.SetFloat("_Metallic", 0.35f);
            look.HoloBody.SetFloat("_Smoothness", 0.32f);
            look.HoloBody.SetFloat("_TintStrength", 0f);
            look.HoloBody.enableInstancing = true;
            EditorUtility.SetDirty(look.HoloBody);
            look.HoloInner = Lit($"{RifleDir}/HoloInner.mat");
            look.HoloInner.SetColor("_BaseColor", new Color(0.012f, 0.012f, 0.013f));
            look.HoloInner.SetFloat("_Metallic", 0f);
            look.HoloInner.SetFloat("_Smoothness", 0.08f);
            look.HoloInner.SetFloat("_TintStrength", 0f);
            look.HoloInner.SetFloat("_EnvironmentReflections", 0f);
            look.HoloInner.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            look.HoloInner.enableInstancing = true;
            EditorUtility.SetDirty(look.HoloInner);
            look.HoloGlass = HoloGlass($"{RifleDir}/HoloGlass.mat", false);
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssets();
            Debug.Log("Lattice setup: players");
        }

        static Material Lit(string path)
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

        static void Maps(Material m, string prefix, float smoothness)
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_albedo.png"));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_normal.png"));
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{prefix}_metal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.enableInstancing = true;
        }

        static Material SoldierMaterial(string faction, Color color)
        {
            var m = Lit($"{Dir}/Soldier_{faction}.mat");
            Maps(m, $"{Dir}/Soldier", 0.9f);
            m.SetColor("_TintColor", color.linear);
            m.SetFloat("_TintStrength", 0.85f);
            // The plates, muted a third toward gray near (grounded, not toy-bright).
            m.SetVector("_TintRange", new Vector4(0.1f, 0.28f, 0.3f, 0f));
            // From ~30 m (texture mip 4.5) to ~160 m (mip 7) the soldier turns to its
            // faction's color, with a glow of it: distinct at 300 m, even backlit.
            m.SetVector("_TintFar", new Vector4(4.5f, 7f, 0.75f, 0.55f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The holographic sight's glass: with its reticle (first person) or without.</summary>
        public static Material HoloGlass(string path, bool reticle)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Lattice/HoloGlass"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find("Lattice/HoloGlass");
            m.SetFloat("_FrontX", 0.17f);
            if (!reticle)
                m.SetColor("_ReticleColor", Color.black);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material RifleMaterial(string name, float smoothness)
        {
            var m = Lit($"{RifleDir}/{name}.mat");
            Maps(m, $"{RifleDir}/{name}", smoothness);
            m.SetFloat("_TintStrength", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Everything from the waist up: arms, chest, head.</summary>
        static AvatarMask UpperBody()
        {
            var path = $"{Dir}/UpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            {
                bool upper = p is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                    or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers or AvatarMaskBodyPart.LeftHandIK or AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(p, upper);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath($"{Dir}/Soldier.fbx").OfType<AnimationClip>().First(c => c.name == name);

        /// <summary>
        /// Body: a locomotion blend by speed (m/s) whose thresholds are the
        /// game's speeds and whose clips play at the rate their feet need
        /// there (measured gaits: walk 1.6, run 5.25, sprint 6.6 m/s), so
        /// nothing slides; jump while airborne; death until respawn.
        /// Aim: the rifle shouldered, over the upper body, weighted by script.
        /// </summary>
        static AnimatorController Controller()
        {
            var path = $"{Dir}/Soldier.controller";
            AssetDatabase.DeleteAsset(path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter(new AnimatorControllerParameter { name = "PlayRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            c.AddParameter("AimRate", AnimatorControllerParameterType.Float);
            c.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var sm = c.layers[0].stateMachine;
            var move = c.CreateBlendTreeInController("Move", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), 2.0f);
            tree.AddChild(Clip("Run"), 6.0f);
            tree.AddChild(Clip("Sprint"), 9.0f);
            var ch = tree.children;
            ch[1].timeScale = 2.0f / 1.6f;
            ch[2].timeScale = 6.0f / 5.25f;
            ch[3].timeScale = 9.0f / 6.6f;
            tree.children = ch;
            move.speedParameterActive = true;
            move.speedParameter = "PlayRate";
            sm.defaultState = move;

            var jump = sm.AddState("Jump");
            jump.motion = Clip("Jump");
            var death = sm.AddState("Death");
            death.motion = Clip("Death");
            death.speed = 1.4f;
            var t = move.AddTransition(jump);
            t.hasExitTime = false;
            t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
            t = jump.AddTransition(move);
            t.hasExitTime = false;
            t.duration = 0.18f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
            t = sm.AddAnyStateTransition(death);
            t.hasExitTime = false;
            t.duration = 0.1f;
            t.canTransitionToSelf = false;
            // The clip staggers upright, arms flung out, for its first 1.4 s (it read as
            // a T-pose) and falls over the next 1: enter past the stagger, played faster,
            // the body is down in under a second.
            t.offset = 0.38f;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            t = death.AddTransition(move);
            t.hasExitTime = false;
            t.duration = 0f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            c.AddLayer("Aim");
            var layers = c.layers;
            layers[1].avatarMask = UpperBody();
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = 0f;
            var aim = layers[1].stateMachine.AddState("Aim");
            aim.motion = Clip("RunAim");
            aim.speedParameterActive = true;
            aim.speedParameter = "AimRate";
            layers[1].stateMachine.defaultState = aim;
            c.layers = layers;
            EditorUtility.SetDirty(c);
            return c;
        }
    }
}
