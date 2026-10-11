using System.Linq;
using Lattice.Audio;
using UnityEditor;
using UnityEngine;

namespace Lattice.EditorTools
{
    /// <summary>
    /// The fight's sound: the clips (synthesized offline into Audio/Clips) gathered
    /// into the audio look, imported for low latency (short clips decoded in
    /// memory, the long beds compressed).
    /// </summary>
    public static class LatticeSetupAudio
    {
        public const string LookPath = "Assets/Lattice/Audio/AudioLook.asset";
        const string Clips = "Assets/Lattice/Audio/Clips";

        [MenuItem("Lattice/Setup/Audio")]
        public static void All()
        {
            var look = AssetDatabase.LoadAssetAtPath<AudioLook>(LookPath);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<AudioLook>();
                AssetDatabase.CreateAsset(look, LookPath);
            }
            look.ShotOwn = Many("Shot_Own");
            look.ShotNear = Many("Shot_Near");
            look.ShotFar = Many("Shot_Far");
            look.Crack = Many("Crack");
            look.ImpactDirt = Many("Impact_Dirt");
            look.ImpactHard = Many("Impact_Hard");
            look.Footstep = Many("Footstep");
            look.Hurt = Many("Hurt");
            look.HitBody = One("Hit_Body");
            look.HitHead = One("Hit_Head");
            look.Kill = One("Kill");
            look.Death = One("Death");
            look.Respawn = One("Respawn");
            look.SightsUp = One("Sights_Up");
            look.SightsDown = One("Sights_Down");
            look.Wind = One("Wind");
            look.BattleSmall = One("Battle_Small");
            look.BattleLarge = One("Battle_Large");
            EditorUtility.SetDirty(look);
            AssetDatabase.SaveAssets();
            Debug.Log($"Lattice setup: audio ({look.ShotOwn.Length + look.ShotNear.Length + look.ShotFar.Length} shot clips)");
        }

        static AudioClip One(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Clips}/{name}.wav");

        static AudioClip[] Many(string prefix) =>
            AssetDatabase.FindAssets($"t:AudioClip {prefix}_", new[] { Clips })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(prefix + "_"))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .ToArray();
    }

    /// <summary>Import rules for the game's sounds: one-shots decoded in memory (no
    /// decode latency on a shot), the long beds and wind compressed.</summary>
    sealed class AudioRules : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Lattice/Audio/"))
                return;
            var a = (AudioImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool bed = name == "Wind" || name.StartsWith("Battle_");
            var s = a.defaultSampleSettings;
            s.loadType = bed ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = bed ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            s.quality = 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = true;
            a.defaultSampleSettings = s;
            a.forceToMono = false;
            a.loadInBackground = bed;
        }
    }
}
