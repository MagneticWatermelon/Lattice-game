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
        /// <summary>The classes' other weapons (U6), ours and others' near: the carbine,
        /// the shotgun, the marksman rifle, the SMG (far shots share the rifle's).</summary>
        public AudioClip[] CarbineOwn, CarbineNear, ShotgunOwn, ShotgunNear, MarksmanOwn, MarksmanNear, SmgOwn, SmgNear;

        /// <summary>The LMG, the sniper rifle, the Exo's twin guns and launcher.</summary>
        public AudioClip[] LmgOwn, LmgNear, SniperOwn, SniperNear, ExoGunsOwn, ExoGunsNear, LauncherOwn, LauncherNear;

        /// <summary>A shot by weapon <paramref name="id"/> (<c>LT_WEAPON_*</c>): ours (in the head) or someone's near.</summary>
        public AudioClip[] Shot(int id, bool own) => id switch
        {
            1 => own ? CarbineOwn : CarbineNear,
            2 => own ? ShotgunOwn : ShotgunNear,
            3 => own ? MarksmanOwn : MarksmanNear,
            4 => own ? SmgOwn : SmgNear,
            5 => Or(own ? LmgOwn : LmgNear, own ? ShotOwn : ShotNear),
            6 => Or(own ? SniperOwn : SniperNear, own ? MarksmanOwn : MarksmanNear),
            7 => Or(own ? ExoGunsOwn : ExoGunsNear, own ? SmgOwn : SmgNear),
            8 => Or(own ? LauncherOwn : LauncherNear, own ? ShotgunOwn : ShotgunNear),
            _ => own ? ShotOwn : ShotNear,
        };

        static AudioClip[] Or(AudioClip[] a, AudioClip[] b) => a != null && a.Length > 0 ? a : b;

        /// <summary>A shot's pitch by weapon (1: as recorded).</summary>
        public float ShotPitch(int id) => 1f;

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

        [Header("Abilities (U6)")]
        /// <summary>The jets' roar while they burn (a loop) and their lighting; a Bulwark
        /// rising; a Mend field opening and a heal landing; cloaking and uncloaking.</summary>
        public AudioClip JetsLoop, JetsIgnite, BulwarkOn, MendOn, MendTick, CloakOn, CloakOff;
        /// <summary>An Exo's Lock-down: planting, and standing up again.</summary>
        public AudioClip LockDown, LockUp;
        /// <summary>Their level, ours; others' within <see cref="HandlingDistance"/> × 2.</summary>
        [Range(0f, 1f)] public float Ability = 0.6f;

        [Header("Gadgets (U6)")]
        /// <summary>Bursts: a frag, C4, a mine, the Exo launcher's shell.</summary>
        public AudioClip[] BlastFrag, BlastC4, BlastMine, BlastShell;
        /// <summary>A throw, something landing or set down, a repair's and a hack's beats, a turret's shots.</summary>
        public AudioClip[] Throw, GadgetLand, RepairTick, HackTick, TurretNear;
        /// <summary>An armed thing's beep, smoke going, a turret built, a revive, a turret
        /// taken over, a dart listening, an ammo pack's refill reaching us.</summary>
        public AudioClip Beep, SmokePop, TurretUp, Revive, Hacked, DartPing, AmmoTick;
        /// <summary>Gadgets' level (their bursts are louder still: <see cref="Blasts"/>).</summary>
        [Range(0f, 1f)] public float Gadgets = 0.7f, Blasts = 1f;

        /// <summary>A burst of gadget kind <paramref name="kind"/> (<c>LT_GADGET_*</c>).</summary>
        public AudioClip[] Blast(int kind) => kind switch
        {
            3 => BlastC4,
            4 => BlastMine,
            8 => BlastShell,
            _ => BlastFrag,
        };

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
