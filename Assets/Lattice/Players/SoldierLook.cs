using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// How soldiers look and move: the model, its animator, the faction
    /// materials, the rifle, and the measured gait of each clip. An asset made
    /// by Lattice > Setup (LatticeSetupPlayers), referenced by the scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Lattice/Soldier Look")]
    public sealed class SoldierLook : ScriptableObject
    {
        public GameObject Model;
        public RuntimeAnimatorController Controller;
        /// <summary>One per faction (Ordinate, Free Compact, Resonance): the
        /// faction's color on the armor plates.</summary>
        public Material[] Factions = new Material[3];
        public Mesh Rifle;
        /// <summary>The crowd's rifle: the same, 600 triangles (Tools/Blender/rifle_fbx.py),
        /// for soldiers tens of pixels tall.</summary>
        public Mesh RifleLow;
        public Material RifleMaterial;
        /// <summary>The holographic sight on the rifle's rail (modeled in the rifle mesh's
        /// space): its body, the hood's matte inside, and its glass as others see it (no reticle).</summary>
        public Mesh Holo;
        public Material HoloBody, HoloInner, HoloGlass;
        /// <summary>The rifle's muzzle in its frame (m at size 1: muzzle +z, top +y; its
        /// pistol grip is at the back, under the rear sight).</summary>
        public Vector3 RifleMuzzle = new Vector3(0f, 0.069f, 0.5f);
        /// <summary>The rifle mesh's rotation into that frame (the mesh's muzzle is at -x, its top at -z).</summary>
        public Vector3 RifleEuler = new Vector3(90f, 90f, 0f);
        /// <summary>The rifle's size (its model is 1 m long; offsets above are at size 1).</summary>
        public float RifleScale = 0.85f;
        [Header("The weapons (U6: Tools/Blender/weapons_fbx.py), by id (LT_WEAPON_*)")]
        /// <summary>Each weapon (0: the rifle above; the carbine, the shotgun, the marksman
        /// rifle, the SMG, the LMG, the sniper rifle; the Exo's are on its arms, none here),
        /// in the rifle's frame: the same receiver, cell and grip, so hands and reloads hold
        /// them all alike. Their crowd's light copies.</summary>
        public Mesh[] Weapons = new Mesh[9];
        public Mesh[] WeaponsLow = new Mesh[9];
        /// <summary>Each one's muzzle (m at size 1, the rifle's frame), as the script prints them.</summary>
        public Vector3[] Muzzles =
        {
            new Vector3(0f, 0.069f, 0.5f), new Vector3(0f, 0.069f, 0.3822f), new Vector3(0f, 0.069f, 0.4205f),
            new Vector3(0f, 0.069f, 0.7885f), new Vector3(0f, 0.069f, 0.283f), new Vector3(0f, 0.069f, 0.6165f),
            new Vector3(0f, 0.069f, 0.9405f), new Vector3(0f, 0.069f, 0.5f), new Vector3(0f, 0.069f, 0.5f),
        };
        /// <summary>The scope (the marksman and sniper rifles'), in place of the holographic
        /// sight (the same space and materials: body, lens caps, glass).</summary>
        public Mesh Scope;
        /// <summary>The Exo's armor plates (Lattice/Lit, dark metal; tinted toward the faction per soldier).</summary>
        public Material ExoPlate;

        [Header("The classes' abilities (U6), as others see them")]
        /// <summary>A Bulwark's shell (its faction's color set per soldier), a cloaked soldier's
        /// shimmer (all it's drawn with), the jets' flame, a Mend field on the ground.</summary>
        public Material BulwarkGlow, CloakGlow, JetFlame, MendField;
        /// <summary>A Bulwark's shell round the body: its radii (m: across, up), its middle's height.</summary>
        public Vector2 ShellRadii = new Vector2(0.62f, 1.05f);
        public float ShellHeight = 0.95f;

        public Mesh WeaponOf(int id) => id > 0 && id < Weapons.Length && Weapons[id] != null ? Weapons[id] : Rifle;
        public Mesh LowOf(int id) => id > 0 && id < WeaponsLow.Length && WeaponsLow[id] != null ? WeaponsLow[id] : RifleLow != null ? RifleLow : Rifle;
        public Vector3 MuzzleOf(int id) => id >= 0 && id < Muzzles.Length ? Muzzles[id] : RifleMuzzle;
        public bool Scoped(int id) => Lattice.Native.Weapons.Scoped(id) && Scope != null;
        public Mesh SightOf(int id) => Scoped(id) ? Scope : Holo;

        /// <summary>The model is scaled so its head sits on the head hitbox (1.6 m).</summary>
        public float ModelScale = 0.95f;

        [Header("Rig (tuned by eye)")]
        /// <summary>Where the rifle's stock sits, from the right upper arm, in the aim's frame (m).</summary>
        public Vector3 ShoulderPocket = new Vector3(-0.15f, -0.07f, 0.24f);
        /// <summary>The hands on the rifle (the first-person arms hold it the same way),
        /// in the rifle's frame (m at size 1): each wrist, the way its fingers point
        /// and the way the back of the hand faces. The right hand holds the pistol
        /// grip, back of the hand to the right; the left lies along the handguard's
        /// left side, fingers curling under it.</summary>
        public Vector3 RightGrip = new Vector3(0.059f, -0.071f, -0.49f);
        public Vector3 RightFingers = new Vector3(0f, -0.39f, 0.92f);
        public Vector3 RightBack = new Vector3(1f, 0f, 0f);
        public Vector3 LeftGrip = new Vector3(-0.117f, 0.037f, 0.084f);
        public Vector3 LeftFingers = new Vector3(0.15f, -0.55f, 0.82f);
        public Vector3 LeftBack = new Vector3(-0.8f, 0.5f, 0f);

        /// <summary>A hand's grip (forward along the fingers, up out of its back) on a rifle turned <paramref name="frame"/>.</summary>
        public static Quaternion Grip(Quaternion frame, Vector3 fingers, Vector3 back) =>
            Quaternion.LookRotation(frame * fingers, frame * back);
        /// <summary>Where the backs of the hands face in the model's bind pose, in its body's
        /// frame there (x right, y up, z forward): measured from the model.</summary>
        public Vector3 RightBindBack = new Vector3(-0.11f, 0.76f, 0.64f);
        public Vector3 LeftBindBack = new Vector3(0.12f, 0.78f, 0.61f);
        /// <summary>Where the elbows point, from each upper arm, in the aim's frame (m).</summary>
        public Vector3 RightElbowHint = new Vector3(0.35f, -0.35f, -0.2f);
        public Vector3 LeftElbowHint = new Vector3(-0.3f, -0.45f, 0.1f);
        /// <summary>The most the upper body twists from the legs, degrees.</summary>
        public float MaxTwist = 75f;
        /// <summary>How much of the sprint clip's vertical bounce the hips keep (1: all of it;
        /// a walk keeps all of its own, the share falling to this from 5 to 8.5 m/s).</summary>
        public float SprintBounce = 0.4f;
        /// <summary>How far (m) the hips may sway to either side of the soldier's path (a
        /// clip that drifts further is brought back).</summary>
        public float HipSway = 0.04f;

        [Header("Crouch (there's no crouch clip: the hips go down, the legs fold by IK)")]
        /// <summary>The hips' height (m) crouched still (a kneel, the right knee down) and
        /// moving (a low walk): set so the head sits on the crouched head hitbox.</summary>
        public float CrouchHipsStill = 0.52f, CrouchHipsMoving = 0.58f;
        /// <summary>How far the body leans forward, still and moving, degrees.</summary>
        public float CrouchLeanStill = 8f, CrouchLeanMoving = 18f;
        /// <summary>The kneel's feet, from the hips' point on the ground (m: forward, right,
        /// up): the left foot forward and flat, the right one back on its toes.</summary>
        public Vector3 KneelLeftFoot = new Vector3(0.24f, -0.13f, 0.09f), KneelRightFoot = new Vector3(-0.35f, 0.12f, 0.12f);
        /// <summary>LOD screen heights (fraction of the screen) below which each LOD gives way.</summary>
        public float[] LodHeights = { 0.30f, 0.11f, 0.035f, 0.004f };
        /// <summary>The levels of detail that get the full rig: the hands on the rifle and
        /// the aim spread down the spine. Past them a soldier is under ~70 px tall (a
        /// hand a pixel or two): the upper body turns from one bone, the hands stay
        /// where the aim layer holds them, at a fifth of the cost.</summary>
        public int FullRigLods = 2;

        [Header("Gait")]
        /// <summary>The speeds (m/s) each clip's feet move at, measured from the clips:
        /// they set playback rates so feet don't slide.</summary>
        public float WalkSpeed = 1.6f, RunSpeed = 5.25f, SprintSpeed = 6.6f;

        /// <summary>Picked to stay apart at range, in sun and in shade (GDD: distinct at
        /// 300 m): shaded and small, a redder amber and a redder magenta both read as
        /// red, so amber leans gold and magenta leans violet.</summary>
        public static readonly Color[] FactionColors =
        {
            new Color(0.92f, 0.62f, 0.10f), // Ordinate: amber
            new Color(0.12f, 0.66f, 0.50f), // Free Compact: jade
            new Color(0.78f, 0.22f, 0.72f), // Resonance: magenta
        };
    }
}
