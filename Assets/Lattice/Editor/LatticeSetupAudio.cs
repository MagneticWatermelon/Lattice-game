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
            (look.CarbineOwn, look.CarbineNear) = (Many("Carbine_Own"), Many("Carbine_Near"));
            (look.ShotgunOwn, look.ShotgunNear) = (Many("Shotgun_Own"), Many("Shotgun_Near"));
            (look.MarksmanOwn, look.MarksmanNear) = (Many("Marksman_Own"), Many("Marksman_Near"));
            (look.SmgOwn, look.SmgNear) = (Many("Smg_Own"), Many("Smg_Near"));
            (look.LmgOwn, look.LmgNear) = (Many("Lmg_Own"), Many("Lmg_Near"));
            (look.SniperOwn, look.SniperNear) = (Many("Sniper_Own"), Many("Sniper_Near"));
            (look.ExoGunsOwn, look.ExoGunsNear) = (Many("ExoGuns_Own"), Many("ExoGuns_Near"));
            (look.LauncherOwn, look.LauncherNear) = (Many("Launcher_Own"), Many("Launcher_Near"));
            (look.LockDown, look.LockUp) = (One("Lock_Down"), One("Lock_Up"));
            (look.BlastFrag, look.BlastC4, look.BlastMine, look.BlastShell) = (Many("Blast_Frag"), Many("Blast_C4"), Many("Blast_Mine"), Many("Blast_Shell"));
            (look.Throw, look.GadgetLand, look.RepairTick, look.HackTick) = (Many("Throw"), Many("Gadget_Land"), Many("Repair_Tick"), Many("Hack_Tick"));
            look.TurretNear = Many("Turret_Near");
            (look.Beep, look.SmokePop, look.TurretUp, look.Revive, look.Hacked, look.DartPing, look.AmmoTick) =
                (One("Beep"), One("Smoke_Pop"), One("Turret_Up"), One("Revive"), One("Hacked"), One("Dart_Ping"), One("Ammo_Tick"));
            look.Crack = Many("Crack");
            look.ImpactDirt = Many("Impact_Dirt");
            look.ImpactHard = Many("Impact_Hard");
            look.Footstep = Many("Footstep");
            look.ReedChime = Many("Reed_Chime");
            look.Hurt = Many("Hurt");
            look.HitBody = One("Hit_Body");
            look.HitHead = One("Hit_Head");
            look.Kill = One("Kill");
            look.Death = One("Death");
            look.Respawn = One("Respawn");
            look.SightsUp = One("Sights_Up");
            look.SightsDown = One("Sights_Down");
            look.CellRelease = One("Cell_Release");
            look.CellOut = One("Cell_Out");
            look.CellPouch = One("Cell_Pouch");
            look.CellIn = One("Cell_In");
            look.CellSeat = One("Cell_Seat");
            look.CellCharge = One("Cell_Charge");
            look.CellClink = Many("Cell_Clink");
            look.DryFire = One("Dry_Fire");
            look.CrouchDown = One("Crouch_Down");
            look.CrouchUp = One("Crouch_Up");
            look.JetsLoop = One("Jets_Loop");
            look.JetsIgnite = One("Jets_Ignite");
            look.BulwarkOn = One("Bulwark_On");
            look.MendOn = One("Mend_On");
            look.MendTick = One("Mend_Tick");
            look.CloakOn = One("Cloak_On");
            look.CloakOff = One("Cloak_Off");
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
