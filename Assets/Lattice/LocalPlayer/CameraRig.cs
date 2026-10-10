using Lattice.Native;
using Lattice.Net;
using UnityEngine;

namespace Lattice.LocalPlayer
{
    /// <summary>
    /// Places the world camera for the view, on our player's drawn position
    /// (lt_own: between predicted steps, corrections smoothed away), after the
    /// frame's plugin calls and before anything draws.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Camera;
        /// <summary>Vertical field of view from the hip, degrees.</summary>
        public float Fov = 74f;
        /// <summary>The zoom applied now (1 = none): the gun sets it down the sights.</summary>
        [HideInInspector] public float Zoom = 1f;

        /// <summary>The eye this frame, game space (where shots leave from in first person).</summary>
        public Vector3 Eye { get; private set; }
        public bool HasBody { get; private set; }

        Vector3 chase;
        bool chaseInit;

        /// <summary>Debugging: orbit another player (its entity id; 0: off), at
        /// <see cref="FollowOffset"/> (meters right, up, back of its aim) — for
        /// looking at soldiers' animation.</summary>
        public static ushort Follow;
        public static Vector3 FollowOffset = new Vector3(1.6f, 0.4f, 3.2f);

        void Awake()
        {
            if (Camera == null)
                Camera = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            var p = LocalPlayer.Instance;
            var f = NetDriver.Frame;
            if (p == null || Camera == null)
                return;
            HasBody = f.Welcomed;
            var feet = f.Own.pos;
            var eye = new Vector3(feet.x, feet.y, feet.z + LtConst.EyeHeight);
            Vector3 camPos;
            Quaternion camRot;
            if (Follow != 0 && FollowCamera(f, out camPos, out camRot))
            {
                transform.SetPositionAndRotation(camPos, camRot);
                Camera.fieldOfView = Fov;
                return;
            }
            switch (p.Mode)
            {
                case ViewMode.Chase:
                {
                    // 5 m behind and 1.8 m above the eye, looking where the player
                    // looks; pulled in front of the ground and cover between.
                    var aim = Coords.Aim(p.Yaw, Mathf.Clamp(p.Pitch, -1.2f, 1.2f));
                    var back = new Vector3(eye.x - aim.x * 5f, eye.y - aim.y * 5f, eye.z - aim.z * 5f + 1.8f);
                    var world = NetDriver.World;
                    var from = new LtVec3 { x = eye.x, y = eye.y, z = eye.z };
                    var to = new LtVec3 { x = back.x, y = back.y, z = back.z };
                    if (world != null && world.Raycast(from, to, out var hit))
                        back = Vector3.Lerp(eye, back, Mathf.Max(0f, hit.t - 0.08f));
                    if (!chaseInit)
                        (chase, chaseInit) = (back, true);
                    chase = Vector3.Lerp(chase, back, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
                    camPos = Coords.ToUnity(chase.x, chase.y, chase.z);
                    var at = Coords.ToUnity(eye.x + aim.x * 20f, eye.y + aim.y * 20f, eye.z + aim.z * 20f);
                    camRot = Quaternion.LookRotation(at - camPos, Vector3.up);
                    break;
                }
                case ViewMode.Spectator:
                {
                    if (!p.SpecPlaced && HasBody)
                        p.Spectate(new Vector3(feet.x - 35f, feet.y - 35f, feet.z + 30f), Mathf.PI / 4f, -0.55f);
                    var e = p.SpecEye;
                    camPos = Coords.ToUnity(e.x, e.y, e.z);
                    camRot = Coords.View(p.SpecYaw, p.SpecPitch);
                    chaseInit = false;
                    break;
                }
                default:
                    camPos = Coords.ToUnity(eye.x, eye.y, eye.z);
                    camRot = Coords.View(p.Yaw, p.Pitch);
                    chaseInit = false;
                    break;
            }
            Eye = eye;
            transform.SetPositionAndRotation(camPos, camRot);
            float zoom = p.Mode == ViewMode.FirstPerson ? Mathf.Max(1f, Zoom) : 1f;
            Camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg;
        }

        bool FollowCamera(NetFrame f, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            for (int i = 0; i < f.EntityCount; i++)
            {
                var e = f.Entities[i];
                if (e.id != Follow)
                    continue;
                var center = Coords.ToUnity(e.pos.x, e.pos.y, e.pos.z + 1.1f);
                var yawRot = Coords.Yaw(e.yaw);
                pos = center + yawRot * new Vector3(FollowOffset.x, FollowOffset.y, -FollowOffset.z);
                rot = Quaternion.LookRotation(center - pos, Vector3.up);
                return true;
            }
            return false;
        }
    }
}
