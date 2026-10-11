using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// A hand held on the rifle. The rig has no finger bones (the model's hands
    /// are closed into grips in the mesh itself), so holding something is just
    /// turning the hand: a grip is a frame, forward along the fingers and up
    /// out of the back of the hand, and this finds the hand bone's rotation
    /// for one, from the bind pose.
    /// </summary>
    public readonly struct HandGrip
    {
        readonly Quaternion offset;

        HandGrip(Quaternion offset) => this.offset = offset;

        /// <summary>From the bind pose: the forearm and hand as bound, and where the
        /// back of the hand faces there (world).</summary>
        public static HandGrip FromBind(Transform lower, Transform hand, Vector3 back)
        {
            var fingers = (hand.position - lower.position).normalized;
            var grip = Quaternion.LookRotation(fingers, Vector3.ProjectOnPlane(back, fingers));
            return new HandGrip(Quaternion.Inverse(grip) * hand.rotation);
        }

        /// <summary>The hand bone's rotation holding <paramref name="grip"/> (world).</summary>
        public Quaternion Hold(Quaternion grip) => grip * offset;

        /// <summary>The grip a hand bone's rotation holds (world): the inverse of <see cref="Hold"/>.</summary>
        public Quaternion Of(Quaternion hand) => hand * Quaternion.Inverse(offset);

        /// <summary>A direction given in the bind pose's body frame (x right, y up, z
        /// forward), in the world: the body's right and up as bound.</summary>
        public static Vector3 InBody(Vector3 v, Vector3 right, Vector3 up)
        {
            up = Vector3.ProjectOnPlane(up, right).normalized;
            return v.x * right + v.y * up + v.z * Vector3.Cross(right, up);
        }
    }
}
