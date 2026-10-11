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
        // The death view: where the eye was and which way it looked when we died.
        float diedAt = -1f, deathYaw;
        Vector3 deathEye;
        Quaternion deathRot;

        /// <summary>The death view's camera is up and back of the body, on the side we faced from.</summary>
        static readonly Vector3 DeathOffset = new Vector3(-3.4f, 0f, 2.4f);
        const float DeathMove = 1.6f;

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
            bool dead = f.Welcomed && f.Own.dead != 0 && p.Mode != ViewMode.Spectator;
            if (dead && DeathView(p, feet, eye, out camPos, out camRot))
            {
                transform.SetPositionAndRotation(camPos, camRot);
                Camera.fieldOfView = Fov;
                Eye = eye;
                return;
            }
            if (!dead)
                diedAt = -1f;
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

        /// <summary>
        /// Dead: the camera rises from the eye and draws back to look down at
        /// the body (GDD §15), eased, kept out of the ground and cover.
        /// </summary>
        bool DeathView(LocalPlayer p, LtVec3 feet, Vector3 eye, out Vector3 pos, out Quaternion rot)
        {
            float now = Time.unscaledTime;
            if (diedAt < 0f)
            {
                diedAt = now;
                deathYaw = p.Yaw;
                deathEye = transform.position;
                deathRot = transform.rotation;
            }
            float c = Mathf.Cos(deathYaw), s = Mathf.Sin(deathYaw);
            var target = new Vector3(feet.x + c * DeathOffset.x, feet.y + s * DeathOffset.x, feet.z + DeathOffset.z);
            var world = NetDriver.World;
            var from = new LtVec3 { x = feet.x, y = feet.y, z = feet.z + 1f };
            var to = new LtVec3 { x = target.x, y = target.y, z = target.z };
            if (world != null && world.Raycast(from, to, out var hit))
                target = Vector3.Lerp(new Vector3(from.x, from.y, from.z), target, Mathf.Max(0f, hit.t - 0.1f));
            if (world != null)
                target.z = Mathf.Max(target.z, world.Height(target.x, target.y) + 0.6f);
            float t = Mathf.Clamp01((now - diedAt) / DeathMove);
            t = 1f - (1f - t) * (1f - t) * (1f - t);
            var goal = Coords.ToUnity(target.x, target.y, target.z);
            var body = Coords.ToUnity(feet.x, feet.y, feet.z + 0.35f);
            pos = Vector3.Lerp(deathEye, goal, t);
            rot = Quaternion.Slerp(deathRot, Quaternion.LookRotation(body - goal, Vector3.up), t);
            return true;
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
