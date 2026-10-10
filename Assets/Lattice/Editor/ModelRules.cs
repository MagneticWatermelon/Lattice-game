using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Lattice.EditorTools
{
    /// <summary>
    /// Import rules for the game's models. The soldier (Players/Soldier) is a
    /// humanoid: its clips loop where they cycle, and every clip stays in
    /// place (root motion baked into the pose): only the server moves
    /// soldiers. No model brings its own materials; the client makes them.
    /// </summary>
    sealed class ModelRules : AssetPostprocessor
    {
        static readonly string[] Loops = { "Idle", "Walk", "Run", "RunAim", "Sprint" };

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Lattice/"))
                return;
            var m = (ModelImporter)assetImporter;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.isReadable = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.useFileScale = true;
            m.globalScale = 1f;
            m.bakeAxisConversion = true;
            if (assetPath.Contains("/Players/Soldier/"))
            {
                m.animationType = ModelImporterAnimationType.Human;
                m.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                m.optimizeGameObjects = false;
                m.importAnimation = true;
                m.animationCompression = ModelImporterAnimationCompression.Optimal;
                m.resampleCurves = true;
            }
            else
            {
                m.animationType = ModelImporterAnimationType.None;
                m.importAnimation = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Contains("/Players/Soldier/"))
                return;
            var m = (ModelImporter)assetImporter;
            var clips = m.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                return;
            foreach (var c in clips)
            {
                // Takes come out as "Soldier|Idle" or "Idle": the clip is the last part.
                var name = c.takeName.Split('|').Last();
                c.name = name;
                bool loop = Loops.Contains(name);
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = false; // root rotation by the body: every clip faces the way the model does
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                c.heightFromFeet = false;
            }
            m.clipAnimations = clips;
        }
    }
}
