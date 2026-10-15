using System.Collections.Generic;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Players
{
    /// <summary>
    /// One soldier drawn in full: the skinned model, animated from what the
    /// plugin draws (lt_render: position, aim, airborne, dead, aiming down
    /// sights, crouched, reloading) and from its shots.
    ///
    /// The clips are forward gaits only, so direction is made here: the legs
    /// turn toward where it moves (backpedaling plays the gait in reverse)
    /// and the spine twists back to the aim, up to <see cref="SoldierLook.MaxTwist"/>.
    /// Aim pitch bends the spine. The rifle is placed by the aim, stock in the
    /// shoulder, and both hands are put on it by IK, so it points exactly
    /// where the player aims. Sprinting, it rides the right hand instead.
    /// Crouched (there's no crouch clip), the hips go down so the head sits on the
    /// crouched head hitbox and the legs fold by IK: a kneel standing still, a low
    /// walk moving. Reloading, the rifle cants and the left hand works its bay.
    /// </summary>
    public sealed partial class SoldierView : MonoBehaviour
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
        MeshFilter rifleFilter, holoFilter;
        /// <summary>Its class and the weapon it carries (U6).</summary>
        int kit, weapon;
        // Its ability, as others see it (U6): a Bulwark's shell, the jets' plumes,
        // the cloak (drawn with nothing but its shimmer).
        Transform shell;
        MeshRenderer shellRenderer;
        readonly Transform[] jets = new Transform[2];
        bool bulwark, jetting, cloaked;
        MaterialPropertyBlock shellProps;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        /// <summary>It turned on this frame (others' sounds).</summary>
        public bool AbilityStarted { get; private set; }
        Transform spine, chest, upperChest, neck, head;
        Transform rUpper, rLower, rHand, lUpper, lLower, lHand;
        Transform hips, lThigh, lShin, lFoot, rThigh, rShin, rFoot;
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
        bool reverse, dead, airborne, hasLast, crouched;
        float firedAt = -10f;
        /// <summary>How crouched it's drawn (eased); how far into a reload (s; -1: none) and
        /// how much of its pose shows (eased in and out with the flag).</summary>
        float crouch, reloadAt = -1f, reloadWeight;
        /// <summary>The run's bounce, settled (<see cref="Settle"/>): the hips' height over the
        /// feet as the clip has it, smoothed, and its slow average (m; -1: none yet); the
        /// last frame's time step.</summary>
        float hipsSmooth = -1f, hipsAverage = -1f, rigDt = 1f / 60f;
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
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            lThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            lShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            // Still in the bind pose (nothing has animated yet): how each hand is bound.
            var bodyRight = (rUpper.position - lUpper.position).normalized;
            var bodyUp = head.position - animator.GetBoneTransform(HumanBodyBones.Hips).position;
            rGrip = HandGrip.FromBind(rLower, rHand, HandGrip.InBody(look.RightBindBack, bodyRight, bodyUp));
            lGrip = HandGrip.FromBind(lLower, lHand, HandGrip.InBody(look.LeftBindBack, bodyRight, bodyUp));

            var rg = new GameObject("Rifle");
            rifle = rg.transform;
            rifle.SetParent(transform, false);
            rifle.localScale = Vector3.one * look.RifleScale;
            rifleFilter = rg.AddComponent<MeshFilter>();
            rifleFilter.sharedMesh = look.Rifle;
            rifleRenderer = rg.AddComponent<MeshRenderer>();
            rifleRenderer.sharedMaterial = look.RifleMaterial;
            rifleRenderer.shadowCastingMode = ShadowCastingMode.On;
            // It moves every frame and comes and goes with the band: the GPU Resident
            // Drawer, made for what stays put, would redo its instance each frame.
            GpuDriven.Exclude(rifleRenderer);
            if (look.Holo != null)
            {
                var hg = new GameObject("Holo sight");
                hg.transform.SetParent(rifle, false);
                holoFilter = hg.AddComponent<MeshFilter>();
                holoFilter.sharedMesh = look.Holo;
                holoRenderer = hg.AddComponent<MeshRenderer>();
                holoRenderer.sharedMaterials = new[] { look.HoloBody, look.HoloInner, look.HoloGlass };
                holoRenderer.shadowCastingMode = ShadowCastingMode.On;
                GpuDriven.Exclude(holoRenderer);
            }
            if (look.BulwarkGlow != null)
            {
                var sg = new GameObject("Bulwark");
                shell = sg.transform;
                shell.SetParent(transform, false);
                sg.AddComponent<MeshFilter>().sharedMesh = AbilityFx.Sphere();
                shellRenderer = sg.AddComponent<MeshRenderer>();
                shellRenderer.sharedMaterial = look.BulwarkGlow;
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
                GpuDriven.Exclude(shellRenderer);
                shellProps = new MaterialPropertyBlock();
                sg.SetActive(false);
            }
            if (look.JetFlame != null)
            {
                for (int i = 0; i < jets.Length; i++)
                {
                    var jg = new GameObject($"Jet {i}");
                    jets[i] = jg.transform;
                    jets[i].SetParent(transform, false);
                    jg.AddComponent<MeshFilter>().sharedMesh = AbilityFx.Plume();
                    var jr = jg.AddComponent<MeshRenderer>();
                    jr.sharedMaterial = look.JetFlame;
                    jr.shadowCastingMode = ShadowCastingMode.Off;
                    jr.receiveShadows = false;
                    GpuDriven.Exclude(jr);
                    jg.SetActive(false);
                }
            }
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Its class's ability as it's drawn this frame (<paramref name="on"/>: the
        /// entity's flag): a Bulwark's shell in its faction's color, the jets' flames,
        /// or, cloaked, the body and its weapon drawn with nothing but a shimmer.
        /// </summary>
        public void Ability(bool on, int faction)
        {
            if (exoOn)
                ExoFaction(faction);
            bool b = on && !dead && kit == LtCode.ClassAssault, j = on && !dead && kit == LtCode.ClassSkirmisher, c = on && !dead && kit == LtCode.ClassRecon;
            AbilityStarted = on && !dead && !wasOn;
            wasOn = on && !dead;
            if (shell != null && b != bulwark)
            {
                shell.gameObject.SetActive(b);
                if (b)
                {
                    var ink = SoldierColors.Ink(faction);
                    shellProps.SetColor(ColorId, new Color(ink.r, ink.g, ink.b) * 2.2f);
                    shellRenderer.SetPropertyBlock(shellProps);
                }
            }
            bulwark = b;
            if (j != jetting)
                foreach (var t in jets)
                    if (t != null)
                        t.gameObject.SetActive(j);
            jetting = j;
            if (c != cloaked)
            {
                cloaked = c;
                Materials();
            }
            if (bulwark)
            {
                float k = Mathf.Lerp(1f, 0.72f, crouch);
                shell.localPosition = new Vector3(0f, look.ShellHeight * k, 0f);
                shell.localScale = new Vector3(look.ShellRadii.x, look.ShellRadii.y * k, look.ShellRadii.x);
            }
        }

        bool wasOn;
        Material[] holoOwn, holoCloak;

        /// <summary>The body's, weapon's and sight's materials: their own, or the cloak's.</summary>
        void Materials()
        {
            var cloak = cloaked ? look.CloakGlow : null;
            foreach (var r in renderers)
                r.sharedMaterial = cloak != null ? cloak : body;
            rifleRenderer.sharedMaterial = cloak != null ? cloak : look.RifleMaterial;
            if (holoRenderer != null)
            {
                holoOwn ??= new[] { look.HoloBody, look.HoloInner, look.HoloGlass };
                holoCloak ??= new[] { look.CloakGlow, look.CloakGlow, look.CloakGlow };
                holoRenderer.sharedMaterials = cloak != null ? holoCloak : holoOwn;
            }
            // (Our own body in first person stays a shadow only, cloaked or not.)
            var mode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : cloak != null ? ShadowCastingMode.Off : ShadowCastingMode.On;
            foreach (var r in renderers)
                r.shadowCastingMode = mode;
            rifleRenderer.shadowCastingMode = mode;
        }

        public void Assign(ushort entity)
        {
            Entity = entity;
            Assigned = true;
            hasLast = false;
            firedAt = -10f;
            aimWeight = 1f;
            crouch = 0f;
            reloadAt = -1f;
            reloadWeight = 0f;
            Ability(false, 0);
            gameObject.SetActive(true);
        }

        /// <summary>The body's material (SoldierColors': by relation, faction or how it's drawn).</summary>
        public void Body(Material mat)
        {
            if (mat == body || mat == null)
                return;
            body = mat;
            if (cloaked)
                return;
            foreach (var r in renderers)
                r.sharedMaterial = mat;
        }

        /// <summary>Is class <paramref name="k"/> carrying weapon <paramref name="w"/> (and its
        /// sight; an Exo, its armor and arm weapons).</summary>
        public void Kit(int k, int w)
        {
            if (k == kit && w == weapon)
                return;
            (kit, weapon) = (k, w);
            rifleFilter.sharedMesh = look.WeaponOf(w);
            if (holoFilter != null)
                holoFilter.sharedMesh = look.SightOf(w);
            ExoShow(k == LtCode.ClassExo, w);
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
            var mode = on ? ShadowCastingMode.ShadowsOnly : cloaked ? ShadowCastingMode.Off : ShadowCastingMode.On;
            foreach (var r in renderers)
                r.shadowCastingMode = mode;
            rifleRenderer.shadowCastingMode = mode;
            if (holoRenderer != null)
                holoRenderer.shadowCastingMode = mode;
            ExoShadows();
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
        public void Fired(float time)
        {
            firedAt = time;
            nextArm++;
        }

        /// <summary>
        /// Places it where it's drawn (game space feet, aim in radians) and sets
        /// the animator for this frame: before animation runs.
        /// </summary>
        public void Place(Vector3 feet, float yaw, float pitch, bool isAirborne, bool isDead, bool ads, bool isCrouched, bool isReloading, float dt, float time)
        {
            dt = Mathf.Max(dt, 1e-4f);
            rigDt = dt;
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
            crouched = isCrouched && !isDead;
            crouch = teleport ? (crouched ? 1f : 0f) : Mathf.MoveTowards(crouch, crouched ? 1f : 0f, dt / 0.16f);
            bool reloading = isReloading && !isDead;
            if (reloading)
                reloadAt = reloadAt < 0f ? 0f : reloadAt + dt;
            reloadWeight = Mathf.MoveTowards(reloadWeight, reloading ? 1f : 0f, dt / 0.25f);
            if (reloadWeight <= 0f)
                reloadAt = -1f;

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

            // Crouched, a walk however fast (the legs fold over it), at the rate that keeps its feet.
            float gait = crouched && speed > 0.3f ? Mathf.Min(speed, 2f) : speed;
            float rate = crouched && speed > 2f ? speed / 2f : Mathf.Max(1f, speed / 9f);
            animator.SetFloat(SpeedId, gait);
            animator.SetFloat(PlayRateId, (reverse ? -1f : 1f) * rate);
            animator.SetFloat(AimRateId, 0f);
            animator.SetBool(AirborneId, airborne && !dead);
            animator.SetBool(DeadId, dead);
            animator.SetLayerWeight(1, aimWeight);
            rifleRenderer.enabled = !dead && !exoOn;
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

            // Small on screen (past the full rig's levels of detail), the upper body
            // turns to the aim from one bone, and the hands stay where the aim layer
            // holds them: a hand there is a pixel or two.
            bool small = (lod < 0 || lod >= look.FullRigLods) && !alwaysAnimate;
            if (crouch > 0.001f && (visible || alwaysAnimate))
                Crouch(crouch, !small);
            else if (visible && !small)
                Settle();
            else
                hipsSmooth = hipsAverage = -1f;
            var aim = Coords.View(aimYaw, aimPitch);
            var up = Vector3.up;
            float twist = Mathf.DeltaAngle(90f - legsYaw * Mathf.Rad2Deg, 90f - aimYaw * Mathf.Rad2Deg);
            twist = Mathf.Clamp(twist, -look.MaxTwist - 15f, look.MaxTwist + 15f);
            if (visible && small)
            {
                var right = aim * Vector3.right;
                Turn(upperChest, Quaternion.AngleAxis(-aimPitch * Mathf.Rad2Deg * aimWeight * 0.7f, right) * Quaternion.AngleAxis(twist, up));
            }
            else if (visible)
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
            // Reloading: the rifle comes down a little and cants, its bay up to the left hand.
            float reload = Reloading(out var leftAt);
            if (reload > 0f)
            {
                frame = Quaternion.Slerp(frame, frame * Quaternion.Euler(12f, -8f, -40f), reload);
                pos += (-up * 0.06f - transform.right * 0.04f) * reload;
            }
            rifle.SetPositionAndRotation(pos, frame * Quaternion.Euler(look.RifleEuler));
            Muzzle = pos + frame * (look.MuzzleOf(weapon) * size);
            // The jets: two nozzles on the back, burning down and a little back.
            if (jetting && upperChest != null)
            {
                var back = upperChest.position - transform.forward * 0.2f - up * 0.12f;
                var dir = Quaternion.LookRotation((Vector3.down - transform.forward * 0.25f).normalized, transform.forward);
                for (int i = 0; i < jets.Length; i++)
                    if (jets[i] != null)
                        jets[i].SetPositionAndRotation(back + transform.right * (i == 0 ? -0.13f : 0.13f), dir);
            }
            MuzzleForward = frame * Vector3.forward;
            ExoPlace(aim);

            if (visible && w > 0.01f && !small)
            {
                var grip = pos + frame * (look.RightGrip * size);
                var fore = pos + frame * (look.LeftGrip * size);
                TwoBoneIK.Solve(rUpper, rLower, rHand, grip, rUpper.position + aim * look.RightElbowHint, w);
                rHand.rotation = Quaternion.Slerp(rHand.rotation, rGrip.Hold(SoldierLook.Grip(frame, look.RightFingers, look.RightBack)), w);
                // Reloading, the left hand leaves the handguard: to the bay, out with the
                // cell, down to the belt for another, back, home.
                if (reload > 0f)
                    fore = Vector3.Lerp(fore, LeftReload(pos, frame, size, leftAt), reload);
                TwoBoneIK.Solve(lUpper, lLower, lHand, fore, lUpper.position + aim * look.LeftElbowHint, w);
                lHand.rotation = Quaternion.Slerp(lHand.rotation, lGrip.Hold(SoldierLook.Grip(frame, look.LeftFingers, look.LeftBack)), w);
            }
        }

        static void Turn(Transform t, Quaternion world)
        {
            if (t != null)
                t.rotation = world * t.rotation;
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// The crouch, over whatever the animator played: the hips down (to a kneel
        /// standing still, a low walk moving), the body leaning in, and the legs folded
        /// by IK onto where the feet go: moving, where the gait put them; still, the
        /// kneel's (the left foot forward, the right knee down, its foot on its toes).
        /// The feet keep their turn. <paramref name="legs"/>: fold them (off when the
        /// soldier's a few pixels tall: the hips going down is what shows).
        /// </summary>
        void Crouch(float w, bool legs)
        {
            if (hips == null || lFoot == null || rFoot == null)
                return;
            var up = Vector3.up;
            var root = transform.position;
            var fwd = transform.forward;
            var right = transform.right;
            Vector3 lf = lFoot.position, rf = rFoot.position;
            Quaternion lfr = lFoot.rotation, rfr = rFoot.rotation;
            float still = 1f - Smooth(Mathf.InverseLerp(0.3f, 1.2f, vel.magnitude));
            float want = Mathf.Lerp(look.CrouchHipsMoving, look.CrouchHipsStill, still);
            float drop = Mathf.Max(0f, hips.position.y - root.y - want) * w;
            hips.position -= up * drop;
            Turn(spine, Quaternion.AngleAxis(Mathf.Lerp(look.CrouchLeanMoving, look.CrouchLeanStill, still) * w, right));
            if (!legs)
                return;
            Vector3 Kneel(Vector3 k) => root + fwd * k.x + right * k.y + up * k.z;
            var lTarget = Vector3.Lerp(lf, Kneel(look.KneelLeftFoot), still);
            var rTarget = Vector3.Lerp(rf, Kneel(look.KneelRightFoot), still);
            // Knees forward (the kneeling one forward and down, onto the ground).
            TwoBoneIK.Solve(lThigh, lShin, lFoot, lTarget, lShin.position + fwd * 0.5f + up * 0.1f, w);
            TwoBoneIK.Solve(rThigh, rShin, rFoot, rTarget, rShin.position + fwd * 0.5f - up * (0.3f * still), w);
            lFoot.rotation = lfr;
            rFoot.rotation = Quaternion.Slerp(rfr, Quaternion.AngleAxis(50f, right) * rfr, still * w);
        }

        /// <summary>
        /// The run's bounce, settled. The sprint clip drops the hips 15 cm at
        /// every footfall, in a sharp V (it read as hopping): the hips ride its
        /// height smoothed, and keep only <see cref="SoldierLook.SprintBounce"/>
        /// of how far it strays from its slow average (all of a walk's); the legs
        /// then reach back by IK to where the clip planted the feet, so nothing
        /// slides or floats.
        /// </summary>
        void Settle()
        {
            if (hips == null || lFoot == null || rFoot == null || airborne)
            {
                hipsSmooth = hipsAverage = -1f;
                return;
            }
            float h = hips.position.y - transform.position.y;
            hipsSmooth = hipsSmooth < 0f ? h : Mathf.Lerp(hipsSmooth, h, 1f - Mathf.Exp(-rigDt / 0.05f));
            hipsAverage = hipsAverage < 0f ? h : Mathf.Lerp(hipsAverage, h, 1f - Mathf.Exp(-rigDt / 0.45f));
            float keep = Mathf.Lerp(1f, look.SprintBounce, Smooth(Mathf.InverseLerp(5f, 8.5f, vel.magnitude)));
            float shift = hipsAverage + (hipsSmooth - hipsAverage) * keep - h;
            if (Mathf.Abs(shift) < 0.002f)
                return;
            var up = Vector3.up;
            var fwd = transform.forward;
            Vector3 lf = lFoot.position, rf = rFoot.position;
            Quaternion lfr = lFoot.rotation, rfr = rFoot.rotation;
            hips.position += up * shift;
            // (Knees bend forward, as the clip's do.)
            TwoBoneIK.Solve(lThigh, lShin, lFoot, lf, lShin.position + fwd * 0.5f, 1f);
            TwoBoneIK.Solve(rThigh, rShin, rFoot, rf, rShin.position + fwd * 0.5f, 1f);
            lFoot.rotation = lfr;
            rFoot.rotation = rfr;
        }

        /// <summary>How far into the reload's pose (0..1), eased in and out with the
        /// flag (it lasts 2.2 s, or 2.7 from empty: the hand then waits at the bay),
        /// and how far through its motion (s).</summary>
        float Reloading(out float t)
        {
            t = Mathf.Max(reloadAt, 0f);
            return dead ? 0f : Smooth(reloadWeight);
        }

        /// <summary>The left wrist through a reload (world): from the handguard to the bay
        /// (the rifle's frame, as the first-person one has it), out with the cell, down to
        /// the belt, back with another, into the bay, home.</summary>
        Vector3 LeftReload(Vector3 pos, Quaternion frame, float size, float t)
        {
            var bay = pos + frame * (new Vector3(-0.17f, -0.07f, -0.19f) * size);
            var outOf = bay - frame * (Vector3.right * (0.2f * size));
            var belt = transform.position + Vector3.up * 0.95f - transform.right * 0.2f + transform.forward * 0.05f;
            if (t < 0.35f)
                return bay;
            if (t < 0.6f)
                return Vector3.Lerp(bay, outOf, Smooth((t - 0.35f) / 0.25f));
            if (t < 1.05f)
                return Vector3.Lerp(outOf, belt, Smooth((t - 0.6f) / 0.45f));
            if (t < 1.45f)
                return Vector3.Lerp(belt, outOf, Smooth((t - 1.05f) / 0.4f));
            return Vector3.Lerp(outOf, bay, Smooth((t - 1.45f) / 0.25f));
        }

        /// <summary>
        /// A plain mesh of this soldier posed (moving at <paramref name="speed"/>, or
        /// fallen), from its lightest LOD, with <paramref name="rifleMesh"/> in its
        /// hands as a second submesh: what the crowd draws, instanced. In the
        /// soldier's own space (facing +z), scale included.
        /// </summary>
        public Mesh Bake(float speed, bool fallen, Mesh rifleMesh, bool crouching = false)
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
            crouched = crouching;
            crouch = crouching ? 1f : 0f;
            reloadAt = -1f;
            // (Moving along its facing: the crouch reads its speed from here.)
            vel = new Vector2(0f, speed);
            animator.SetFloat(SpeedId, crouching && speed > 0.3f ? Mathf.Min(speed, 2f) : speed);
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
            // Baked without the renderer's scale, the body comes out at its size in
            // the world (meters), in the renderer's own orientation: its position and
            // rotation alone take it to ours. (Its full matrix would apply the
            // import's 0.01 scale a second time: a body 2 cm tall.)
            var body = new Mesh();
            lightest.BakeMesh(body, false);
            var toRoot = transform.worldToLocalMatrix;
            var place = Matrix4x4.TRS(lightest.transform.position, lightest.transform.rotation, Vector3.one);
            var parts = new List<CombineInstance>
            {
                new CombineInstance { mesh = body, transform = toRoot * place },
            };
            // An Exo's armor in its weapon's place (the rifle's material, its darkest texel).
            Mesh armor = exoOn && !fallen ? ExoMesh(toRoot) : null;
            if (armor != null)
                parts.Add(new CombineInstance { mesh = armor, transform = Matrix4x4.identity });
            else if (rifleMesh != null)
                parts.Add(new CombineInstance { mesh = rifleMesh, transform = toRoot * rifle.localToWorldMatrix });
            var mesh = new Mesh { name = (fallen ? "Crowd fallen" : speed > 1f ? "Crowd running" : "Crowd standing") + (crouching ? ", crouched" : "") };
            mesh.CombineMeshes(parts.ToArray(), false, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            Destroy(body);
            if (armor != null)
                Destroy(armor);
            animator.cullingMode = cull;
            dead = false;
            crouched = false;
            crouch = 0f;
            vel = Vector2.zero;
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
