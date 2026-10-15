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

        /// <summary>Radians per pixel of mouse movement; down the sights, times this too.</summary>
        public float Sensitivity = 0.0025f;
        public float AdsSensitivity = 1f;
        /// <summary>A screen of the shell is up (the deploy screen, the map, the menu):
        /// the mouse is its, and the player stands still and holds its fire.</summary>
        public static bool UiOpen;
        public bool Autoplay, Autofire, Autoaim;
        /// <summary>Self-check: turn toward the nearest enemy in reach and fire when on it.</summary>
        public bool Autotarget;
        public IGun Gun;

        /// <summary>Toggles the HUD reads: server ghosts, tier colors, net graph (closed
        /// until opened, remembered between games), the controls card.</summary>
        public bool Ghosts, TierColors, NetGraph, Help;
        public const string NetGraphPref = "Lattice.NetGraph";
        /// <summary>Standing still, not firing, whatever the keys say: while the world is
        /// still being built behind the join card (set by the game each frame).</summary>
        public bool Held;
        /// <summary>Soldiers colored by relation (ours blue-white, enemies orange and
        /// magenta), else by faction: I, remembered between games.</summary>
        public bool Iff = true;
        public const string IffPref = "Lattice.Iff";

        /// <summary>The mouse is ours: clicks fire, the right button aims.</summary>
        public bool Grabbed { get; private set; }
        /// <summary>This frame's movement wish, for animation and the viewmodel.</summary>
        public float Forward, Right;
        public bool Sprinting, Jumping, Crouching;
        /// <summary>Self-check (--crouch): crouched whenever it plays (with --autotarget, it
        /// fights crouched).</summary>
        public bool AutoCrouch;
        /// <summary>Self-check (--ability): uses its class's ability now and then, as the
        /// bots do, but more often (jets held 0.8 s every 4 s, the rest pressed every 5 s).</summary>
        public bool AutoAbility;

        bool swallow, crouchToggle, jumpHeld;
        float reloadUntil;
        Wander wander = new Wander { seed = 0x9E3779B9u };

        const float MaxPitch = Mathf.PI / 2f - 0.01f;
        /// <summary>How long a reload press is held out to the plugin at most (its net
        /// thread makes an input every 33 ms from the latest intent): until the reload
        /// shows as under way, or this long (a full cell: nothing to do).</summary>
        const float ReloadPress = 0.25f;

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
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Grabbed && Application.isFocused && !UiOpen)
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
                if (kb.nKey.wasPressedThisFrame)
                {
                    NetGraph = !NetGraph;
                    PlayerPrefs.SetInt(NetGraphPref, NetGraph ? 1 : 0);
                }
                if (kb.f1Key.wasPressedThisFrame) Help = !Help;
            }

            // Mouse look.
            if (Grabbed && mouse != null)
            {
                var d = mouse.delta.ReadValue() * Sensitivity * (Gun?.SensitivityScale ?? 1f) * (Gun != null && Gun.Ads ? AdsSensitivity : 1f);
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
            if (Held || UiOpen)
                fwd = right = 0f;
            else if (Autoplay)
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
                // Our player stays as it was: crouched, if it was toggled so.
                if (crouchToggle || AutoCrouch)
                    buttons |= (byte)LtCode.ButtonCrouch;
            }
            else
            {
                bool sprintKey = kb != null && kb.leftShiftKey.isPressed;
                bool jumpKey = kb != null && kb.spaceKey.isPressed;
                // C toggles a crouch, Ctrl holds one. From a toggled crouch, sprinting
                // or jumping stands up (the jump waits for the key to come back up).
                if (kb != null && kb.cKey.wasPressedThisFrame)
                    crouchToggle = !crouchToggle;
                if (crouchToggle && kb != null && ((kb.leftShiftKey.wasPressedThisFrame && fwd > 0f) || kb.spaceKey.wasPressedThisFrame))
                {
                    crouchToggle = false;
                    jumpHeld = kb.spaceKey.wasPressedThisFrame;
                }
                if (!jumpKey)
                    jumpHeld = false;
                bool crouch = crouchToggle || AutoCrouch || (kb != null && kb.leftCtrlKey.isPressed);
                if (sprintKey) buttons |= (byte)LtCode.ButtonSprint;
                if (jumpKey && !jumpHeld) buttons |= (byte)LtCode.ButtonJump;
                // Crouched: no sprint, no jump (the game's rule; sent so, the input says what happens).
                if (crouch)
                    buttons = (byte)((buttons | LtCode.ButtonCrouch) & ~(LtCode.ButtonSprint | LtCode.ButtonJump));
                if (kb != null && kb.rKey.wasPressedThisFrame)
                    reloadUntil = Time.unscaledTime + ReloadPress;
            }

            if (mouse == null || !mouse.leftButton.isPressed)
                swallow = false;
            var own = NetDriver.Frame.Own;
            bool alive = NetDriver.Frame.Welcomed && own.dead == 0 && !Held && !UiOpen;
            bool onTarget = Autotarget && alive && Mode != ViewMode.Spectator && Target(dt);
            // The self-check tops up between fights, as a player would.
            if (Autotarget && alive && !onTarget && targetId == LtCode.Nobody && own.reload <= 0f && own.reserve > 0 && own.mag < Weapons.Of(own.weapon).magazine / 3)
                reloadUntil = Time.unscaledTime + ReloadPress;
            // The press is held out until the reload shows as under way: held past
            // the reload's end, the next shot would start another.
            if (own.reload > 0f || !alive)
                reloadUntil = 0f;
            if (Time.unscaledTime < reloadUntil && Mode != ViewMode.Spectator)
                buttons |= (byte)LtCode.ButtonReload;
            // The class's ability (U6): F, held as long as it's down (jets); a press
            // for the rest (the game takes its going down).
            if (alive && Mode != ViewMode.Spectator && ((kb != null && kb.fKey.isPressed) || AutoUse(own.kit)))
                buttons |= (byte)LtCode.ButtonAbility;
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
            Crouching = (buttons & LtCode.ButtonCrouch) != 0;
            intent.yaw = Yaw;
            intent.pitch = Pitch;
            intent.forward = fwd;
            intent.right = right;
            intent.buttons = buttons;
        }

        /// <summary>
        /// --autotarget: turn toward the nearest living enemy in sight within
        /// its weapon's reach (its chest, led by how it moves and raised for the
        /// slug's drop), at a hand's pace; true once the aim is on it. A
        /// self-check, like --autofire: the turn is ours, the shot the plugin's as ever.
        /// </summary>
        bool Target(float dt)
        {
            var f = NetDriver.Frame;
            var me = f.Own.pos;
            float reach = Reach(f.Own.weapon);
            float best = reach * reach;
            int k = -1;
            var world = NetDriver.World;
            float eye = f.Own.crouched != 0 ? LtConst.CrouchEyeHeight : LtConst.EyeHeight;
            var from = new LtVec3 { x = me.x, y = me.y, z = me.z + eye };
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
                if (d2 >= best)
                    continue;
                // Only one we can see: through a wall it's a waste of rounds.
                var chestAt = new LtVec3 { x = e.pos.x, y = e.pos.y, z = e.pos.z + 1.2f };
                if (world != null && world.Raycast(from, chestAt, out _))
                    continue;
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
            float flight = Mathf.Sqrt(tx * tx + ty * ty) / Weapons.Of(f.Own.weapon).speed;
            tx += targetVel.x * flight;
            ty += targetVel.y * flight;
            // The chest, standing or crouched; from our own eyes, standing or crouched.
            float chest = (t.flags & LtCode.FlagCrouched) != 0 ? 0.8f : 1.2f;
            float tz = at.z + chest + 0.5f * LtConst.Gravity * flight * flight - (me.z + eye);
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

        /// <summary>--ability: whether the self-check holds its ability's button now.</summary>
        bool AutoUse(int kit)
        {
            if (!AutoAbility)
                return false;
            bool jets = kit == LtCode.ClassSkirmisher;
            return Time.unscaledTime % (jets ? 4f : 5f) < (jets ? 0.8f : 0.15f);
        }

        /// <summary>How far the self-check takes on an enemy with kit <paramref name="kit"/>'s
        /// weapon: twice where its damage stops falling off, or as far as its slug flies (at most 400 m).</summary>
        static unsafe float Reach(int kit)
        {
            ref readonly var w = ref Weapons.Of(kit);
            float flies = w.speed * w.rangeSecs;
            float reach = w.damage[0] != w.farDamage[0] && w.falloff[1] > 0f ? 2f * w.falloff[1] : flies;
            return Mathf.Clamp(Mathf.Min(reach, flies), 20f, 400f);
        }

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
