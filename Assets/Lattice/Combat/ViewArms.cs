using Lattice.Players;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Combat
{
    /// <summary>
    /// The player's arms in first person: the soldier's own arms from the
    /// collarbones out (cut from its model, skinned only to those bones), drawn
    /// with the first-person rifle and holding it the way soldiers do (the same
    /// grips). Each frame both hands go onto the rifle: the collarbone, rooted
    /// at the neck (fixed to the view), swings the shoulder toward the hand,
    /// then two-bone IK reaches it, the elbow toward its hint. If the rifle is
    /// held out beyond reach, the collar gives way forward.
    /// </summary>
    public sealed class ViewArms
    {
        sealed class Arm
        {
            public Transform Collar, Upper, Lower, Hand;
            public Quaternion CollarBind, UpperBind, LowerBind, HandBind;
            public HandGrip Grip;
            public float CollarLength, Reach;
        }

        readonly GameObject root;
        readonly Renderer[] renderers;
        readonly Arm right, left;
        Material body;

        public ViewArms(GameObject model, Transform parent, int layer, SoldierLook soldier)
        {
            root = Object.Instantiate(model, parent, false);
            root.name = "First-person arms";
            root.transform.localScale = Vector3.one * soldier.ModelScale;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
            renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
                if (r is SkinnedMeshRenderer s)
                {
                    s.updateWhenOffscreen = true;
                    s.skinnedMotionVectors = false;
                }
            }
            Transform Find(string name)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                        return t;
                throw new MissingReferenceException($"first-person arms: no bone {name}");
            }
            right = Bind(Find("RightShoulder"), Find("RightArm"), Find("RightForeArm"), Find("RightHand"));
            left = Bind(Find("LeftShoulder"), Find("LeftArm"), Find("LeftForeArm"), Find("LeftHand"));
            // The body's frame as bound: right from shoulder to shoulder, up from hips to head.
            var bodyRight = (right.Upper.position - left.Upper.position).normalized;
            var bodyUp = Find("Head").position - Find("Hips").position;
            right.Grip = HandGrip.FromBind(right.Lower, right.Hand, HandGrip.InBody(soldier.RightBindBack, bodyRight, bodyUp));
            left.Grip = HandGrip.FromBind(left.Lower, left.Hand, HandGrip.InBody(soldier.LeftBindBack, bodyRight, bodyUp));
            root.SetActive(false);
        }

        static Arm Bind(Transform collar, Transform upper, Transform lower, Transform hand) => new Arm
        {
            Collar = collar,
            Upper = upper,
            Lower = lower,
            Hand = hand,
            CollarBind = collar.localRotation,
            UpperBind = upper.localRotation,
            LowerBind = lower.localRotation,
            HandBind = hand.localRotation,
            CollarLength = (upper.position - collar.position).magnitude,
            Reach = (lower.position - upper.position).magnitude + (hand.position - lower.position).magnitude,
        };

        public void Show(bool on)
        {
            if (root.activeSelf != on)
                root.SetActive(on);
        }

        /// <summary>The sleeves' material: our soldier's (SoldierColors).</summary>
        public void Body(Material mat)
        {
            if (mat == body || mat == null)
                return;
            body = mat;
            foreach (var r in renderers)
                r.sharedMaterial = mat;
        }

        /// <summary>
        /// Both hands onto the rifle (world): each collar rooted where it sits,
        /// swinging the shoulder <paramref name="swing"/> of the way toward the
        /// hand; each wrist at its grip, the hand turned to hold it, the elbow
        /// toward its hint.
        /// </summary>
        public void Hold(Vector3 rCollar, Vector3 rElbow, Vector3 rWrist, Quaternion rGrip,
                         Vector3 lCollar, Vector3 lElbow, Vector3 lWrist, Quaternion lGrip, float swing)
        {
            Reach(right, rCollar, rElbow, rWrist, rGrip, swing);
            Reach(left, lCollar, lElbow, lWrist, lGrip, swing);
        }

        static void Reach(Arm a, Vector3 collar, Vector3 elbow, Vector3 wrist, Quaternion grip, float swing)
        {
            // From the bind pose every frame, so the solve never drifts.
            a.Collar.localRotation = a.CollarBind;
            a.Upper.localRotation = a.UpperBind;
            a.Lower.localRotation = a.LowerBind;
            a.Hand.localRotation = a.HandBind;
            // Out of reach even with the shoulder swung all the way: the collar comes forward.
            var toWrist = wrist - collar;
            float max = (a.CollarLength + a.Reach) * 0.985f;
            if (toWrist.sqrMagnitude > max * max)
                collar = wrist - toWrist.normalized * max;
            a.Collar.position = collar;
            // The shoulder swings toward the hand: part way, or as far as it must to reach.
            var bindDir = (a.Upper.position - collar).normalized;
            var wantDir = (wrist - collar).normalized;
            float k = swing;
            if ((wrist - (collar + Vector3.Slerp(bindDir, wantDir, k) * a.CollarLength)).magnitude > a.Reach * 0.985f)
                k = 1f;
            a.Collar.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(bindDir, wantDir), k) * a.Collar.rotation;
            TwoBoneIK.Solve(a.Upper, a.Lower, a.Hand, wrist, elbow, 1f);
            a.Hand.rotation = a.Grip.Hold(grip);
        }
    }
}
