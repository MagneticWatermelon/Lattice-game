using UnityEngine;

namespace Lattice.Combat
{
    /// <summary>
    /// How fighting looks: the first-person rifle and its motion, tracers,
    /// muzzle flashes and impacts. An asset made by Lattice > Setup
    /// (LatticeSetupCombat), referenced by the scene. Offsets are in the
    /// first-person camera's space (x right, y up, z forward, meters).
    /// </summary>
    [CreateAssetMenu(menuName = "Lattice/Combat Look")]
    public sealed class CombatLook : ScriptableObject
    {
        [Header("First-person rifle")]
        public Mesh ViewRifle;
        public Material ViewRifleMaterial;
        /// <summary>The mesh's rotation into "muzzle +z, top +y" (as the soldier's rifle).</summary>
        public Vector3 ViewRifleEuler = new Vector3(90f, 90f, 0f);
        /// <summary>Its size: the soldiers' (the arms hold it at their own scale).</summary>
        public float ViewScale = 0.85f;
        /// <summary>The first-person camera's vertical field of view (the rifle's own, so it
        /// never stretches with the world's FOV setting), degrees.</summary>
        public float ViewFov = 64f;
        /// <summary>The rifle's center from the hip.</summary>
        public Vector3 Hip = new Vector3(0.17f, -0.22f, 0.46f);
        /// <summary>The rifle's turn from the hip (angled in toward the crosshair), degrees.</summary>
        public Vector3 HipEuler = new Vector3(-3f, -5f, -5f);
        /// <summary>The holographic sight's line of sight, at its rear window, in the rifle's
        /// frame at size 1: aiming down sights puts it on the line of sight,
        /// <see cref="SightDistance"/> from the eye.</summary>
        public Vector3 Sight = new Vector3(0f, 0.224f, -0.198f);
        public float SightDistance = 0.11f;
        /// <summary>The sight's glass in first person: with the reticle.</summary>
        public Material ViewHoloGlass;
        /// <summary>The muzzle, in the rifle's frame at size 1.</summary>
        public Vector3 Muzzle = new Vector3(0f, 0.069f, 0.5f);

        [Header("The classes' weapons (U6)")]
        /// <summary>Each class's first-person weapon by kit (0: <see cref="ViewRifle"/>), as
        /// SoldierLook's: Tools/Blender/weapons_fbx.py. Muzzles and sights are SoldierLook's.</summary>
        public Mesh[] ViewWeapons = new Mesh[9];
        /// <summary>The scope's rear lens on its line of sight (the rifle's frame at size 1),
        /// as <see cref="Sight"/> is the holographic sight's, and how far it zooms. Fully
        /// down it, the view is the scope's picture (the HUD's), not the rifle.</summary>
        public Vector3 ScopeSight = new Vector3(0f, 0.206f, -0.2565f);
        public float ScopeZoom = 3f;

        public Mesh ViewWeaponOf(int id) => id > 0 && id < ViewWeapons.Length && ViewWeapons[id] != null ? ViewWeapons[id] : ViewRifle;

        [Header("The ammo cell")]
        /// <summary>The rifle's cell (Tools/Blender/cell_fbx.py): a canister across the
        /// receiver's round bay that a reload takes out and puts back. Centered on its
        /// own middle, its axis the mesh's y (the rifle's left-right once turned by
        /// <see cref="ViewRifleEuler"/>); submeshes body, trim, glow.</summary>
        public Mesh Cell;
        public Material CellBody, CellTrim, CellGlow;
        /// <summary>The bay's center in the rifle's frame at size 1, and the cell's
        /// half-length (its caps' faces, out along the frame's x).</summary>
        public Vector3 CellSeat = new Vector3(0f, -0.0716f, -0.1887f);
        public float CellHalf = 0.0962f;
        /// <summary>The charge's glow at full: the faction's color times this (HDR, for the bloom).</summary>
        public float CellGlowStrength = 1.1f;

        [Header("First-person arms")]
        /// <summary>The soldier's arms alone (Combat/Arms), held by <see cref="ViewArms"/>.</summary>
        public GameObject Arms;
        /// <summary>The left hand on the first-person rifle (its frame, m at size 1; as
        /// SoldierLook's grips): the wrist under the handguard's front, the fingers curling up
        /// its left side, the back of the hand out and down. Seen from the eye, not from
        /// outside: further along and lower than a soldier's.</summary>
        public Vector3 ViewLeftGrip = new Vector3(-0.05f, -0.06f, 0.2f);
        public Vector3 ViewLeftFingers = new Vector3(0.35f, 0.45f, 0.82f);
        public Vector3 ViewLeftBack = new Vector3(-0.7f, -0.7f, 0.1f);
        /// <summary>Where the collarbones are rooted (by the neck) and the elbows' pull, in the
        /// first-person camera's space (m); how far the shoulders swing toward the hands (0..1).</summary>
        public Vector3 RightCollar = new Vector3(0.04f, -0.24f, -0.02f);
        public Vector3 LeftCollar = new Vector3(-0.22f, -0.42f, -0.12f);
        public Vector3 RightElbow = new Vector3(0.55f, -0.6f, 0.05f);
        public Vector3 LeftElbow = new Vector3(-0.55f, -0.75f, 0.2f);
        public float ShoulderSwing = 0.5f;
        /// <summary>Sprinting: the rifle lowered and turned in.</summary>
        public Vector3 SprintOffset = new Vector3(0.05f, -0.09f, 0.04f);
        public Vector3 SprintEuler = new Vector3(8f, -38f, 28f);
        /// <summary>A shot's kick: back (m) and up (degrees), from the hip and down the sights.</summary>
        public Vector2 KickHip = new Vector2(0.045f, 4.2f);
        public Vector2 KickAds = new Vector2(0.022f, 1.6f);
        public float KickTau = 0.075f;
        /// <summary>How much of a weapon's kick reaches the view fully down a scope.</summary>
        public float ScopedKick = 0.6f;

        [Header("Effects")]
        public Material Tracer;
        public Material Flash;
        /// <summary>Impact puffs (alpha-blended) and sparks (added), on a soft puff texture.</summary>
        public Material Dust;
        public Material Sparks;
    }
}
