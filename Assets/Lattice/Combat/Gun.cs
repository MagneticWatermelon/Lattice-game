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
    /// sway behind the mouse, lowered while sprinting, its flash and light.
    /// The rules (cone, rates, recoil numbers) are the game's, from the
    /// generated constants; the shot itself is the plugin's (lt_fire).
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
        public event System.Action Fired;

        public bool Ads { get; private set; }
        /// <summary>0 from the hip, 1 down the sights (eased between).</summary>
        public float Blend { get; private set; }
        public float SensitivityScale => 1f - Blend * (1f - 1f / LtConst.AdsZoom);
        public float LastShot { get; private set; } = -10f;
        /// <summary>The first-person muzzle in the world (for effects), as drawn.</summary>
        public Vector3 MuzzleWorld { get; private set; }

        /// <summary>The first-person view's root: on the world camera, as the view is drawn from it.</summary>
        Transform view;
        /// <summary>The view's vertical field of view now, degrees.</summary>
        float viewFov;
        Transform rifle, flash;
        Light flashLight;
        Renderer[] viewRenderers;
        ViewArms arms;
        float sprint, bobPhase, kick, kickRoll, flashUntil, dip, airTime;
        float sunlit = 1f;
        Vector2 debt;          // recoil not yet recovered: degrees (up, right)
        Vector2 sway, swayVel; // degrees (yaw, pitch) the rifle lags the view
        float lastYaw, lastPitch;
        bool wasGrounded = true;
        uint rng = 0x5EED6A11u;
        Vector3 muzzleView;    // the muzzle in the world camera's view space (m)

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
            r.AddComponent<MeshFilter>().sharedMesh = Look.ViewRifle;
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
                hg.AddComponent<MeshFilter>().sharedMesh = Soldier.Holo;
                var hr = hg.AddComponent<MeshRenderer>();
                hr.sharedMaterials = new[] { Soldier.HoloBody, Soldier.HoloInner, Look.ViewHoloGlass };
                hr.shadowCastingMode = ShadowCastingMode.Off;
                GpuDriven.Exclude(hr);
            }

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
            viewRenderers = new Renderer[] { mr };

            if (Look.Arms != null && Soldier != null)
                arms = new ViewArms(Look.Arms, go.transform, ViewLayer, Soldier);
        }

        void OnDestroy()
        {
            ViewModelFeature.Camera = null;
            ViewModelFeature.Fov = 0f;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>The trigger and the sights, each frame before the inputs go out.</summary>
        public void Frame(LtSession session, Lattice.LocalPlayer.LocalPlayer player, bool trigger, bool aim)
        {
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Ads = aim;
            Blend = Mathf.MoveTowards(Blend, Ads ? 1f : 0f, dt / AdsTime);
            if (Rig != null)
                Rig.Zoom = Mathf.Lerp(1f, LtConst.AdsZoom, Ease(Blend));

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
            }

            // Recoil drifts back down once the burst is over.
            if (t - LastShot > LtConst.RecoilDelay && debt.x > 0f)
            {
                float back = Mathf.Min(LtConst.RecoilRecovery * dt, debt.x);
                float side = debt.y * back / debt.x;
                player.Pitch -= back * Mathf.Deg2Rad;
                player.Yaw = Coords.Wrap(player.Yaw + side * Mathf.Deg2Rad);
                debt -= new Vector2(back, side);
            }
        }

        /// <summary>A shot fired: the view kicks (the first of a burst harder), the
        /// rifle kicks back, the muzzle flashes.</summary>
        void Shot(Lattice.LocalPlayer.LocalPlayer player, float t)
        {
            bool first = t - LastShot > LtConst.BurstGap;
            float up = LtConst.RecoilUp * (first ? LtConst.RecoilFirst : 1f);
            float side = Mathf.Lerp(LtConst.RecoilSideMin, LtConst.RecoilSideMax, Rand());
            player.Pitch = Mathf.Min(player.Pitch + up * Mathf.Deg2Rad, Mathf.PI / 2f - 0.01f);
            player.Yaw = Coords.Wrap(player.Yaw - side * Mathf.Deg2Rad);
            debt += new Vector2(up, side);
            LastShot = t;
            kick = 1f;
            kickRoll = (Rand() - 0.5f) * 2f;
            flashUntil = t + FlashSecs;
            Fired?.Invoke();
        }

        void LateUpdate()
        {
            if (view == null || Rig == null)
                return;
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            var f = NetDriver.Frame;
            bool show = p != null && p.Mode == ViewMode.FirstPerson && f.Welcomed && f.Own.dead == 0;
            if (rifle.gameObject.activeSelf != show)
                rifle.gameObject.SetActive(show);
            arms?.Show(show);
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (!show)
            {
                flash.gameObject.SetActive(false);
                flashLight.enabled = false;
                ViewModelFeature.Fov = 0f;
                return;
            }
            float a = Ease(Blend);
            viewFov = 2f * Mathf.Atan(Mathf.Tan(Look.ViewFov * 0.5f * Mathf.Deg2Rad) / Mathf.Lerp(1f, 1.12f, a)) * Mathf.Rad2Deg;
            ViewModelFeature.Fov = viewFov;

            // Movement: bob while walking, lowered while sprinting, a dip on landing.
            var v = f.Own.vel;
            float speed = Mathf.Sqrt(v.x * v.x + v.y * v.y);
            bool grounded = f.Own.grounded != 0;
            bool sprinting = speed > 7.5f && !Ads && grounded;
            sprint = Mathf.Lerp(sprint, sprinting ? 1f : 0f, 1f - Mathf.Exp(-dt / 0.12f));
            if (grounded)
                bobPhase += speed * dt / 2.4f * Mathf.PI * 2f;
            if (!grounded)
                airTime += dt;
            else
            {
                if (!wasGrounded && airTime > 0.25f)
                    dip = Mathf.Clamp01(airTime * 1.2f);
                airTime = 0f;
            }
            wasGrounded = grounded;
            dip *= Mathf.Exp(-dt / 0.12f);
            float moving = Mathf.Min(speed / 6f, 1f) * (1f - 0.85f * a) * (grounded ? 1f : 0f);
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
            var adsPos = new Vector3(0f, 0f, Look.SightDistance) - Look.Sight * size;
            var pos = Vector3.Lerp(Look.Hip, adsPos, a) + Look.SprintOffset * sprint + bob
                      + new Vector3(0f, -0.035f * dip, -kb.x * kick) + new Vector3(sway.x * 0.002f, -sway.y * 0.002f, 0f);
            var rot = Quaternion.Euler(-kb.y * kick - sway.y + 2.5f * dip, sway.x, kickRoll * kick * 2f + bobRoll)
                      * Quaternion.Euler(Look.SprintEuler * sprint)
                      * Quaternion.Slerp(Quaternion.Euler(Look.HipEuler), Quaternion.identity, a);
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

            // Both hands on it, as soldiers hold it.
            if (arms != null)
            {
                var cam = view;
                var frame = cam.rotation * rot;
                var center = cam.TransformPoint(pos);
                arms.Body(Lattice.Players.SoldierColors.Material(f.Own.faction));
                arms.Hold(cam.TransformPoint(Look.RightCollar), cam.TransformPoint(Look.RightElbow),
                          center + frame * (Soldier.RightGrip * size), SoldierLook.Grip(frame, Soldier.RightFingers, Soldier.RightBack),
                          cam.TransformPoint(Look.LeftCollar), cam.TransformPoint(Look.LeftElbow),
                          center + frame * (Soldier.LeftGrip * size), SoldierLook.Grip(frame, Soldier.LeftFingers, Soldier.LeftBack),
                          Look.ShoulderSwing);
            }

            // Where the tracer leaves from: the drawn muzzle, as the world camera
            // sees that point (the view's field of view differs from the world's).
            var mv = pos + rot * (Look.Muzzle * size);
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
