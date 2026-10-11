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
