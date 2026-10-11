using System.Collections.Generic;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Players
{
    /// <summary>
    /// One soldier drawn in full: the skinned model, animated from what the
    /// plugin draws (lt_render: position, aim, airborne, dead, aiming down
    /// sights) and from its shots.
    ///
    /// The clips are forward gaits only, so direction is made here: the legs
    /// turn toward where it moves (backpedaling plays the gait in reverse)
    /// and the spine twists back to the aim, up to <see cref="SoldierLook.MaxTwist"/>.
    /// Aim pitch bends the spine. The rifle is placed by the aim, stock in the
    /// shoulder, and both hands are put on it by IK, so it points exactly
    /// where the player aims. Sprinting, it rides the right hand instead.
    /// </summary>
    public sealed class SoldierView : MonoBehaviour
    {
        public ushort Entity { get; private set; }
        public bool Assigned { get; private set; }
        /// <summary>The muzzle this frame (Unity space), for flashes and tracers.</summary>
        public Vector3 Muzzle { get; private set; }
        public Vector3 MuzzleForward { get; private set; }

        SoldierLook look;
        Animator animator;
        Transform model, rifle;
        Renderer[] renderers;
        // Levels of detail, chosen here (a LODGroup on a moving soldier costs the GPU
        // Resident Drawer an allocation every frame): renderers per level, the size
        // the screen heights refer to, the level shown (-1: none).
        Renderer[][] lodRenderers;
        float lodSize;
        int lod = -2;
        MeshRenderer rifleRenderer, holoRenderer;
        Transform spine, chest, upperChest, neck, head;
        Transform rUpper, rLower, rHand, lUpper, lLower, lHand;
        HandGrip rGrip, lGrip;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int PlayRateId = Animator.StringToHash("PlayRate");
        static readonly int AimRateId = Animator.StringToHash("AimRate");
        static readonly int AirborneId = Animator.StringToHash("Airborne");
        static readonly int DeadId = Animator.StringToHash("Dead");

        // Game space: where it was drawn, its smoothed velocity, the legs' yaw.
        Vector3 lastPos;
        Vector2 vel;
        float legsYaw, aimYaw, aimPitch, aimWeight, sprint;
        bool reverse, dead, airborne, hasLast;
        float firedAt = -10f;
        bool shadowsOnly, alwaysAnimate;
        Material body;

        public void Init(SoldierLook look)
        {
            this.look = look;
            var inst = Instantiate(look.Model, transform, false);
            inst.name = "Model";
            model = inst.transform;
            model.localScale = Vector3.one * look.ModelScale;
            animator = inst.GetComponent<Animator>();
            animator.runtimeAnimatorController = look.Controller;
            animator.applyRootMotion = false;
            animator.cullingMode = Application.isBatchMode ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            renderers = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                if (r is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = false;
                    smr.skinnedMotionVectors = false;
                    // Bounds that hold any pose: the arms reaching for the rifle, a fall.
                    // (The bind pose's are tight enough that a soldier just off screen
                    // would be culled while its rifle, held out, stays in view.)
                    float toLocal = 1f / Mathf.Max(smr.rootBone != null ? smr.rootBone.lossyScale.x : smr.transform.lossyScale.x, 1e-6f);
                    smr.localBounds = new Bounds(smr.localBounds.center, Vector3.one * (2.4f * look.ModelScale * toLocal));
                }
            }
            var lods = inst.GetComponent<LODGroup>();
            if (lods != null)
            {
                var l = lods.GetLODs();
                lodRenderers = new Renderer[l.Length][];
                for (int i = 0; i < l.Length; i++)
                    lodRenderers[i] = l[i].renderers;
                var s = inst.transform.lossyScale;
                lodSize = lods.size * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                DestroyImmediate(lods);
            }
            // (The clips are imported body-oriented: the animated body faces the
            // model's +z, whatever the bind pose's axes.)
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? chest;
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            // Still in the bind pose (nothing has animated yet): how each hand is bound.
            var bodyRight = (rUpper.position - lUpper.position).normalized;
            var bodyUp = head.position - animator.GetBoneTransform(HumanBodyBones.Hips).position;
            rGrip = HandGrip.FromBind(rLower, rHand, HandGrip.InBody(look.RightBindBack, bodyRight, bodyUp));
            lGrip = HandGrip.FromBind(lLower, lHand, HandGrip.InBody(look.LeftBindBack, bodyRight, bodyUp));

            var rg = new GameObject("Rifle");
            rifle = rg.transform;
            rifle.SetParent(transform, false);
            rg.AddComponent<MeshFilter>().sharedMesh = look.Rifle;
            rifleRenderer = rg.AddComponent<MeshRenderer>();
            rifleRenderer.sharedMaterial = look.RifleMaterial;
            rifleRenderer.shadowCastingMode = ShadowCastingMode.On;
            if (look.Holo != null)
            {
                var hg = new GameObject("Holo sight");
                hg.transform.SetParent(rifle, false);
                hg.AddComponent<MeshFilter>().sharedMesh = look.Holo;
                holoRenderer = hg.AddComponent<MeshRenderer>();
                holoRenderer.sharedMaterials = new[] { look.HoloBody, look.HoloInner, look.HoloGlass };
                holoRenderer.shadowCastingMode = ShadowCastingMode.On;
            }
            gameObject.SetActive(false);
        }

        public void Assign(ushort entity)
        {
            Entity = entity;
            Assigned = true;
            hasLast = false;
            firedAt = -10f;
            aimWeight = 1f;
            gameObject.SetActive(true);
        }

        /// <summary>The body's material (SoldierColors': by relation, faction or how it's drawn).</summary>
        public void Body(Material mat)
        {
            if (mat == body || mat == null)
                return;
            body = mat;
            foreach (var r in renderers)
                r.sharedMaterial = mat;
        }

        public void Release()
        {
            Assigned = false;
            gameObject.SetActive(false);
        }

        /// <summary>Only its shadow shows (our own body in first person).</summary>
        public void SetShadowsOnly(bool on)
        {
            if (shadowsOnly == on)
                return;
            shadowsOnly = on;
            var mode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in renderers)
                r.shadowCastingMode = mode;
            rifleRenderer.shadowCastingMode = mode;
            if (holoRenderer != null)
                holoRenderer.shadowCastingMode = mode;
        }

        /// <summary>
        /// Shows the level of detail its size on screen calls for, by the thresholds
        /// in the look (as a LODGroup would): <paramref name="eye"/> the camera,
        /// <paramref name="scale"/> the LOD bias over 2 tan(fov/2).
        /// </summary>
        public void Lod(Vector3 eye, float scale)
        {
            if (lodRenderers == null)
                return;
            float d = Vector3.Distance(eye, transform.position);
            float height = lodSize * scale / Mathf.Max(d, 0.01f);
            int want = -1;
            for (int i = 0; i < lodRenderers.Length && i < look.LodHeights.Length; i++)
            {
                if (height >= look.LodHeights[i])
                {
                    want = i;
                    break;
                }
            }
            if (want == lod)
                return;
            lod = want;
            for (int i = 0; i < lodRenderers.Length; i++)
                foreach (var r in lodRenderers[i])
                    if (r != null)
                        r.enabled = i == want;
        }

        /// <summary>Animate whether or not it's seen (our own body).</summary>
        public void AlwaysAnimate()
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            alwaysAnimate = true;
        }

        /// <summary>It was drawn firing now.</summary>
        public void Fired(float time) => firedAt = time;

        /// <summary>
        /// Places it where it's drawn (game space feet, aim in radians) and sets
        /// the animator for this frame: before animation runs.
        /// </summary>
        public void Place(Vector3 feet, float yaw, float pitch, bool isAirborne, bool isDead, bool ads, float dt, float time)
        {
            dt = Mathf.Max(dt, 1e-4f);
            bool teleport = !hasLast || (feet - lastPos).sqrMagnitude > LtConst.Teleport * LtConst.Teleport || (dead && !isDead);
            if (teleport)
            {
                vel = Vector2.zero;
                legsYaw = yaw;
            }
            else
            {
                var v = new Vector2(feet.x - lastPos.x, feet.y - lastPos.y) / dt;
                vel = Vector2.Lerp(vel, v, 1f - Mathf.Exp(-dt / 0.1f));
            }
            lastPos = feet;
            hasLast = true;
            if (!isDead)
            {
                aimYaw = yaw;
                aimPitch = pitch;
            }
            dead = isDead;
            airborne = isAirborne;

            float speed = vel.magnitude;
            float twistMax = look.MaxTwist * Mathf.Deg2Rad;
            float target;
            reverse = false;
            if (dead)
                target = legsYaw;
            else if (speed < 0.6f)
                // Standing: the legs turn only once the twist gets large (a step in place).
                target = Mathf.Abs(Wrap(aimYaw - legsYaw)) > 0.9f * twistMax ? aimYaw : legsYaw;
            else
            {
                float move = Mathf.Atan2(vel.y, vel.x);
                float a = Wrap(move - aimYaw);
                if (Mathf.Abs(a) > 1.75f)
                {
                    reverse = true;
                    a = Wrap(a + Mathf.PI);
                }
                target = aimYaw + Mathf.Clamp(a, -twistMax, twistMax);
            }
            // Legs turn quickly but not instantly.
            legsYaw += Wrap(target - legsYaw) * (1f - Mathf.Exp(-dt / (speed < 0.6f ? 0.18f : 0.09f)));
            legsYaw = Wrap(legsYaw);

            transform.SetPositionAndRotation(Coords.ToUnity(feet.x, feet.y, feet.z), Coords.Yaw(legsYaw));

            bool sprinting = speed > 7.2f && !ads;
            sprint = Mathf.MoveTowards(sprint, sprinting ? 1f : 0f, dt / 0.15f);
            bool combat = ads || time - firedAt < 1.5f || !sprinting;
            aimWeight = Mathf.MoveTowards(aimWeight, dead ? 0f : combat ? 1f : 0f, dt / 0.2f);

            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(PlayRateId, (reverse ? -1f : 1f) * Mathf.Max(1f, speed / 9f));
            animator.SetFloat(AimRateId, 0f);
            animator.SetBool(AirborneId, airborne && !dead);
            animator.SetBool(DeadId, dead);
            animator.SetLayerWeight(1, aimWeight);
            rifleRenderer.enabled = !dead;
            if (holoRenderer != null)
                holoRenderer.enabled = !dead;
        }

        static float Wrap(float a)
        {
            a = (a + Mathf.PI) % (2f * Mathf.PI);
            if (a < 0f)
                a += 2f * Mathf.PI;
            return a - Mathf.PI;
        }

        /// <summary>
        /// After animation: twist and bend the spine to the aim, place the rifle,
        /// put the hands on it. Skipped when no LOD is on screen.
        /// </summary>
        public void Rig()
        {
            if (dead || spine == null)
                return;
            // (A headless editor draws no camera: everything counts as seen there.)
            bool visible = Application.isBatchMode || alwaysAnimate;
            foreach (var r in renderers)
                visible |= r.isVisible;
            visible |= rifleRenderer.isVisible;

            var aim = Coords.View(aimYaw, aimPitch);
            var up = Vector3.up;
            float twist = Mathf.DeltaAngle(90f - legsYaw * Mathf.Rad2Deg, 90f - aimYaw * Mathf.Rad2Deg);
            twist = Mathf.Clamp(twist, -look.MaxTwist - 15f, look.MaxTwist + 15f);
            if (visible)
            {
                Turn(spine, Quaternion.AngleAxis(twist * 0.25f, up));
                Turn(chest, Quaternion.AngleAxis(twist * 0.35f, up));
                Turn(upperChest, Quaternion.AngleAxis(twist * 0.4f, up));
                var right = aim * Vector3.right;
                float p = -aimPitch * Mathf.Rad2Deg * aimWeight;
                Turn(spine, Quaternion.AngleAxis(p * 0.2f, right));
                Turn(chest, Quaternion.AngleAxis(p * 0.25f, right));
                Turn(upperChest, Quaternion.AngleAxis(p * 0.25f, right));
                Turn(neck, Quaternion.AngleAxis(-aimPitch * Mathf.Rad2Deg * 0.12f, right));
                Turn(head, Quaternion.AngleAxis(-aimPitch * Mathf.Rad2Deg * 0.18f, right));
            }

            // The rifle's frame (forward: the muzzle, up: its top): in aim, stock in
            // the right shoulder pointing along the aim; sprinting, carried in the
            // right hand, muzzle forward and down. The mesh turns into that frame.
            float size = look.RifleScale;
            var stock = rUpper.position + aim * look.ShoulderPocket;
            var aimPos = stock + aim * Vector3.forward * (0.45f * size);
            var carry = Quaternion.LookRotation((transform.forward * 0.8f + Vector3.down * 0.6f).normalized, transform.up);
            var handPos = rHand.position - carry * (look.RightGrip * size);
            float w = Mathf.Max(aimWeight, 1f - sprint);
            var pos = Vector3.Lerp(handPos, aimPos, w);
            var frame = Quaternion.Slerp(carry, aim, w);
            rifle.SetPositionAndRotation(pos, frame * Quaternion.Euler(look.RifleEuler));
            rifle.localScale = Vector3.one * size;
            Muzzle = pos + frame * (look.RifleMuzzle * size);
            MuzzleForward = frame * Vector3.forward;

            if (visible && w > 0.01f)
            {
                var grip = pos + frame * (look.RightGrip * size);
                var fore = pos + frame * (look.LeftGrip * size);
                TwoBoneIK.Solve(rUpper, rLower, rHand, grip, rUpper.position + aim * look.RightElbowHint, w);
                rHand.rotation = Quaternion.Slerp(rHand.rotation, rGrip.Hold(SoldierLook.Grip(frame, look.RightFingers, look.RightBack)), w);
                TwoBoneIK.Solve(lUpper, lLower, lHand, fore, lUpper.position + aim * look.LeftElbowHint, w);
                lHand.rotation = Quaternion.Slerp(lHand.rotation, lGrip.Hold(SoldierLook.Grip(frame, look.LeftFingers, look.LeftBack)), w);
            }
        }

        static void Turn(Transform t, Quaternion world)
        {
            if (t != null)
                t.rotation = world * t.rotation;
        }

        /// <summary>
        /// A plain mesh of this soldier posed (moving at <paramref name="speed"/>, or
        /// fallen), from its lightest LOD, with <paramref name="rifleMesh"/> in its
        /// hands as a second submesh: what the crowd draws, instanced. In the
        /// soldier's own space (facing +z), scale included.
        /// </summary>
        public Mesh Bake(float speed, bool fallen, Mesh rifleMesh)
        {
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var cull = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            (legsYaw, aimYaw, aimPitch) = (Mathf.PI / 2f, Mathf.PI / 2f, 0f);
            dead = fallen;
            aimWeight = fallen ? 0f : 1f;
            sprint = 0f;
            animator.SetFloat(SpeedId, speed);
            animator.SetFloat(PlayRateId, 1f);
            animator.SetFloat(AimRateId, 0f);
            animator.SetBool(DeadId, fallen);
            animator.SetBool(AirborneId, false);
            animator.SetLayerWeight(1, aimWeight);
            animator.Play(fallen ? "Death" : "Move", 0, fallen ? 1f : 0.3f);
            animator.Play("Aim", 1, 0.25f);
            animator.Update(0f);
            animator.Update(0f);
            dead = false;
            Rig();
            dead = fallen;

            SkinnedMeshRenderer lightest = null;
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer s && (lightest == null || s.sharedMesh.vertexCount < lightest.sharedMesh.vertexCount))
                    lightest = s;
            // Baked in the renderer's own space, unscaled; its full matrix (the
            // import's scale included) takes it to ours.
            var body = new Mesh();
            lightest.BakeMesh(body, false);
            var toRoot = transform.worldToLocalMatrix;
            var parts = new List<CombineInstance>
            {
                new CombineInstance { mesh = body, transform = toRoot * lightest.transform.localToWorldMatrix },
            };
            if (rifleMesh != null)
                parts.Add(new CombineInstance { mesh = rifleMesh, transform = toRoot * rifle.localToWorldMatrix });
            var mesh = new Mesh { name = fallen ? "Crowd fallen" : speed > 1f ? "Crowd running" : "Crowd standing" };
            mesh.CombineMeshes(parts.ToArray(), false, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            Destroy(body);
            animator.cullingMode = cull;
            dead = false;
            gameObject.SetActive(false);
            return mesh;
        }
    }

    /// <summary>Analytic two-bone IK (shoulder, elbow, hand), the elbow toward a hint.</summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float weight)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float la = (pb - pa).magnitude, lb = (pc - pb).magnitude;
            Vector3 toT = target - pa;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (pc - pa).normalized;
            Vector3 toHint = hint - pa;
            Vector3 bend = toHint - Vector3.Dot(toHint, dir) * dir;
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.Cross(dir, Vector3.up);
            bend.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = pa + (dir * cosA + bend * sinA) * la;
            var ra = Quaternion.FromToRotation(pb - pa, elbow - pa) * a.rotation;
            a.rotation = Quaternion.Slerp(a.rotation, ra, weight);
            pb = b.position;
            pc = c.position;
            var rb = Quaternion.FromToRotation(pc - pb, pa + dir * d - pb) * b.rotation;
            b.rotation = Quaternion.Slerp(b.rotation, rb, weight);
        }
    }
}
