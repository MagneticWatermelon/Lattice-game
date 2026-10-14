using UnityEngine;

namespace Lattice.Audio
{
    /// <summary>
    /// How the fight sounds: the clips (synthesized offline, Audio/Clips) and
    /// the mix. An asset made by Lattice > Setup (LatticeSetupAudio),
    /// referenced by the scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Lattice/Audio Look")]
    public sealed class AudioLook : ScriptableObject
    {
        [Header("Shots")]
        /// <summary>Our rifle (stereo, played in the head), others' near and far.</summary>
        public AudioClip[] ShotOwn, ShotNear, ShotFar;
        /// <summary>A slug passing close: the supersonic crack.</summary>
        public AudioClip[] Crack;
        public AudioClip[] ImpactDirt, ImpactHard;

        [Header("Us")]
        public AudioClip[] Footstep, Hurt;
        public AudioClip HitBody, HitHead, Kill, Death, Respawn, SightsUp, SightsDown;

        [Header("Handling")]
        /// <summary>The reload's sounds (the cell released, pulled out, the hand at the
        /// pouch, the fresh one slid in, seated, charged from empty), the trigger pulled
        /// dry, a spent cell landing, crouching down and getting up.</summary>
        public AudioClip CellRelease, CellOut, CellPouch, CellIn, CellSeat, CellCharge, DryFire, CrouchDown, CrouchUp;
        public AudioClip[] CellClink;

        [Header("Beds")]
        public AudioClip Wind, BattleSmall, BattleLarge;

        [Header("Life (Kiln, K4)")]
        /// <summary>Glass reeds touching in the wind: heard round the listener in the
        /// Glasslands' reed fields, and more as someone pushes through them.</summary>
        public AudioClip[] ReedChime;
        /// <summary>Chimes a second in a full field with no one in it, and more for each
        /// m/s the local player pushes through.</summary>
        public float ChimesInWind = 0.9f, ChimesPerSpeed = 0.45f;

        [Header("Mix")]
        [Range(0f, 1f)] public float Master = 0.9f;
        [Range(0f, 1f)] public float OwnShot = 0.8f, Shots = 0.8f, Feedback = 0.7f, Footsteps = 0.45f, Ambience = 0.28f, Battle = 0.55f;
        /// <summary>Handling sounds (the reload, the trigger, crouching): ours, and others'
        /// within <see cref="HandlingDistance"/> (m).</summary>
        [Range(0f, 1f)] public float Handling = 0.6f;
        public float HandlingDistance = 18f;
        /// <summary>Distance (m) inside which a shot plays at full level; past it, inverse distance.</summary>
        public float ShotNearDistance = 10f;
        /// <summary>Where the far shot's sound takes over from the near one, and where shots go unheard
        /// (the battle bed carries what's farther).</summary>
        public float FarFrom = 70f, Audible = 900f;
        /// <summary>Far shots heard one by one: within this distance, at most so many a second.</summary>
        public float FarIndividually = 450f, FarPerSecond = 10f;
        /// <summary>Air takes the top off with distance: the low-pass corner's e-folding distance (m).</summary>
        public float AirDistance = 140f;
        /// <summary>The speed of sound, m/s: distant shots arrive late.</summary>
        public float SoundSpeed = 343f;
    }
}
