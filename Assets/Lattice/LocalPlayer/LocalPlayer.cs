using Lattice.Native;
using Lattice.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lattice.LocalPlayer
{
    public enum ViewMode
    {
        FirstPerson,
        /// <summary>Behind and above our player.</summary>
        Chase,
        /// <summary>Free flight; our player stands still.</summary>
        Spectator,
    }

    /// <summary>What the gun does with the trigger each frame (Combat implements it):
    /// it may fire through the session and kick the view (recoil).</summary>
    public interface IGun
    {
        /// <summary>Down the sights now (the input's BUTTON_ADS).</summary>
        bool Ads { get; }
        /// <summary>The mouse's sensitivity now (slower down the sights).</summary>
        float SensitivityScale { get; }
        /// <summary>Called each frame before the inputs go out: <paramref name="trigger"/>
        /// held, <paramref name="aim"/> wanted; fire, then kick the view.</summary>
        void Frame(LtSession session, LocalPlayer player, bool trigger, bool aim);
    }

    /// <summary>
    /// The player's controls: mouse look, WASD, sprint and jump, the views and
    /// the toggles, or a wander for self-checks (--autoplay). It's the session's
    /// input source: every frame, before the inputs go out, it turns the
    /// controls into the intent (and lets the gun pull the trigger).
    ///
    /// The view's canonical form is the game's yaw and pitch (radians); the
    /// camera derives from it. Mouse right decreases yaw, mouse up raises pitch.
    /// </summary>
    public sealed class LocalPlayer : MonoBehaviour, IInputSource
    {
        public static LocalPlayer Instance { get; private set; }

        public ViewMode Mode = ViewMode.FirstPerson;
        /// <summary>The player's aim, game radians (first person and chase).</summary>
        public float Yaw, Pitch;
        /// <summary>The spectator camera: its eye (game space) and aim.</summary>
        public Vector3 SpecEye;
        public float SpecYaw, SpecPitch = -0.4f;
        public bool SpecPlaced;

        /// <summary>Radians per pixel of mouse movement.</summary>
        public float Sensitivity = 0.0025f;
        public bool Autoplay, Autofire, Autoaim;
        /// <summary>Self-check: turn toward the nearest enemy in reach and fire when on it.</summary>
        public bool Autotarget;
        public IGun Gun;

        /// <summary>Toggles the HUD reads: server ghosts, tier colors, net graph.</summary>
        public bool Ghosts, TierColors, NetGraph = true, Help;
        /// <summary>Soldiers colored by relation (ours blue-white, enemies orange and
        /// magenta), else by faction: I, remembered between games.</summary>
        public bool Iff = true;
        public const string IffPref = "Lattice.Iff";

        /// <summary>The mouse is ours: clicks fire, the right button aims.</summary>
        public bool Grabbed { get; private set; }
        /// <summary>This frame's movement wish, for animation and the viewmodel.</summary>
        public float Forward, Right;
        public bool Sprinting, Jumping;

        bool swallow;
        Wander wander = new Wander { seed = 0x9E3779B9u };

        const float MaxPitch = Mathf.PI / 2f - 0.01f;

        void OnEnable()
        {
            Instance = this;
            NetDriver.Input = this;
        }

        void OnDisable()
        {
            if (NetDriver.Input == (IInputSource)this)
                NetDriver.Input = null;
            if (Instance == this)
                Instance = null;
            Release();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus)
                Release();
        }

        public void Grab()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Grabbed = true;
        }

        public void Release()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Grabbed = false;
        }

        /// <summary>Start in free flight at <paramref name="eye"/> (game space).</summary>
        public void Spectate(Vector3 eye, float yaw, float pitch)
        {
            Mode = ViewMode.Spectator;
            SpecEye = eye;
            (SpecYaw, SpecPitch, SpecPlaced) = (yaw, pitch, true);
        }

        void IInputSource.Intent(LtSession session, ref LtIntent intent)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);

            // Grab and release the mouse; the grabbing click doesn't fire.
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Grabbed && Application.isFocused)
            {
                Grab();
                swallow = true;
            }
            if (kb != null && kb.escapeKey.wasPressedThisFrame && Grabbed)
                Release();
            if (kb != null)
            {
                if (kb.vKey.wasPressedThisFrame)
                {
                    Mode = Mode == ViewMode.FirstPerson ? ViewMode.Chase : Mode == ViewMode.Chase ? ViewMode.Spectator : ViewMode.FirstPerson;
                    if (Mode == ViewMode.Spectator && !SpecPlaced)
                        PlaceSpectatorBehind();
                }
                if (kb.gKey.wasPressedThisFrame) Ghosts = !Ghosts;
                if (kb.tKey.wasPressedThisFrame) TierColors = !TierColors;
                if (kb.iKey.wasPressedThisFrame)
                {
                    Iff = !Iff;
                    PlayerPrefs.SetInt(IffPref, Iff ? 1 : 0);
                }
                if (kb.nKey.wasPressedThisFrame) NetGraph = !NetGraph;
                if (kb.f1Key.wasPressedThisFrame) Help = !Help;
            }

            // Mouse look.
            if (Grabbed && mouse != null)
            {
                var d = mouse.delta.ReadValue() * Sensitivity * (Gun?.SensitivityScale ?? 1f);
                if (Mode == ViewMode.Spectator)
                {
                    SpecYaw = Coords.Wrap(SpecYaw - d.x);
                    SpecPitch = Mathf.Clamp(SpecPitch + d.y, -MaxPitch, MaxPitch);
                }
                else
                {
                    Yaw = Coords.Wrap(Yaw - d.x);
                    Pitch = Mathf.Clamp(Pitch + d.y, -MaxPitch, MaxPitch);
                }
            }

            float Axis(Key pos, Key neg) => kb == null ? 0f : (kb[pos].isPressed ? 1f : 0f) - (kb[neg].isPressed ? 1f : 0f);
            float fwd = Axis(Key.W, Key.S), right = Axis(Key.D, Key.A);
            byte buttons = 0;
            bool trigger = false, aim = false;
            if (Autoplay)
            {
                wander.Step(Time.realtimeSinceStartup, dt, ref Yaw, out fwd, out right, out bool sprint, out bool jump);
                if (sprint) buttons |= (byte)LtCode.ButtonSprint;
                if (jump) buttons |= (byte)LtCode.ButtonJump;
            }
            else if (Mode == ViewMode.Spectator)
            {
                // Free flight: WASD along the view, Space/C up and down, Shift faster.
                float speed = (kb != null && kb.leftShiftKey.isPressed ? 120f : 25f) * dt;
                float up = Axis(Key.Space, Key.C);
                float s = Mathf.Sin(SpecYaw), c = Mathf.Cos(SpecYaw);
                SpecEye += new Vector3((c * fwd + s * right) * speed, (s * fwd - c * right) * speed, up * speed);
                fwd = right = 0f;
            }
            else
            {
                if (kb != null && kb.leftShiftKey.isPressed) buttons |= (byte)LtCode.ButtonSprint;
                if (kb != null && kb.spaceKey.isPressed) buttons |= (byte)LtCode.ButtonJump;
            }

            if (mouse == null || !mouse.leftButton.isPressed)
                swallow = false;
            bool alive = NetDriver.Frame.Welcomed && NetDriver.Frame.Own.dead == 0;
            bool onTarget = Autotarget && alive && Mode != ViewMode.Spectator && Target(dt);
            if (Mode != ViewMode.Spectator && alive)
            {
                trigger = Autofire || onTarget || (Grabbed && !swallow && mouse != null && mouse.leftButton.isPressed);
                aim = Autoaim || (Grabbed && mouse != null && mouse.rightButton.isPressed);
            }
            Gun?.Frame(session, this, trigger, aim);
            if (Gun != null && Gun.Ads)
                buttons = (byte)((buttons & ~LtCode.ButtonSprint) | LtCode.ButtonAds);

            (Forward, Right) = (fwd, right);
            Sprinting = (buttons & LtCode.ButtonSprint) != 0 && fwd > 0f;
            Jumping = (buttons & LtCode.ButtonJump) != 0;
            intent.yaw = Yaw;
            intent.pitch = Pitch;
            intent.forward = fwd;
            intent.right = right;
            intent.buttons = buttons;
        }

        /// <summary>
        /// --autotarget: turn toward the nearest living enemy within 400 m (its
        /// chest, led by how it moves and raised for the slug's drop), at a
        /// hand's pace; true once the aim is on it. A self-check, like
        /// --autofire: the turn is ours, the shot the plugin's as ever.
        /// </summary>
        bool Target(float dt)
        {
            var f = NetDriver.Frame;
            var me = f.Own.pos;
            float best = 400f * 400f;
            int k = -1;
            for (int i = 0; i < f.EntityCount; i++)
            {
                var e = f.Entities[i];
                if (e.faction == f.Own.faction || (e.flags & LtCode.FlagDead) != 0)
                    continue;
                float dx = e.pos.x - me.x, dy = e.pos.y - me.y;
                float d2 = dx * dx + dy * dy;
                // Keep the one we're on unless another is much nearer.
                if (e.id == targetId)
                    d2 *= 0.6f;
                if (d2 < best)
                    (best, k) = (d2, i);
            }
            if (k < 0)
            {
                targetId = LtCode.Nobody;
                return false;
            }
            var t = f.Entities[k];
            var at = new Vector3(t.pos.x, t.pos.y, t.pos.z);
            // Its velocity from where it was drawn last frame (the same one only).
            if (t.id != targetId || dt <= 0f)
                targetVel = Vector3.zero;
            else
                targetVel = Vector3.Lerp(targetVel, (at - targetLast) / dt, 1f - Mathf.Exp(-dt / 0.2f));
            (targetId, targetLast) = (t.id, at);
            float tx = at.x - me.x, ty = at.y - me.y;
            float flight = Mathf.Sqrt(tx * tx + ty * ty) / LtConst.MuzzleSpeed;
            tx += targetVel.x * flight;
            ty += targetVel.y * flight;
            float tz = at.z + 1.2f + 0.5f * LtConst.Gravity * flight * flight - (me.z + LtConst.EyeHeight);
            float wantYaw = Mathf.Atan2(ty, tx);
            float wantPitch = Mathf.Atan2(tz, Mathf.Sqrt(tx * tx + ty * ty));
            float dYaw = Mathf.DeltaAngle(Yaw * Mathf.Rad2Deg, wantYaw * Mathf.Rad2Deg);
            float dPitch = (wantPitch - Pitch) * Mathf.Rad2Deg;
            float step = 260f * dt;
            Yaw = Coords.Wrap(Yaw + Mathf.Clamp(dYaw, -step, step) * Mathf.Deg2Rad);
            Pitch = Mathf.Clamp(Pitch + Mathf.Clamp(dPitch, -step, step) * Mathf.Deg2Rad, -MaxPitch, MaxPitch);
            return Mathf.Abs(dYaw) < 2f && Mathf.Abs(dPitch) < 2f;
        }

        ushort targetId = LtCode.Nobody;
        Vector3 targetLast, targetVel;

        /// <summary>Above and behind our player, looking at it.</summary>
        void PlaceSpectatorBehind()
        {
            var own = NetDriver.Frame.Own;
            SpecEye = new Vector3(own.pos.x - 35f, own.pos.y - 35f, own.pos.z + 30f);
            (SpecYaw, SpecPitch, SpecPlaced) = (Mathf.PI / 4f, -0.55f, true);
        }

        /// <summary>--autoplay: run in a random direction, change it every 1-3 s,
        /// sprint and jump now and then, look where it runs (the Bevy client's).</summary>
        struct Wander
        {
            public uint seed;
            float heading, until;
            bool sprint;

            float Rand()
            {
                seed = seed * 1664525u + 1013904223u;
                return (seed >> 8) / 16777216f;
            }

            public void Step(float t, float dt, ref float yaw, out float fwd, out float right, out bool sprintOut, out bool jump)
            {
                if (t > until)
                {
                    heading = Rand() * Mathf.PI * 2f;
                    until = t + 1f + 2f * Rand();
                    sprint = Rand() < 0.4f;
                }
                jump = Rand() < 0.004f;
                float turn = Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, heading * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                yaw = Coords.Wrap(yaw + turn * Mathf.Min(dt * 4f, 1f));
                // Run along the heading, whatever the view: forward and right in the view's frame.
                float rel = heading - yaw;
                fwd = Mathf.Cos(rel);
                right = -Mathf.Sin(rel);
                sprintOut = sprint;
            }
        }
    }
}
