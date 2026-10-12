using System;
using Lattice.Native;
using UnityEngine;

namespace Lattice.Combat
{
    /// <summary>
    /// The first-person reload (GDD §4.5): the rifle cants to show its left side,
    /// the left hand presses the cell's cap, the cell springs out, is pulled and
    /// tossed; a fresh one comes up from the pouch, slides into the bay and the
    /// palm seats it. From empty (the longer reload) the hand then twists the cap
    /// to charge it. Timed by the plugin's predicted reload (LtOwn.reload), so the
    /// rifle is back on the handguard exactly when it can fire again.
    ///
    /// Keyframed: the left wrist and the hand's grip (as SoldierLook's grips: the way
    /// the fingers point, the way the back of the hand faces) in the rifle's frame
    /// while it works on the rifle, in the camera's while it's away; Catmull-Rom
    /// through the positions, eased slerps between the grips. The rifle's own
    /// motion is keyed the same way. Everything here is camera space (x right,
    /// y up, z forward, m) or the rifle's frame at size 1 (x right, y up, z to the
    /// muzzle).
    /// </summary>
    public sealed class Reload
    {
        public enum Cue { Release, Out, Pouch, In, Seat, Charge }

        /// <summary>Raised as the motion passes each sound's moment.</summary>
        public event Action<Cue> Cued;

        /// <summary>A reload under way (or blending out after one ended early).</summary>
        public bool Active { get; private set; }
        /// <summary>From empty: the longer one, with the charge.</summary>
        public bool Empty { get; private set; }
        /// <summary>Seconds into it, and how long it takes.</summary>
        public float T { get; private set; }
        public float Total { get; private set; }
        /// <summary>How far into the reload pose (eased 0..1): the sights and the sprint
        /// pose give way to it.</summary>
        public float Weight { get; private set; }
        /// <summary>Reloads begun (for the report), and how many of them from empty.</summary>
        public int Started { get; private set; }
        public int StartedEmpty { get; private set; }

        /// <summary>Debugging (the editor, stills): show the reload at this many seconds
        /// in (empty or not) instead of what the plugin says; negative: off.</summary>
        public static float DebugAt = -1f;
        public static bool DebugEmpty;

        float start, abortAt = -1f, abortFrom;
        int cue;
        Cue[] cues;
        float[] cueAt;

        // ---- the choreography --------------------------------------------------

        /// <summary>A hand key: the wrist and the grip, in the rifle's frame or the camera's.</summary>
        struct HandKey
        {
            public float T;
            public bool Camera;
            public Vector3 Wrist, Fingers, Back;
        }

        /// <summary>A rifle key: an offset (camera space, m) and a turn in its own frame (degrees).</summary>
        struct RifleKey
        {
            public float T;
            public Vector3 Offset, Euler;
        }

        // Where things are on the rifle (its frame at size 1): the left cap's face,
        // and how far the cell springs out and comes all the way out.
        Vector3 seat, cap;
        float half;
        const float Pop = 0.07f;
        float OutAt => -2f * half - 0.025f;

        // The left hand's hold: the one it has on the handguard (SoldierLook: its
        // wrist, the way its fingers point and its back faces), taken as an offset
        // from the axis it closes round (the handguard's, under the hand). The
        // mesh's hand is closed to fit that. Turned a quarter about the vertical, so
        // the axis lies along the cell (fingers toward the rifle, curling under), the
        // same hold closes round the cell; and the wrist stays ahead of the hand, so
        // the forearm reaches forward and down, away from the eye, never across the bay.
        static readonly Vector3 HandguardAxis = new Vector3(0f, 0.03f, 0.133f);
        static readonly Quaternion ToCell = Quaternion.Euler(0f, 90f, 0f);
        Vector3 holdOffset, holdFingers, holdBack;
        /// <summary>How far in from its outer cap the hand holds the cell.</summary>
        const float HoldIn = 0.035f;

        HandKey[] hand;
        RifleKey[] rifle;
        // When the cell changes hands (seconds), for the tactical reload; the empty
        // one shifts what follows the seat by its charge.
        const float PressAt = 0.33f, PopDone = 0.41f, TossAt = 0.76f, PouchAt = 1.05f, AlignAt = 1.48f, InsertDone = 1.6f,
            OpenAt = 1.64f, SeatAt = 1.70f;
        const float ChargeTime = 0.5f, Twist = 60f;

        public Reload(Vector3 seat, float half)
        {
            this.seat = seat;
            this.half = half;
            cap = new Vector3(-half, seat.y, seat.z);
        }

        /// <summary>A point on the cell's axis (the bay's), <paramref name="x"/> along it.</summary>
        Vector3 Axis(float x) => new Vector3(x, seat.y, seat.z);

        /// <summary>The hold's key with the hand round the axis at <paramref name="g"/>, turned <paramref name="turn"/> about the axis.</summary>
        HandKey Hold(float t, Vector3 g, float turn = 0f)
        {
            var r = Quaternion.AngleAxis(turn, Vector3.right);
            return new HandKey { T = t, Wrist = g + r * holdOffset, Fingers = r * holdFingers, Back = r * holdBack };
        }

        void Build(bool empty, Vector3 grip, Vector3 gripFingers, Vector3 gripBack)
        {
            float k = empty ? ChargeTime : 0f;
            float total = empty ? LtConst.ReloadEmptySteps / (float)LtConst.TickHz : LtConst.ReloadSteps / (float)LtConst.TickHz;
            holdOffset = ToCell * (grip - HandguardAxis);
            holdFingers = ToCell * gripFingers;
            holdBack = ToCell * gripBack;
            // Where the hand is round the axis: at the cap's face (just out from it), on
            // the popped cell, and on the fresh one as it lines up with the bay.
            float face = -half;
            var onCap = Axis(face - 0.025f);
            var onPopped = Axis(face - Pop + HoldIn);
            var onFresh = Axis(OutAt - 0.01f - half + HoldIn);
            var keys = new System.Collections.Generic.List<HandKey>
            {
                new HandKey { T = 0f, Wrist = grip, Fingers = gripFingers, Back = gripBack },
                Hold(0.26f, onCap - new Vector3(0.035f, 0f, 0f)),
                Hold(PressAt, onCap + new Vector3(0.004f, 0f, 0f)),
                Hold(PopDone, onPopped),
                Hold(0.62f, onPopped + new Vector3(OutAt + Pop, -0.012f, -0.02f)),
                new HandKey { T = TossAt, Camera = true, Wrist = new Vector3(-0.3f, -0.4f, 0.34f), Fingers = new Vector3(-0.2f, -0.7f, 0.68f), Back = new Vector3(-0.6f, 0.6f, 0.45f) },
                new HandKey { T = PouchAt, Camera = true, Wrist = new Vector3(-0.3f, -0.72f, 0.16f), Fingers = new Vector3(0.05f, -0.98f, 0.18f), Back = new Vector3(-0.98f, 0f, 0.2f) },
                new HandKey { T = 1.28f, Camera = true, Wrist = new Vector3(-0.24f, -0.42f, 0.4f), Fingers = new Vector3(0.6f, -0.3f, 0.74f), Back = new Vector3(-0.2f, 0.9f, 0.4f) },
                Hold(AlignAt, onFresh),
                Hold(InsertDone, onPopped),
                Hold(OpenAt, Axis(face - Pop - 0.025f)),
                Hold(SeatAt, onCap + new Vector3(0.003f, 0f, 0f)),
            };
            if (empty)
            {
                // The charge: the hand turns the cap a sixth (the cell looks the same
                // every 60 degrees, so it can be handed back unturned), then lets go.
                keys.Add(Hold(SeatAt + 0.1f, onCap + new Vector3(0.003f, 0f, 0f)));
                keys.Add(Hold(SeatAt + 0.32f, onCap + new Vector3(0.003f, 0f, 0f), -Twist));
                keys.Add(Hold(SeatAt + 0.4f, onCap - new Vector3(0.03f, 0f, 0f), -Twist));
            }
            keys.Add(new HandKey { T = 1.95f + k, Wrist = grip, Fingers = gripFingers, Back = gripBack });
            keys.Add(new HandKey { T = total + 1f, Wrist = grip, Fingers = gripFingers, Back = gripBack });
            hand = keys.ToArray();
            // Rolled hard (its left side, with the bay, up toward the eye, which looks
            // down on it), brought in and up a little, the muzzle still forward,
            // dipped a touch: the bay half way down the screen, just right of middle.
            var canted = new Vector3(8f, -8f, -55f);
            var at = new Vector3(-0.096f, 0.038f, 0.015f);
            var rk = new System.Collections.Generic.List<RifleKey>
            {
                new RifleKey { T = 0f },
                new RifleKey { T = 0.28f, Offset = at, Euler = canted },
                new RifleKey { T = 0.38f, Offset = at + new Vector3(0.004f, 0f, 0f), Euler = canted + new Vector3(0f, 0f, 3f) },
                new RifleKey { T = 0.62f, Offset = at + new Vector3(0.01f, 0.005f, 0f), Euler = canted + new Vector3(1f, -2f, -2f) },
                new RifleKey { T = 0.95f, Offset = at + new Vector3(0.01f, -0.02f, 0.01f), Euler = canted + new Vector3(-2f, 3f, 5f) },
                new RifleKey { T = 1.35f, Offset = at, Euler = canted },
                new RifleKey { T = InsertDone, Offset = at + new Vector3(0.006f, 0f, 0f), Euler = canted + new Vector3(0f, -1f, 2f) },
                new RifleKey { T = SeatAt + k, Offset = at, Euler = canted + new Vector3(0f, 0f, 2f) },
                new RifleKey { T = 2.02f + k, Offset = new Vector3(0.004f, 0.006f, 0f), Euler = new Vector3(1.5f, -1f, 2f) },
                new RifleKey { T = total, Offset = Vector3.zero, Euler = Vector3.zero },
                new RifleKey { T = total + 1f },
            };
            rifle = rk.ToArray();

            cues = empty
                ? new[] { Cue.Release, Cue.Out, Cue.Pouch, Cue.In, Cue.Seat, Cue.Charge }
                : new[] { Cue.Release, Cue.Out, Cue.Pouch, Cue.In, Cue.Seat };
            cueAt = empty
                ? new[] { PressAt, 0.43f, PouchAt - 0.08f, AlignAt + 0.02f, SeatAt, SeatAt + 0.12f }
                : new[] { PressAt, 0.43f, PouchAt - 0.08f, AlignAt + 0.02f, SeatAt };
        }

        // ---- each frame --------------------------------------------------------

        /// <summary>
        /// Follows the plugin's reload: <paramref name="left"/> seconds of it left
        /// (0: none). Starts the motion when one begins, keeps its clock in step with
        /// the plugin's (whole steps) and lets it go, blending out, if it ends early.
        /// </summary>
        public void Follow(float left, float now, Vector3 grip, Vector3 gripFingers, Vector3 gripBack)
        {
            float tactical = LtConst.ReloadSteps / (float)LtConst.TickHz;
            float empty = LtConst.ReloadEmptySteps / (float)LtConst.TickHz;
            if (DebugAt >= 0f)
            {
                if (!Active || Empty != DebugEmpty || hand == null)
                    Begin(DebugEmpty, now, grip, gripFingers, gripBack);
                T = DebugAt;
                start = now - T;
                Weight = Pose(T);
                return;
            }
            if (left > 0f)
            {
                if (!Active || abortAt >= 0f)
                {
                    bool fromEmpty = left > 0.5f * (tactical + empty);
                    Begin(fromEmpty, now, grip, gripFingers, gripBack);
                    Started++;
                    StartedEmpty += fromEmpty ? 1 : 0;
                }
                // The plugin counts whole steps: a reload with n steps left is
                // between n and n-1 from its end. Ours runs smooth; if it's off by
                // more than that (a correction), it jumps.
                float plugin = Total - left;
                float t = now - start;
                if (t < plugin - 0.02f || t > plugin + 1f / LtConst.TickHz + 0.03f)
                    start = now - plugin;
                T = Mathf.Min(now - start, Total);
            }
            else if (Active)
            {
                // Done, or cut short (a death, a respawn, a correction): run out what
                // little is left, or blend out from where it was.
                if (now - start >= Total - 0.08f && abortAt < 0f)
                {
                    T = now - start;
                    if (T > Total + 0.25f)
                        Active = false;
                }
                else
                {
                    if (abortAt < 0f)
                        (abortAt, abortFrom) = (now, Weight);
                    T = now - start;
                    if (now - abortAt > 0.18f)
                        Active = false;
                }
            }
            Weight = Active ? Pose(T) * (abortAt >= 0f ? Mathf.Clamp01(1f - (now - abortAt) / 0.18f) : 1f) : 0f;
            // The sounds, as the motion passes them (not after an abort).
            while (Active && abortAt < 0f && cues != null && cue < cues.Length && T >= cueAt[cue])
                Cued?.Invoke(cues[cue++]);
        }

        // Each kind's keys, built once (the first reload of each, or Prepare): never per frame.
        readonly HandKey[][] handKeys = new HandKey[2][];
        readonly RifleKey[][] rifleKeys = new RifleKey[2][];
        readonly Cue[][] cueKeys = new Cue[2][];
        readonly float[][] cueTimes = new float[2][];

        /// <summary>Builds both kinds' keys now (at startup), so no reload allocates.</summary>
        public void Prepare(Vector3 grip, Vector3 gripFingers, Vector3 gripBack)
        {
            for (int k = 0; k < 2; k++)
            {
                Build(k == 1, grip, gripFingers, gripBack);
                (handKeys[k], rifleKeys[k], cueKeys[k], cueTimes[k]) = (hand, rifle, cues, cueAt);
            }
        }

        void Begin(bool empty, float now, Vector3 grip, Vector3 gripFingers, Vector3 gripBack)
        {
            Empty = empty;
            Total = empty ? LtConst.ReloadEmptySteps / (float)LtConst.TickHz : LtConst.ReloadSteps / (float)LtConst.TickHz;
            int kind = empty ? 1 : 0;
            if (handKeys[kind] == null)
                Prepare(grip, gripFingers, gripBack);
            (hand, rifle, cues, cueAt) = (handKeys[kind], rifleKeys[kind], cueKeys[kind], cueTimes[kind]);
            // (The hold the cell-in-hand reads is the same for both kinds.)
            holdOffset = ToCell * (grip - HandguardAxis);
            holdFingers = ToCell * gripFingers;
            holdBack = ToCell * gripBack;
            start = now;
            T = 0f;
            cue = 0;
            abortAt = -1f;
            Active = true;
        }

        /// <summary>How far into the reload pose at <paramref name="t"/>: in over the
        /// first 0.25 s, out over the last 0.3.</summary>
        float Pose(float t)
        {
            float a = Mathf.Clamp01(t / 0.25f), b = Mathf.Clamp01((Total - t) / 0.3f);
            return Smooth(Mathf.Min(a, b));
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>The rifle's motion now: an offset (camera space) and a turn in its frame.</summary>
        public void Rifle(out Vector3 offset, out Quaternion turn)
        {
            if (!Active || rifle == null)
            {
                offset = Vector3.zero;
                turn = Quaternion.identity;
                return;
            }
            float w = abortAt >= 0f ? Weight : 1f;
            int i = 0;
            while (i < rifle.Length - 2 && T >= rifle[i + 1].T)
                i++;
            float u = Mathf.InverseLerp(rifle[i].T, rifle[i + 1].T, T);
            var a = rifle[Mathf.Max(i - 1, 0)];
            var b = rifle[i];
            var c = rifle[i + 1];
            var d = rifle[Mathf.Min(i + 2, rifle.Length - 1)];
            offset = CatmullRom(a.Offset, b.Offset, c.Offset, d.Offset, u) * w;
            var e = CatmullRom(a.Euler, b.Euler, c.Euler, d.Euler, u) * w;
            // The seat's jolt: a quick kick right and back, gone in a tenth of a second.
            float seatT = T - (SeatAt + 0f);
            if (seatT > 0f && seatT < 0.25f)
            {
                float j = Mathf.Exp(-seatT / 0.05f) * Mathf.Sin(seatT * 60f) * w;
                offset += new Vector3(0.006f, -0.003f, 0f) * j;
                e += new Vector3(-1.2f, 0f, 2.5f) * j;
            }
            float popT = T - PressAt;
            if (popT > 0f && popT < 0.2f)
            {
                float j = Mathf.Exp(-popT / 0.04f) * w;
                e += new Vector3(-0.8f, 0f, 1.6f) * j;
            }
            turn = Quaternion.Euler(e);
        }

        /// <summary>
        /// The left hand now (camera space): the wrist and the grip it holds with.
        /// <paramref name="frame"/> is the rifle's frame in camera space (its center,
        /// its rotation) and <paramref name="size"/> its scale; <paramref name="weight"/>
        /// how much of it to take over the hand's place on the handguard. False when not reloading.
        /// </summary>
        public bool Hand(Vector3 center, Quaternion frame, float size, out Vector3 wrist, out Quaternion grip, out float weight)
        {
            wrist = default;
            grip = default;
            // Cut short, the hand goes back to the handguard with the rifle.
            weight = abortAt >= 0f ? Weight : 1f;
            if (!Active || hand == null)
                return false;
            int i = 0;
            while (i < hand.Length - 2 && T >= hand[i + 1].T)
                i++;
            float u = Mathf.InverseLerp(hand[i].T, hand[i + 1].T, T);
            Vector3 P(int k) => Place(hand[Mathf.Clamp(k, 0, hand.Length - 1)], center, frame, size);
            wrist = CatmullRom(P(i - 1), P(i), P(i + 1), P(i + 2), u);
            var ga = Rot(hand[i], frame);
            var gb = Rot(hand[i + 1], frame);
            grip = Quaternion.Slerp(ga, gb, Smooth(u));
            return true;
        }

        static Vector3 Place(HandKey k, Vector3 center, Quaternion frame, float size) =>
            k.Camera ? k.Wrist : center + frame * (k.Wrist * size);

        static Quaternion Rot(HandKey k, Quaternion frame)
        {
            var q = Quaternion.LookRotation(k.Fingers, k.Back);
            return k.Camera ? q : frame * q;
        }

        /// <summary>Where the cell is now.</summary>
        public enum CellState { Seated, InHand, Dropped, Gone }

        /// <summary>
        /// The cell: seated (pushed out along the rifle's left by <paramref name="pulled"/>,
        /// m at size 1), in the hand, just let go (the gun throws it), or not there
        /// (between the toss and the pouch). <paramref name="fresh"/>: it's the new
        /// one; <paramref name="glow"/> how charged it looks (0..1).
        /// </summary>
        public CellState Cell(float charge, out float pulled, out bool fresh, out float glow)
        {
            pulled = 0f;
            fresh = false;
            glow = charge;
            if (!Active || abortAt >= 0f)
                return CellState.Seated;
            float t = T, k = Empty ? ChargeTime : 0f;
            if (t < PressAt)
                return CellState.Seated;
            if (t < PopDone)
            {
                // Springs out, a little past, and settles.
                float u = (t - PressAt) / (PopDone - PressAt);
                float s = 1f + 2.70158f * Mathf.Pow(u - 1f, 3f) + 1.70158f * Mathf.Pow(u - 1f, 2f);
                pulled = Pop * s;
                return CellState.Seated;
            }
            if (t < TossAt)
                return CellState.InHand;
            if (t < PouchAt)
                return CellState.Dropped;
            fresh = true;
            if (t < InsertDone)
            {
                glow = 1f;
                return CellState.InHand;
            }
            if (t < SeatAt)
            {
                float u = Mathf.Clamp01((t - OpenAt) / (SeatAt - OpenAt));
                pulled = Pop * (1f - u * u);
                glow = 0.35f;
                return CellState.Seated;
            }
            // Seated; it lights up: at once from a tactical reload, with the twist
            // from empty.
            float on = Empty ? Mathf.Clamp01((t - (SeatAt + 0.12f)) / 0.3f) : Mathf.Clamp01((t - SeatAt) / 0.15f);
            glow = Mathf.Lerp(0.35f, 1f, on);
            return CellState.Seated;
        }

        /// <summary>
        /// The cell in the hand (camera space): the hold closes on it a little in from
        /// its outer cap, turned as it sits in the rifle when the hand is lined up
        /// with the bay (so taking it out and putting it in never jumps). <paramref name="meshEuler"/>: the mesh's turn into the
        /// rifle's frame (CombatLook.ViewRifleEuler).
        /// </summary>
        public void CellInHand(Vector3 wrist, Quaternion grip, float size, Vector3 meshEuler, out Vector3 pos, out Quaternion rot)
        {
            // The hand's turn back into the rifle's frame, as the hold has it: from the
            // wrist to the axis under the hand, then in along the cell to its middle.
            var toFrame = grip * Quaternion.Inverse(Quaternion.LookRotation(holdFingers, holdBack));
            pos = wrist + toFrame * ((-holdOffset + new Vector3(half - HoldIn, 0f, 0f)) * size);
            rot = toFrame * Quaternion.Euler(meshEuler);
        }

        /// <summary>The cap's turn about the cell's axis now (the empty reload's twist), degrees.</summary>
        public float CapTurn
        {
            get
            {
                if (!Active || !Empty || abortAt >= 0f)
                    return 0f;
                float u = (T - (SeatAt + 0.1f)) / 0.22f;
                // Turned, then back to none at once when the hand has let go: the same look.
                return u >= 1.3f ? 0f : -Twist * Smooth(Mathf.Clamp01(u));
            }
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
