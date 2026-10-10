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
        public Material RifleMaterial;
        /// <summary>The rifle model's muzzle, grip and foregrip, in its own space (m).</summary>
        public Vector3 RifleMuzzle = new Vector3(0f, 0.06f, 0.53f);
        public Vector3 RifleGrip = new Vector3(0f, -0.02f, -0.12f);
        public Vector3 RifleForegrip = new Vector3(0f, -0.01f, 0.18f);
        /// <summary>The rifle mesh's rotation into "muzzle along +z, top along +y" (its muzzle is at -x, its top at +z: LookRotation(up, left)).</summary>
        public Vector3 RifleEuler = new Vector3(270f, 90f, 0f);
        /// <summary>The rifle's size (its model is 1 m long; offsets above are at size 1).</summary>
        public float RifleScale = 0.85f;
        /// <summary>The model is scaled so its head sits on the head hitbox (1.6 m).</summary>
        public float ModelScale = 0.95f;

        [Header("Rig (tuned by eye)")]
        /// <summary>Where the rifle's stock sits, from the right upper arm, in the aim's frame (m).</summary>
        public Vector3 ShoulderPocket = new Vector3(-0.13f, 0.06f, 0.06f);
        /// <summary>Hands on the rifle: rotations from the aim's frame (degrees).</summary>
        public Vector3 RightHandEuler = new Vector3(0f, 0f, -90f);
        public Vector3 LeftHandEuler = new Vector3(0f, 0f, 90f);
        /// <summary>Where the elbows point, from each upper arm, in the aim's frame (m).</summary>
        public Vector3 RightElbowHint = new Vector3(0.35f, -0.35f, -0.2f);
        public Vector3 LeftElbowHint = new Vector3(-0.3f, -0.45f, 0.1f);
        /// <summary>The most the upper body twists from the legs, degrees.</summary>
        public float MaxTwist = 75f;
        /// <summary>LOD screen heights (fraction of the screen) below which each LOD gives way.</summary>
        public float[] LodHeights = { 0.30f, 0.11f, 0.035f, 0.004f };

        [Header("Gait")]
        /// <summary>The speeds (m/s) each clip's feet move at, measured from the clips:
        /// they set playback rates so feet don't slide.</summary>
        public float WalkSpeed = 1.6f, RunSpeed = 5.25f, SprintSpeed = 6.6f;

        public static readonly Color[] FactionColors =
        {
            new Color(0.79f, 0.51f, 0.09f), // Ordinate: amber
            new Color(0.14f, 0.57f, 0.44f), // Free Compact: jade
            new Color(0.72f, 0.20f, 0.50f), // Resonance: magenta
        };
    }
}
