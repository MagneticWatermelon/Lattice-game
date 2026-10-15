using System;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.Players;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Combat
{
    /// <summary>
    /// The rifle in the player's hands, PlanetSide 2's way (GDD §15, the Bevy
    /// client's gun.rs): aiming down sights (zoom, slower mouse and movement,
    /// a tight cone), recoil that kicks the view and drifts back after the
    /// burst, and the first-person rifle: to the sights, its kick, bob,
    /// sway behind the mouse, lowered while sprinting, its flash and light;
    /// its ammo cell, whose glow is the rounds left, and the reload that
    /// swaps it (<see cref="Reload"/>). The rules (cone, rates, recoil
    /// numbers, rounds) are the game's, from the generated constants; the
    /// shot itself is the plugin's (lt_fire), and so are the rounds (LtOwn).
    ///
    /// The first-person rifle is drawn in the world camera's render with its
    /// own field of view, after the world with depth cleared
    /// (<see cref="ViewModelFeature"/>), so it never sinks into a wall and
    /// never stretches with the player's FOV.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class Gun : MonoBehaviour, IGun
    {
        public const int ViewLayer = 6;
        const float AdsTime = 0.2f;
        const float FlashSecs = 0.045f;
        /// <summary>How far toward the sun the world's shade is looked for (m), and how
        /// long the rifle takes to pass into or out of it (s).</summary>
        const float SunReach = 300f, SunFade = 0.12f;

        public CombatLook Look;
        public CameraRig Rig;
        /// <summary>The soldiers' look: how hands hold the rifle, the faction materials.</summary>
        public SoldierLook Soldier;

        /// <summary>Raised when one of our shots fires (sound, HUD).</summary>
        public event Action Fired;
        /// <summary>Raised as a reload's motion passes each of its sounds.</summary>
        public event Action<Reload.Cue> ReloadCue;
        /// <summary>The trigger pulled with no rounds left at all (a reload can't help).</summary>
        public event Action DryFire;
        /// <summary>A spent cell hit the ground (Unity space; how hard, 0..1).</summary>
        public event Action<Vector3, float> CellLanded;

        public bool Ads { get; private set; }
        /// <summary>0 from the hip, 1 down the sights (eased between).</summary>
        public float Blend { get; private set; }
        public float SensitivityScale => 1f - Blend * (1f - 1f / AdsZoom(NetDriver.Frame.Own.kit));
        /// <summary>How far into the scope's own picture (0..1): the marksman rifle's,
        /// fully down its sights; at 1 the rifle isn't drawn (the HUD shows the scope).</summary>
        public float Scoped { get; private set; }

        /// <summary>The view's zoom down kit <paramref name="kit"/>'s sights.</summary>
        float AdsZoom(int kit) => Soldier != null && Soldier.Scoped(kit) ? Look.ScopeZoom : LtConst.AdsZoom;
        public float LastShot { get; private set; } = -10f;
        /// <summary>When the trigger was last pulled dry (no rounds at all), for the HUD; how often (the report).</summary>
        public float DryAt { get; private set; } = -10f;
        public int DryPulls { get; private set; }
        /// <summary>The first-person muzzle in the world (for effects), as drawn.</summary>
        public Vector3 MuzzleWorld { get; private set; }
        /// <summary>The reload's motion (the plugin's predicted reload times it).</summary>
        public Reload Reloading { get; private set; }

        /// <summary>The first-person view's root: on the world camera, as the view is drawn from it.</summary>
        Transform view;
        /// <summary>The view's vertical field of view now, degrees.</summary>
        float viewFov;
        Transform rifle, flash, cell;
        MeshFilter rifleFilter, sightFilter;
        int shownKit;
        Light flashLight;
        ViewArms arms;
        MeshRenderer cellRenderer;
        MaterialPropertyBlock cellProps;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        float sprint, bobPhase, kick, kickRoll, flashUntil, dip, airTime, cellPulse, cellGlow;
        float sunlit = 1f;
        Vector2 debt;          // recoil not yet recovered: degrees (up, right)
        Vector2 sway, swayVel; // degrees (yaw, pitch) the rifle lags the view
        float lastYaw, lastPitch;
        bool wasGrounded = true, wasCrouched, triggerWas;
        uint rng = 0x5EED6A11u;
        Vector3 muzzleView;    // the muzzle in the world camera's view space (m)
        Reload.CellState cellState;
        Vector3 cellWorld, cellVel;

        /// <summary>A spent cell, tossed: it falls in the world, bounces and lies a while.</summary>
        sealed class Spent
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public Vector3 Pos, Vel, Spin;
            public Quaternion Rot;
            public float Age, Glow;
            public bool InWorld, Down;
        }
        readonly Spent[] spent = new Spent[3];
        int nextSpent;
        const float SpentLife = 8f;

        float Rand()
        {
            rng = rng * 1664525u + 1013904223u;
            return (rng >> 8) / 16777216f;
        }

        void Start()
        {
            var main = Rig.Camera;
            // The world camera culls the view layer; only the view's pass draws it.
            main.cullingMask |= 1 << ViewLayer;
            var go = new GameObject("First-person view") { layer = ViewLayer };
            view = go.transform;
            view.SetParent(main.transform, false);
            viewFov = Look.ViewFov;
            ViewModelFeature.Camera = main;

            var r = new GameObject("First-person rifle");
            r.layer = ViewLayer;
            rifle = r.transform;
            rifle.SetParent(go.transform, false);
            rifleFilter = r.AddComponent<MeshFilter>();
            rifleFilter.sharedMesh = Look.ViewRifle;
            var mr = r.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Look.ViewRifleMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            // Moving every frame: drawn the plain way, not by the GPU Resident Drawer.
            GpuDriven.Exclude(mr);
            if (Soldier == null)
                Soldier = FindAnyObjectByType<Crowd>()?.Look;
            if (Soldier != null && Soldier.Holo != null)
            {
                var hg = new GameObject("Holo sight") { layer = ViewLayer };
                hg.transform.SetParent(rifle, false);
                sightFilter = hg.AddComponent<MeshFilter>();
                sightFilter.sharedMesh = Soldier.Holo;
                var hr = hg.AddComponent<MeshRenderer>();
                hr.sharedMaterials = new[] { Soldier.HoloBody, Soldier.HoloInner, Look.ViewHoloGlass };
                hr.shadowCastingMode = ShadowCastingMode.Off;
                GpuDriven.Exclude(hr);
            }

            cellProps = new MaterialPropertyBlock();
            if (Look.Cell != null)
            {
                cellRenderer = CellRenderer("Ammo cell", go.transform, ViewLayer);
                cell = cellRenderer.transform;
                for (int i = 0; i < spent.Length; i++)
                {
                    var s = CellRenderer($"Spent cell {i}", null, ViewLayer);
                    s.gameObject.SetActive(false);
                    spent[i] = new Spent { Go = s.gameObject, Renderer = s };
                }
            }
            Reloading = new Reload(Look.CellSeat, Look.CellHalf);
            Reloading.Cued += c => ReloadCue?.Invoke(c);
            if (Soldier != null)
                Reloading.Prepare(Soldier.LeftGrip, Soldier.LeftFingers, Soldier.LeftBack);

            var f = new GameObject("Flash");
            f.layer = ViewLayer;
            flash = f.transform;
            flash.SetParent(go.transform, false);
            f.AddComponent<MeshFilter>().sharedMesh = FlashMesh.Get();
            var fr = f.AddComponent<MeshRenderer>();
            fr.sharedMaterial = Look.Flash;
            fr.shadowCastingMode = ShadowCastingMode.Off;
            fr.receiveShadows = false;
            GpuDriven.Exclude(fr);
            f.SetActive(false);

            var l = new GameObject("Flash light");
            l.transform.SetParent(main.transform, false);
            flashLight = l.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.72f, 0.42f);
            flashLight.range = 9f;
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            if (Look.Arms != null && Soldier != null)
                arms = new ViewArms(Look.Arms, go.transform, ViewLayer, Soldier);
        }

        MeshRenderer CellRenderer(string name, Transform parent, int layer)
        {
            var c = new GameObject(name) { layer = layer };
            if (parent != null)
                c.transform.SetParent(parent, false);
            c.AddComponent<MeshFilter>().sharedMesh = Look.Cell;
            var r = c.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { Look.CellBody, Look.CellTrim, Look.CellGlow };
            r.shadowCastingMode = parent == null ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            GpuDriven.Exclude(r);
            return r;
        }

        void OnDestroy()
        {
            ViewModelFeature.Camera = null;
            ViewModelFeature.Fov = 0f;
            foreach (var s in spent)
                if (s?.Go != null)
                    Destroy(s.Go);
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>The trigger and the sights, each frame before the inputs go out.</summary>
        public void Frame(LtSession session, Lattice.LocalPlayer.LocalPlayer player, bool trigger, bool aim)
        {
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var own = NetDriver.Frame.Own;
            // No sights while the cell is out: the hands are busy.
            bool reloading = own.reload > 0f || (Reloading != null && Reloading.Weight > 0.05f);
            Ads = aim && !reloading;
            Blend = Mathf.MoveTowards(Blend, Ads ? 1f : 0f, dt / AdsTime);
            if (Rig != null)
                Rig.Zoom = Mathf.Lerp(1f, AdsZoom(own.kit), Ease(Blend));
            Scoped = Soldier != null && Soldier.Scoped(own.kit) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.97f, Blend)) : 0f;

            if (trigger && session != null)
            {
                var fire = new LtFire
                {
                    yaw = player.Yaw,
                    pitch = player.Pitch,
                    ads = (byte)(Ads ? 1 : 0),
                    muzzle = new LtVec3 { x = muzzleView.x, y = muzzleView.y, z = muzzleView.z },
                };
                if (session.Fire(fire, out _))
                    Shot(player, t);
                else if (!triggerWas && own.mag == 0 && own.reload <= 0f && own.dead == 0)
                {
                    // Nothing in the cell and nothing to reload with: the trigger clicks.
                    DryAt = t;
                    DryPulls++;
                    DryFire?.Invoke();
                }
            }
            triggerWas = trigger;

            // Recoil drifts back down once the burst is over.
            if (t - LastShot > Mathf.Max(LtConst.RecoilDelay, Weapons.Of(NetDriver.Frame.Own.kit).intervalSecs) && debt.x > 0f)
            {
                float back = Mathf.Min(LtConst.RecoilRecovery * dt, debt.x);
                float side = debt.y * back / debt.x;
                player.Pitch -= back * Mathf.Deg2Rad;
                player.Yaw = Coords.Wrap(player.Yaw + side * Mathf.Deg2Rad);
                debt -= new Vector2(back, side);
            }
        }

        /// <summary>A shot fired: the view kicks (the first of a burst harder), the
        /// rifle kicks back, the muzzle flashes, the cell pulses.</summary>
        void Shot(Lattice.LocalPlayer.LocalPlayer player, float t)
        {
            // This class's weapon's kick (U6): a shotgun's and a marksman rifle's each shot, a burst's the first.
            ref readonly var w = ref Weapons.Of(NetDriver.Frame.Own.kit);
            bool first = t - LastShot > Mathf.Max(LtConst.BurstGap, w.intervalSecs * 1.5f);
            float up = w.recoilUp * (first ? w.recoilFirst : 1f);
            float side;
            unsafe
            {
                side = Mathf.Lerp(w.recoilSide[0], w.recoilSide[1], Rand());
            }
            player.Pitch = Mathf.Min(player.Pitch + up * Mathf.Deg2Rad, Mathf.PI / 2f - 0.01f);
            player.Yaw = Coords.Wrap(player.Yaw - side * Mathf.Deg2Rad);
            debt += new Vector2(up, side);
            LastShot = t;
            kick = 1f;
            kickRoll = (Rand() - 0.5f) * 2f;
            flashUntil = t + FlashSecs;
            cellPulse = 1f;
            Fired?.Invoke();
        }

        void LateUpdate()
        {
            if (view == null || Rig == null)
                return;
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            var f = NetDriver.Frame;
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            // The reload runs whatever the view (its sounds play in every view).
            if (Soldier != null)
                Reloading.Follow(f.Welcomed && f.Own.dead == 0 ? f.Own.reload : 0f, t, Soldier.LeftGrip, Soldier.LeftFingers, Soldier.LeftBack);
            UpdateSpent(dt);
            // This class's weapon and its sight (U6).
            int kit = f.Own.kit;
            if (kit != shownKit)
            {
                shownKit = kit;
                rifleFilter.sharedMesh = Look.ViewWeaponOf(kit);
                if (sightFilter != null)
                    sightFilter.sharedMesh = Soldier.SightOf(kit);
            }
            // (Not while the debug camera follows someone else, nor all the way down a
            // scope: the view is then the scope's picture.)
            bool show = p != null && p.Mode == ViewMode.FirstPerson && f.Welcomed && f.Own.dead == 0 && CameraRig.Follow == 0 && Scoped < 0.999f;
            if (rifle.gameObject.activeSelf != show)
                rifle.gameObject.SetActive(show);
            arms?.Show(show);
            if (!show)
            {
                flash.gameObject.SetActive(false);
                flashLight.enabled = false;
                if (cell != null && cell.gameObject.activeSelf)
                    cell.gameObject.SetActive(false);
                cellState = Reload.CellState.Seated;
                ViewModelFeature.Fov = 0f;
                return;
            }
            float a = Ease(Blend);
            viewFov = 2f * Mathf.Atan(Mathf.Tan(Look.ViewFov * 0.5f * Mathf.Deg2Rad) / Mathf.Lerp(1f, 1.12f, a)) * Mathf.Rad2Deg;
            ViewModelFeature.Fov = viewFov;
            float busy = Reloading.Weight;

            // Movement: bob while walking, lowered while sprinting, a dip on landing
            // and as we crouch or stand.
            var v = f.Own.vel;
            float speed = Mathf.Sqrt(v.x * v.x + v.y * v.y);
            bool grounded = f.Own.grounded != 0;
            bool crouched = f.Own.crouched != 0;
            bool sprinting = speed > 7.5f && !Ads && grounded;
            sprint = Mathf.Lerp(sprint, sprinting ? 1f - busy : 0f, 1f - Mathf.Exp(-dt / 0.12f));
            if (grounded)
                bobPhase += speed * dt / (crouched ? 1.7f : 2.4f) * Mathf.PI * 2f;
            if (!grounded)
                airTime += dt;
            else
            {
                if (!wasGrounded && airTime > 0.25f)
                    dip = Mathf.Clamp01(airTime * 1.2f);
                airTime = 0f;
            }
            if (crouched != wasCrouched)
                dip = Mathf.Max(dip, crouched ? 0.32f : 0.22f);
            wasGrounded = grounded;
            wasCrouched = crouched;
            dip *= Mathf.Exp(-dt / 0.12f);
            float moving = Mathf.Min(speed / 6f, 1f) * (1f - 0.85f * a) * (grounded ? 1f : 0f) * (1f - 0.6f * busy);
            var bob = new Vector3(Mathf.Sin(bobPhase) * 0.012f, -Mathf.Abs(Mathf.Sin(bobPhase * 2f)) * 0.009f, 0f) * moving;
            float bobRoll = Mathf.Sin(bobPhase) * 1.2f * moving;

            // Sway: the rifle lags the view a little (a damped spring), less down the sights.
            float dYaw = Mathf.DeltaAngle(lastYaw * Mathf.Rad2Deg, p.Yaw * Mathf.Rad2Deg);
            float dPitch = (p.Pitch - lastPitch) * Mathf.Rad2Deg;
            (lastYaw, lastPitch) = (p.Yaw, p.Pitch);
            var target = new Vector2(Mathf.Clamp(dYaw * 0.6f, -5f, 5f), Mathf.Clamp(dPitch * 0.6f, -5f, 5f)) * (1f - 0.75f * a);
            sway = Vector2.SmoothDamp(sway, target, ref swayVel, 0.07f, Mathf.Infinity, dt);

            kick *= Mathf.Exp(-dt / Look.KickTau);
            var kb = Vector2.Lerp(Look.KickHip, Look.KickAds, a);

            float size = Look.ViewScale;
            var sight = Soldier != null && Soldier.Scoped(kit) ? Look.ScopeSight : Look.Sight;
            var adsPos = new Vector3(0f, 0f, Look.SightDistance) - sight * size;
            var pos = Vector3.Lerp(Look.Hip, adsPos, a) + Look.SprintOffset * sprint + bob
                      + new Vector3(0f, -0.035f * dip, -kb.x * kick) + new Vector3(sway.x * 0.002f, -sway.y * 0.002f, 0f);
            var rot = Quaternion.Euler(-kb.y * kick - sway.y + 2.5f * dip, sway.x, kickRoll * kick * 2f + bobRoll)
                      * Quaternion.Euler(Look.SprintEuler * sprint)
                      * Quaternion.Slerp(Quaternion.Euler(Look.HipEuler), Quaternion.identity, a);
            // The reload's own motion, over all that.
            Reloading.Rifle(out var reloadOffset, out var reloadTurn);
            pos += reloadOffset;
            rot *= reloadTurn;
            rifle.localPosition = pos;
            rifle.localRotation = rot * Quaternion.Euler(Look.ViewRifleEuler);
            rifle.localScale = Vector3.one * size;

            // The world's shade on the rifle (it samples no shadow map): a ray from
            // the eye toward the sun, over the ground and cover as the game has
            // them. The light fades as we step in or out, as through a penumbra.
            var sun = RenderSettings.sun;
            var world = NetDriver.World;
            if (sun != null && world != null)
            {
                var from = Coords.ToGame(Rig.Camera.transform.position);
                var toSun = Coords.DirToGame(-sun.transform.forward);
                var to = new LtVec3 { x = from.x + toSun.x * SunReach, y = from.y + toSun.y * SunReach, z = from.z + toSun.z * SunReach };
                bool shaded = world.Raycast(from, to, out _);
                sunlit = Mathf.MoveTowards(sunlit, shaded ? 0f : 1f, dt / SunFade);
            }
            ViewModelFeature.Sun = sunlit;

            // Both hands on it, as soldiers hold it; the left one off to swap the cell.
            bool reloadHand = Reloading.Hand(pos, rot, size, out var handPos, out var handGrip, out float handWeight);
            if (arms != null)
            {
                var cam = view;
                var frame = cam.rotation * rot;
                var center = cam.TransformPoint(pos);
                var lWrist = center + frame * (Soldier.LeftGrip * size);
                var lGrip = SoldierLook.Grip(frame, Soldier.LeftFingers, Soldier.LeftBack);
                if (reloadHand)
                {
                    lWrist = Vector3.Lerp(lWrist, cam.TransformPoint(handPos), handWeight);
                    lGrip = Quaternion.Slerp(lGrip, cam.rotation * handGrip, handWeight);
                }
                arms.Body(Lattice.Players.SoldierColors.Material(f.Own.faction));
                arms.Hold(cam.TransformPoint(Look.RightCollar), cam.TransformPoint(Look.RightElbow),
                          center + frame * (Soldier.RightGrip * size), SoldierLook.Grip(frame, Soldier.RightFingers, Soldier.RightBack),
                          cam.TransformPoint(Look.LeftCollar), cam.TransformPoint(Look.LeftElbow),
                          lWrist, lGrip, Look.ShoulderSwing);
            }

            Cell(f, pos, rot, size, reloadHand, handPos, handGrip, dt);

            // Where the tracer leaves from: the drawn muzzle, as the world camera
            // sees that point (the view's field of view differs from the world's).
            var mv = pos + rot * ((Soldier != null ? Soldier.MuzzleOf(kit) : Look.Muzzle) * size);
            float k = Mathf.Tan(Rig.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(viewFov * 0.5f * Mathf.Deg2Rad);
            muzzleView = new Vector3(mv.x * k, mv.y * k, mv.z);
            MuzzleWorld = Rig.Camera.transform.TransformPoint(muzzleView);

            bool on = t < flashUntil;
            if (flash.gameObject.activeSelf != on)
                flash.gameObject.SetActive(on);
            if (on)
            {
                float s = (0.8f + 0.5f * Rand()) * size;
                flash.localPosition = mv;
                flash.localRotation = rot * Quaternion.Euler(0f, 0f, Rand() * 360f);
                flash.localScale = new Vector3(s, s, s * (1.2f + Rand()));
            }
            flashLight.enabled = on;
            if (on)
            {
                flashLight.transform.position = MuzzleWorld + Rig.Camera.transform.forward * 0.3f;
                flashLight.intensity = 6f * (0.7f + 0.6f * Rand());
            }
        }

        /// <summary>
        /// The cell: seated in the rifle (its glow the rounds left, a flash with each
        /// shot), or wherever the reload has it; let go, it's tossed into the world.
        /// </summary>
        void Cell(NetFrame f, Vector3 pos, Quaternion rot, float size, bool reloadHand, Vector3 handPos, Quaternion handGrip, float dt)
        {
            if (cell == null)
                return;
            int mag = f.Own.mag;
            float charge = mag == 0 ? 0.04f : 0.3f + 0.7f * mag / (float)Mathf.Max(1, Weapons.Of(f.Own.kit).magazine);
            var state = Reloading.Cell(charge, out float pulled, out _, out float glow);
            Vector3 cp;
            Quaternion cr;
            switch (state)
            {
                case Reload.CellState.InHand when reloadHand:
                    Reloading.CellInHand(handPos, handGrip, size, Look.ViewRifleEuler, out cp, out cr);
                    break;
                case Reload.CellState.Dropped:
                case Reload.CellState.Gone:
                    if (cellState == Reload.CellState.InHand)
                        Toss(dt);
                    if (cell.gameObject.activeSelf)
                        cell.gameObject.SetActive(false);
                    cellState = state;
                    return;
                default:
                    cp = pos + rot * ((Look.CellSeat + new Vector3(-pulled, 0f, 0f)) * size);
                    cr = rot * Quaternion.AngleAxis(Reloading.CapTurn, Vector3.right) * Quaternion.Euler(Look.ViewRifleEuler);
                    break;
            }
            if (!cell.gameObject.activeSelf)
                cell.gameObject.SetActive(true);
            cell.localPosition = cp;
            cell.localRotation = cr;
            cell.localScale = Vector3.one * size;
            // Its world motion, for the toss.
            var w = view.TransformPoint(cp);
            if (dt > 0f && cellState == state)
                cellVel = Vector3.Lerp(cellVel, (w - cellWorld) / dt, 0.5f);
            else
                cellVel = Vector3.zero;
            cellWorld = w;
            cellState = state;
            // The glow: eased to its level, a flash with each shot.
            cellPulse *= Mathf.Exp(-dt / 0.07f);
            cellGlow = Mathf.Lerp(cellGlow, glow, 1f - Mathf.Exp(-dt / 0.08f));
            Glow(cellRenderer, cellGlow + 0.45f * cellPulse, f.Own.faction);
        }

        void Glow(MeshRenderer r, float level, int faction)
        {
            var c = SoldierColors.Body(faction).linear * (Look.CellGlowStrength * level * level);
            cellProps.SetColor(EmissionId, new Color(c.r, c.g, c.b, 1f));
            r.SetPropertyBlock(cellProps, 2);
        }

        /// <summary>The cell leaves the hand where it is, with the hand's motion and a flick down and out.</summary>
        void Toss(float dt)
        {
            var s = spent[nextSpent++ % spent.Length];
            if (s == null)
                return;
            s.Pos = cell.position;
            s.Rot = cell.rotation;
            var camRot = view.rotation;
            s.Vel = cellVel + camRot * new Vector3(-0.6f, -0.9f, 0.25f);
            s.Spin = camRot * new Vector3(4f + 3f * Rand(), 1.5f * (Rand() - 0.5f), 7f + 4f * Rand());
            s.Age = 0f;
            s.Glow = cellGlow;
            s.InWorld = false;
            s.Down = false;
            s.Go.layer = ViewLayer;
            s.Go.transform.SetPositionAndRotation(s.Pos, s.Rot);
            s.Go.transform.localScale = Vector3.one * Look.ViewScale;
            s.Go.SetActive(true);
        }

        /// <summary>Spent cells fall, bounce off the ground and cover as the game has
        /// them, and lie a while. Drawn with the rifle until they're out of the world
        /// camera's view, then in the world (depth-tested, shadowed).</summary>
        void UpdateSpent(float dt)
        {
            var world = NetDriver.World;
            var cam = Rig != null ? Rig.Camera : null;
            foreach (var s in spent)
            {
                if (s == null || !s.Go.activeSelf)
                    continue;
                s.Age += dt;
                if (s.Age > SpentLife || world == null || cam == null)
                {
                    s.Go.SetActive(false);
                    continue;
                }
                if (!s.Down)
                {
                    s.Vel += Physics.gravity * dt;
                    var next = s.Pos + s.Vel * dt;
                    var from = Coords.ToGame(s.Pos);
                    // A little past where it's going: the cell's own half-width.
                    var dir = (next - s.Pos).normalized * 0.03f;
                    var toPad = Coords.ToGame(next + dir);
                    if (world.Raycast(from, toPad, out var hit))
                    {
                        float hard = Mathf.Clamp01(s.Vel.magnitude / 6f);
                        var n = new Vector3(hit.normal.x, hit.normal.z, hit.normal.y);
                        var at = Coords.ToUnity(hit.pos.x, hit.pos.y, hit.pos.z);
                        next = at + n * 0.03f;
                        var vn = Vector3.Dot(s.Vel, n) * n;
                        s.Vel = (s.Vel - vn) * 0.55f - vn * 0.32f;
                        s.Spin *= 0.5f;
                        if (hard > 0.15f)
                            CellLanded?.Invoke(at, hard);
                        if (s.Vel.magnitude < 0.6f)
                        {
                            s.Down = true;
                            // Lying on its side, along the ground.
                            var along = Vector3.ProjectOnPlane(s.Rot * Vector3.up, n);
                            if (along.sqrMagnitude < 1e-4f)
                                along = Vector3.ProjectOnPlane(Vector3.forward, n);
                            s.Rot = Quaternion.FromToRotation(s.Rot * Vector3.up, along.normalized) * s.Rot;
                        }
                    }
                    s.Pos = next;
                    if (!s.Down)
                        s.Rot = Quaternion.Euler(s.Spin * (Mathf.Rad2Deg * dt)) * s.Rot;
                }
                s.Glow = Mathf.MoveTowards(s.Glow, 0f, dt / 0.5f);
                // Into the world once the world camera can't see it (no jump between
                // the two fields of view on screen), or once it's down there.
                if (!s.InWorld)
                {
                    var local = cam.transform.InverseTransformPoint(s.Pos);
                    float edge = -local.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) - 0.05f;
                    if (local.z <= 0.05f || local.y < -Mathf.Abs(edge) || s.Age > 0.6f)
                    {
                        s.InWorld = true;
                        s.Go.layer = 0;
                    }
                }
                s.Go.transform.SetPositionAndRotation(s.Pos, s.Rot);
                Glow(s.Renderer, s.Glow, NetDriver.Frame.Own.faction);
            }
        }
    }

    /// <summary>A muzzle flash's shape: two crossed quads along the barrel and one facing back
    /// at the shooter, UVs centered (the flash shader draws the fire).</summary>
    public static class FlashMesh
    {
        static Mesh mesh;

        public static Mesh Get()
        {
            if (mesh != null)
                return mesh;
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float kind)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(0, 0 + kind)); uv.Add(new Vector2(1, 0 + kind)); uv.Add(new Vector2(1, 1 + kind)); uv.Add(new Vector2(0, 1 + kind));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            // Along the barrel (kind 0: v in 0..1, z forward 0..0.35): two crossed quads.
            Quad(new Vector3(-0.07f, 0, 0), new Vector3(0.07f, 0, 0), new Vector3(0.07f, 0, 0.35f), new Vector3(-0.07f, 0, 0.35f), 0f);
            Quad(new Vector3(0, -0.07f, 0), new Vector3(0, 0.07f, 0), new Vector3(0, 0.07f, 0.35f), new Vector3(0, -0.07f, 0.35f), 0f);
            // Facing back (kind 2: v in 2..3): the star the shooter sees.
            Quad(new Vector3(-0.09f, -0.09f, 0.02f), new Vector3(0.09f, -0.09f, 0.02f), new Vector3(0.09f, 0.09f, 0.02f), new Vector3(-0.09f, 0.09f, 0.02f), 2f);
            mesh = new Mesh { name = "Muzzle flash" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
