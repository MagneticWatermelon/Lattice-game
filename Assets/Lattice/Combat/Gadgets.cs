using System.Collections.Generic;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.Players;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Combat
{
    /// <summary>
    /// The classes' gadgets (U6), from the plugin's frame (lt_gadgets: each
    /// flown to the render step by the same flight as the server's): frags,
    /// smoke grenades, C4, mines, ammo packs, recon darts and engineers'
    /// turrets, each a small procedural model in gunmetal with lights in its
    /// side's color (blinking as a fuse runs down, as C4 waits, as a mine
    /// arms); a turret's head turns and tilts to where it last fired. Smoke
    /// fills its cloud; bursts (<c>LT_FX_BLAST</c>) are a fireball, smoke,
    /// thrown dirt and sparks, a flash of light and a shake of the view.
    /// Gadgets' moments that sound (thrown, landed, built, armed, smoke
    /// going, hacked) are left in <see cref="Cues"/> for the audio.
    /// Nothing allocates per frame.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public sealed class Gadgets : MonoBehaviour
    {
        public CombatLook Look;

        /// <summary>A gadget's moment to hear, this frame (<see cref="Cues"/>).</summary>
        public enum CueKind : byte { Thrown, Landed, Built, Armed, Smoke, Dart, Hacked, Beep }

        public struct Cue
        {
            public CueKind Kind;
            public byte Gadget;
            /// <summary>Where, Unity space.</summary>
            public Vector3 Pos;
        }

        /// <summary>This frame's cues (read after LateUpdate: the audio runs later).</summary>
        public static readonly Cue[] Cues = new Cue[64];
        public static int CueCount;

        Mesh[] meshes;
        Mesh turretBase, turretHead;
        RenderParams body, trim, olive, can;
        readonly RenderParams[] glow = new RenderParams[3];
        RenderParams danger, white;
        Material[] glowMats;
        Material dangerMat, whiteMat;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // What each gadget was last frame, by id (sorted): resting, its side, smoke going, armed.
        const int Max = 1024;
        uint[] prevIds = new uint[Max], curIds = new uint[Max];
        byte[] prevState = new byte[Max], curState = new byte[Max];
        int prevCount;
        const byte StateResting = 1, StateSmoke = 2, StateArmed = 4;
        bool seenAny;

        ParticleSystem smoke, fire, debris, dirt;
        readonly Light[] lights = new Light[4];
        readonly float[] lightAt = new float[4], lightPeak = new float[4];
        int nextLight;
        float smokeOwed;
        uint rng = 0x6AD6u;

        public int Drawn { get; private set; }
        // For the report: the most gadgets known and drawn at once, ours seen, bursts, cues.
        int maxKnown, maxDrawn, oursSeen, nBlasts, nCues, nSmoke;

        string Summary() =>
            $"  gadgets: up to {maxKnown} known and {maxDrawn} drawn at once; ours seen {oursSeen}; bursts {nBlasts}; smoke puffs {nSmoke}; sound cues {nCues}; turret shots seen {nTurretShots}; enemies spotted at most {maxSpotted}\n";
        int nTurretShots, maxSpotted;

        float Rand()
        {
            rng = rng * 1664525u + 1013904223u;
            return (rng >> 8) / 16777216f;
        }

        void Start()
        {
            if (Look == null || Look.CellBody == null)
            {
                enabled = false;
                return;
            }
            meshes = new Mesh[LtCode.GadgetShell + 1];
            meshes[LtCode.GadgetFrag] = GadgetMeshes.Frag();
            meshes[LtCode.GadgetSmoke] = GadgetMeshes.SmokeCan();
            meshes[LtCode.GadgetC4] = GadgetMeshes.C4();
            meshes[LtCode.GadgetMine] = GadgetMeshes.Mine();
            meshes[LtCode.GadgetAmmoPack] = GadgetMeshes.AmmoPack();
            meshes[LtCode.GadgetDart] = GadgetMeshes.Dart();
            turretBase = GadgetMeshes.TurretBase();
            turretHead = GadgetMeshes.TurretHead();

            // Painted steel light enough to read in a dim room, darker fittings, olive
            // drab for the frag and the pack, a pale can for smoke.
            body = Params(Paint("Gadget body", new Color(0.36f, 0.38f, 0.4f), 0.55f, 0.45f), true);
            trim = Params(Paint("Gadget trim", new Color(0.1f, 0.105f, 0.11f), 0.8f, 0.6f), true);
            olive = Params(Paint("Gadget olive", new Color(0.3f, 0.33f, 0.21f), 0.15f, 0.35f), true);
            can = Params(Paint("Gadget can", new Color(0.62f, 0.63f, 0.6f), 0.4f, 0.4f), true);
            glowMats = new Material[3];
            for (int f = 0; f < 3; f++)
            {
                glowMats[f] = new Material(Look.CellGlow) { name = $"Gadget glow {f}" };
                glow[f] = Params(glowMats[f], false);
            }
            dangerMat = new Material(Look.CellGlow) { name = "Gadget danger" };
            dangerMat.SetColor(EmissionId, new Color(6f, 0.25f, 0.12f, 1f));
            danger = Params(dangerMat, false);
            whiteMat = new Material(Look.CellGlow) { name = "Gadget white" };
            whiteMat.SetColor(EmissionId, new Color(3f, 3f, 3f, 1f));
            white = Params(whiteMat, false);

            // Smoke (alpha-blended puffs, long-lived), fire (added), debris (sparks
            // and grit thrown up, falling back), dirt (dark clods, alpha).
            smoke = Impacts.Make(transform, "Gadget smoke", Look.Dust, 4000);
            var sm = smoke.main;
            sm.gravityModifier = -0.012f;
            var sc = smoke.colorOverLifetime;
            sc.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            sc.color = g;
            var ss = smoke.sizeOverLifetime;
            ss.enabled = true;
            ss.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.25f));
            var sv = smoke.limitVelocityOverLifetime;
            sv.enabled = true;
            sv.dampen = 0.06f;
            sv.limit = 0.6f;

            fire = Impacts.Make(transform, "Blast fire", Look.Sparks, 1500);
            var fc = fire.colorOverLifetime;
            fc.enabled = true;
            var fg = new Gradient();
            fg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.4f), new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fc.color = fg;
            var fs = fire.sizeOverLifetime;
            fs.enabled = true;
            fs.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
            var fv = fire.limitVelocityOverLifetime;
            fv.enabled = true;
            fv.dampen = 0.25f;
            fv.limit = 1f;

            debris = Impacts.Make(transform, "Blast debris", Look.Sparks, 1500);
            var dm = debris.main;
            dm.gravityModifier = 1f;
            var dr = debris.GetComponent<ParticleSystemRenderer>();
            dr.renderMode = ParticleSystemRenderMode.Stretch;
            dr.velocityScale = 0.04f;
            dr.lengthScale = 1f;

            dirt = Impacts.Make(transform, "Blast dirt", Look.Dust, 1500);
            var tm = dirt.main;
            tm.gravityModifier = 1f;

            for (int i = 0; i < lights.Length; i++)
            {
                var lg = new GameObject($"Blast light {i}");
                lg.transform.SetParent(transform, false);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.62f, 0.3f);
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights[i] = l;
            }
            Lattice.Diagnostics.Report.Sections.Add(Summary);
        }

        void OnDestroy() => Lattice.Diagnostics.Report.Sections.Remove(Summary);

        Material Paint(string name, Color color, float metallic, float smoothness)
        {
            var m = new Material(Look.CellBody) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static RenderParams Params(Material m, bool shadows) => new RenderParams(m)
        {
            shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = shadows,
        };

        void LateUpdate()
        {
            CueCount = 0;
            var f = NetDriver.Frame;
            if (meshes == null)
                return;
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            for (int k = 0; k < 3; k++)
            {
                var col = SoldierColors.Body(k).linear * 7f;
                glowMats[k].SetColor(EmissionId, new Color(col.r, col.g, col.b, 1f));
            }

            for (int i = 0; i < f.FxCount; i++)
            {
                var fx = f.Fx[i];
                if (fx.kind == LtCode.FxBlast)
                    Blast(fx.weapon, Coords.ToUnity(fx.pos), eye, now);
                else if (fx.kind == LtCode.FxTurret)
                    nTurretShots++;
            }

            int n = f.GadgetCount;
            int drawn = 0;
            for (int i = 0; i < n; i++)
            {
                var gd = f.Gadgets[i];
                var pos = Coords.ToUnity(gd.pos);
                byte state = (byte)(gd.resting != 0 ? StateResting : 0);
                if (gd.kind == LtCode.GadgetSmoke && gd.age >= LtConst.SmokeFuse)
                    state |= StateSmoke;
                if (gd.kind == LtCode.GadgetMine && gd.resting != 0 && gd.age >= LtConst.MineArm)
                    state |= StateArmed;
                Track(i, gd, pos, state);
                if (gd.kind == LtCode.GadgetSmoke && (state & StateSmoke) != 0)
                    Smoke(gd, pos, dt);
                if (gd.kind == LtCode.GadgetTurret && gd.health < LtCode.TurretHealth * 0.4f && Rand() < dt * 5f)
                    EmitSmoke(pos + Vector3.up * 1.05f, 0.5f + Rand() * 0.4f, 1.6f, new Color(0.18f, 0.17f, 0.16f, 0.8f), new Vector3((Rand() - 0.5f) * 0.3f, 0.9f, (Rand() - 0.5f) * 0.3f));
                if ((pos - eye).sqrMagnitude > 350f * 350f)
                    continue;
                Draw(gd, pos, now);
                drawn++;
            }
            if (testRow && cam != null)
                TestRow(cam, now);
            Drawn = drawn;
            maxKnown = Mathf.Max(maxKnown, n);
            maxSpotted = Mathf.Max(maxSpotted, f.SpottedCount);
            maxDrawn = Mathf.Max(maxDrawn, drawn);
            nCues += CueCount;
            Finish(n);
            Lights(now);
        }

        /// <summary>Debugging (LATTICE_GADGET_TEST set): every model in a row in front of the camera.</summary>
        static readonly bool testRow = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("LATTICE_GADGET_TEST"));

        void TestRow(Camera cam, float now)
        {
            var t = cam.transform;
            var flat = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, flat);
            for (int k = 1; k <= LtCode.GadgetTurret; k++)
            {
                var pos = t.position + flat * 3.5f + right * ((k - 4) * 0.7f) + Vector3.down * 1.2f;
                var g = new LtGadget { id = (uint)k, kind = (byte)k, faction = 1, resting = 1, health = (ushort)LtCode.TurretHealth, normal = new LtVec3 { z = 1f }, age = now, sinceShot = 1e6f };
                Draw(g, pos, now);
            }
        }

        /// <summary>What changed for gadget <paramref name="i"/> since last frame: a cue for each moment that sounds.</summary>
        void Track(int i, in LtGadget gd, Vector3 pos, byte state)
        {
            curIds[i] = gd.id;
            curState[i] = (byte)(state | (gd.faction << 4));
            int k = System.Array.BinarySearch(prevIds, 0, prevCount, gd.id);
            if (k < 0)
            {
                // New to us: thrown (or built) now, or there already when we came.
                if (gd.ours != 0)
                    oursSeen++;
                if (!seenAny)
                    return;
                if (gd.kind == LtCode.GadgetTurret)
                    AddCue(CueKind.Built, gd.kind, pos);
                else if (gd.resting == 0 && gd.age < 0.5f)
                    AddCue(CueKind.Thrown, gd.kind, pos);
                return;
            }
            byte was = prevState[k];
            if ((state & StateResting) != 0 && (was & StateResting) == 0)
                AddCue(gd.kind == LtCode.GadgetDart ? CueKind.Dart : CueKind.Landed, gd.kind, pos);
            if ((state & StateSmoke) != 0 && (was & StateSmoke) == 0)
                AddCue(CueKind.Smoke, gd.kind, pos);
            if ((state & StateArmed) != 0 && (was & StateArmed) == 0)
                AddCue(CueKind.Armed, gd.kind, pos);
            if ((was >> 4) != gd.faction && gd.kind == LtCode.GadgetTurret)
                AddCue(CueKind.Hacked, gd.kind, pos);
            // C4's beep with each blink (its owner's side hears it as well as the enemy).
            if (gd.kind == LtCode.GadgetC4 && gd.resting != 0 && Mathf.Repeat(gd.age, 1f) < Time.unscaledDeltaTime)
                AddCue(CueKind.Beep, gd.kind, pos);
        }

        void Finish(int n)
        {
            (prevIds, curIds) = (curIds, prevIds);
            (prevState, curState) = (curState, prevState);
            prevCount = Mathf.Min(n, Max);
            System.Array.Sort(prevIds, prevState, 0, prevCount);
            seenAny |= NetDriver.Frame.Welcomed;
        }

        static void AddCue(CueKind kind, byte gadget, Vector3 pos)
        {
            if (CueCount < Cues.Length)
                Cues[CueCount++] = new Cue { Kind = kind, Gadget = gadget, Pos = pos };
        }

        /// <summary>The thing itself: lying on the face it landed on (or tumbling in
        /// flight), its lights blinking as its kind does.</summary>
        void Draw(in LtGadget gd, Vector3 pos, float now)
        {
            var bounds = new Bounds(pos + Vector3.up * 0.5f, new Vector3(2f, 2.5f, 2f));
            body.worldBounds = trim.worldBounds = olive.worldBounds = can.worldBounds = danger.worldBounds = white.worldBounds = bounds;
            for (int k = 0; k < 3; k++)
                glow[k].worldBounds = bounds;
            int side = Mathf.Clamp(gd.faction, 0, 2);
            float spin = (gd.id * 2654435761u % 360u);
            if (gd.kind == LtCode.GadgetTurret)
            {
                var baseRot = Quaternion.Euler(0f, spin, 0f);
                Graphics.RenderMesh(body, turretBase, 0, Matrix4x4.TRS(pos, baseRot, Vector3.one));
                Graphics.RenderMesh(trim, turretBase, 1, Matrix4x4.TRS(pos, baseRot, Vector3.one));
                // Its head: where it last fired, for a few seconds; else a slow watch round.
                float yaw, pitch;
                if (gd.sinceShot < 3f)
                    (yaw, pitch) = (gd.yaw, gd.pitch);
                else
                {
                    float rest = gd.sinceShot < 1e5f ? gd.yaw : spin * Mathf.Deg2Rad;
                    yaw = rest + Mathf.Sin((now + gd.id) * 0.35f) * 0.7f;
                    pitch = -0.04f;
                }
                var head = Matrix4x4.TRS(pos + Vector3.up * LtConst.TurretHeight, Coords.View(yaw, pitch), Vector3.one);
                Graphics.RenderMesh(body, turretHead, 0, head);
                Graphics.RenderMesh(trim, turretHead, 1, head);
                // Its eye: its side's, red and flickering when it's nearly done for.
                bool failing = gd.health < LtCode.TurretHealth * 0.3f;
                if (!failing || Mathf.Repeat(now * 7f + gd.id, 1f) < 0.6f)
                    Graphics.RenderMesh(failing ? danger : glow[side], turretHead, 2, head);
                return;
            }
            var mesh = meshes[gd.kind < meshes.Length ? gd.kind : 0];
            if (mesh == null)
                return;
            Quaternion rot;
            if (gd.resting != 0)
            {
                var n = Coords.DirToUnity(gd.normal);
                if (n.sqrMagnitude < 0.01f)
                    n = Vector3.up;
                rot = Quaternion.FromToRotation(Vector3.up, n.normalized) * Quaternion.Euler(0f, spin, 0f);
            }
            else
            {
                // Tumbling end over end along its flight (a dart flies point first).
                var v = Coords.DirToUnity(gd.vel);
                var fwd = v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward;
                rot = gd.kind == LtCode.GadgetDart
                    ? Quaternion.FromToRotation(Vector3.up, -fwd)
                    : Quaternion.LookRotation(fwd) * Quaternion.Euler(gd.age * 720f, spin, gd.age * 260f);
            }
            var m = Matrix4x4.TRS(pos, rot, Vector3.one);
            var bodyParams = gd.kind == LtCode.GadgetFrag || gd.kind == LtCode.GadgetAmmoPack ? olive : gd.kind == LtCode.GadgetSmoke ? can : body;
            Graphics.RenderMesh(bodyParams, mesh, 0, m);
            Graphics.RenderMesh(trim, mesh, 1, m);
            if (Lit(gd, now, out bool red))
                Graphics.RenderMesh(red ? danger : glow[side], mesh, 2, m);
        }

        /// <summary>Whether a gadget's lights are on this frame, and red (a fuse, C4's LED).</summary>
        static bool Lit(in LtGadget gd, float now, out bool red)
        {
            red = false;
            switch (gd.kind)
            {
                case LtCode.GadgetFrag:
                {
                    // Faster as the fuse runs down.
                    red = true;
                    float left = Mathf.Max(0f, LtConst.FragFuse - gd.age);
                    float period = Mathf.Lerp(0.07f, 0.5f, left / LtConst.FragFuse);
                    return Mathf.Repeat(gd.age, period) < period * 0.5f;
                }
                case LtCode.GadgetC4:
                    red = true;
                    return gd.resting != 0 && Mathf.Repeat(gd.age, 1f) < 0.15f;
                case LtCode.GadgetMine:
                    // Arming: quick blinks; armed, a slow pulse.
                    if (gd.age < LtConst.MineArm)
                        return Mathf.Repeat(gd.age, 0.25f) < 0.12f;
                    return Mathf.Repeat(gd.age, 2f) < 0.35f;
                case LtCode.GadgetDart:
                    return gd.resting != 0 && Mathf.Repeat(gd.age, 1.2f) < 0.25f;
                case LtCode.GadgetAmmoPack:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A smoke grenade's cloud: puffs filling its radius while it pours.</summary>
        void Smoke(in LtGadget gd, Vector3 pos, float dt)
        {
            float t = gd.age - LtConst.SmokeFuse;
            if (t > LtConst.SmokeSecs - 3f)
                return;
            // A thick burst as it goes, then a steady pour.
            smokeOwed += dt * (t < 1.2f ? 90f : 26f);
            float r = LtConst.SmokeRadius;
            while (smokeOwed >= 1f)
            {
                smokeOwed -= 1f;
                float spread = Mathf.Clamp01(t / 2.5f) * 0.8f + 0.2f;
                float a = Rand() * Mathf.PI * 2f, d = Mathf.Sqrt(Rand()) * r * 0.75f * spread;
                var at = pos + new Vector3(Mathf.Cos(a) * d, 0.4f + Rand() * 2.6f * spread, Mathf.Sin(a) * d);
                float shade = 0.72f + 0.16f * Rand();
                EmitSmoke(at, 3f + Rand() * 2.5f, 6f + Rand() * 2f, new Color(shade, shade, shade * 0.98f, 0.9f),
                          new Vector3((Rand() - 0.5f) * 0.6f, 0.08f + Rand() * 0.12f, (Rand() - 0.5f) * 0.6f));
            }
        }

        void EmitSmoke(Vector3 at, float size, float life, Color color, Vector3 vel)
        {
            var p = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = at,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = Rand() * 360f,
            };
            smoke.Emit(p, 1);
            nSmoke++;
        }

        /// <summary>A burst of gadget kind <paramref name="kind"/> (a frag, C4, a mine, a
        /// launcher's shell): fireball, smoke, dirt and sparks, light, a shake.</summary>
        void Blast(int kind, Vector3 pos, Vector3 eye, float now)
        {
            nBlasts++;
            float s = kind == LtCode.GadgetC4 ? 1.4f : kind == LtCode.GadgetMine ? 1.1f : kind == LtCode.GadgetShell ? 0.7f : 1f;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int nFire = (int)(26 * s);
            for (int i = 0; i < nFire; i++)
            {
                var dir = Hemi();
                p.position = pos + dir * (0.2f * s) + Vector3.up * 0.3f;
                p.velocity = dir * (3f + 6f * Rand()) * s;
                p.startSize = (0.9f + 1.4f * Rand()) * s;
                p.startLifetime = 0.18f + 0.3f * Rand();
                p.startColor = Color.white;
                p.rotation = Rand() * 360f;
                fire.Emit(p, 1);
            }
            int nSmoke = (int)(22 * s);
            for (int i = 0; i < nSmoke; i++)
            {
                var dir = Hemi();
                float shade = 0.2f + 0.14f * Rand();
                EmitSmoke(pos + dir * (0.6f * s) + Vector3.up * 0.5f, (1.6f + 2f * Rand()) * s, 2.2f + 2.5f * Rand(),
                          new Color(shade, shade * 0.95f, shade * 0.9f, 0.85f), dir * (1f + 2.5f * Rand()) * s + Vector3.up * 0.6f);
            }
            int nDebris = (int)(40 * s);
            for (int i = 0; i < nDebris; i++)
            {
                var dir = Hemi();
                dir.y += 0.4f;
                p.position = pos + Vector3.up * 0.2f;
                p.velocity = dir.normalized * (7f + 13f * Rand()) * s;
                p.startSize = 0.03f + 0.03f * Rand();
                p.startLifetime = 0.5f + 0.8f * Rand();
                p.startColor = Color.white;
                debris.Emit(p, 1);
            }
            int nDirt = (int)(30 * s);
            for (int i = 0; i < nDirt; i++)
            {
                var dir = Hemi();
                dir.y += 0.8f;
                p.position = pos + Vector3.up * 0.1f;
                p.velocity = dir.normalized * (4f + 8f * Rand()) * s;
                p.startSize = 0.12f + 0.2f * Rand();
                p.startLifetime = 0.9f + 0.9f * Rand();
                float sh = 0.18f + 0.1f * Rand();
                p.startColor = new Color(sh * 1.2f, sh, sh * 0.8f, 0.95f);
                p.rotation = Rand() * 360f;
                dirt.Emit(p, 1);
            }
            int k = nextLight++ % lights.Length;
            var l = lights[k];
            l.transform.position = pos + Vector3.up * 1.2f;
            l.range = 14f * s;
            l.enabled = true;
            lightAt[k] = now;
            lightPeak[k] = 26f * s;
            float d = Vector3.Distance(eye, pos);
            float reach = (kind == LtCode.GadgetC4 ? LtConst.C4Reach : kind == LtCode.GadgetMine ? LtConst.MineReach : kind == LtCode.GadgetShell ? LtConst.ShellReach : LtConst.FragReach) * 6f;
            if (d < reach)
                CameraRig.Shake(Mathf.Clamp01(1f - d / reach) * 0.9f * s);
        }

        Vector3 Hemi()
        {
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3(Rand() * 2f - 1f, Rand(), Rand() * 2f - 1f);
                float m = v.sqrMagnitude;
                if (m > 0.04f && m <= 1f)
                    return v / Mathf.Sqrt(m);
            }
            return Vector3.up;
        }

        void Lights(float now)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (!l.enabled)
                    continue;
                float t = now - lightAt[i];
                if (t > 0.35f)
                {
                    l.enabled = false;
                    continue;
                }
                l.intensity = lightPeak[i] * Mathf.Exp(-t / 0.08f);
            }
        }
    }

    /// <summary>
    /// The gadgets' models, built once in code: each a mesh of three parts
    /// (submeshes: body, trim, lights), its base at the origin, up +y (the
    /// face it lies on), in meters.
    /// </summary>
    public static class GadgetMeshes
    {
        sealed class Builder
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<int>[] t = { new List<int>(), new List<int>(), new List<int>() };

            /// <summary>A box of <paramref name="size"/> centered at <paramref name="c"/>, turned by <paramref name="r"/>.</summary>
            public Builder Box(int part, Vector3 c, Vector3 size, Quaternion r)
            {
                var h = size * 0.5f;
                for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var nn = Vector3.zero;
                    nn[axis] = sign;
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    int b = v.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        var p = Vector3.zero;
                        p[axis] = sign * h[axis];
                        p[a1] = ((k == 1 || k == 2) ? 1 : -1) * h[a1];
                        p[a2] = (k >= 2 ? 1 : -1) * h[a2];
                        v.Add(c + r * p);
                        n.Add(r * nn);
                    }
                    if (sign > 0)
                        t[part].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    else
                        t[part].AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                return this;
            }

            /// <summary>A cylinder along its local y, <paramref name="h"/> tall, centered at <paramref name="c"/>.</summary>
            public Builder Cylinder(int part, Vector3 c, float radius, float h, Quaternion r, int sides = 14)
            {
                int ring = v.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(c + r * (d * radius + Vector3.down * h * 0.5f));
                    n.Add(r * d);
                    v.Add(c + r * (d * radius + Vector3.up * h * 0.5f));
                    n.Add(r * d);
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = ring + i * 2;
                    t[part].AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                }
                for (int cap = -1; cap <= 1; cap += 2)
                {
                    int center = v.Count;
                    v.Add(c + r * (Vector3.up * cap * h * 0.5f));
                    n.Add(r * (Vector3.up * cap));
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = i * Mathf.PI * 2f / sides;
                        v.Add(c + r * (new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * cap * h * 0.5f));
                        n.Add(r * (Vector3.up * cap));
                    }
                    for (int i = 0; i < sides; i++)
                        if (cap > 0)
                            t[part].AddRange(new[] { center, center + 2 + i, center + 1 + i });
                        else
                            t[part].AddRange(new[] { center, center + 1 + i, center + 2 + i });
                }
                return this;
            }

            /// <summary>An ellipsoid of <paramref name="radii"/> centered at <paramref name="c"/>.</summary>
            public Builder Sphere(int part, Vector3 c, Vector3 radii, int around = 14, int up = 9)
            {
                int b = v.Count;
                for (int j = 0; j <= up; j++)
                for (int i = 0; i <= around; i++)
                {
                    float a = i * Mathf.PI * 2f / around, e = j * Mathf.PI / up;
                    var d = new Vector3(Mathf.Sin(e) * Mathf.Cos(a), -Mathf.Cos(e), Mathf.Sin(e) * Mathf.Sin(a));
                    v.Add(c + Vector3.Scale(d, radii));
                    n.Add(new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z).normalized);
                }
                for (int j = 0; j < up; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = b + j * (around + 1) + i, c2 = a + around + 1;
                    t[part].AddRange(new[] { a, c2, a + 1, a + 1, c2, c2 + 1 });
                }
                return this;
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.subMeshCount = 3;
                for (int k = 0; k < 3; k++)
                    m.SetTriangles(t[k], k);
                m.RecalculateBounds();
                return m;
            }
        }

        const int Body = 0, Trim = 1, Glow = 2;
        static readonly Quaternion I = Quaternion.identity;
        /// <summary>A cylinder lying along z.</summary>
        static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>A frag: an egg of a body, the spoon down its side, the fuse on top and its light.</summary>
        public static Mesh Frag() => new Builder()
            .Sphere(Body, new Vector3(0f, 0.055f, 0f), new Vector3(0.042f, 0.055f, 0.042f))
            .Cylinder(Trim, new Vector3(0f, 0.115f, 0f), 0.014f, 0.026f, I, 10)
            .Box(Trim, new Vector3(0.036f, 0.075f, 0f), new Vector3(0.008f, 0.07f, 0.018f), Quaternion.Euler(0f, 0f, 10f))
            .Sphere(Glow, new Vector3(0f, 0.131f, 0f), new Vector3(0.008f, 0.008f, 0.008f), 8, 5)
            .Build("Frag");

        /// <summary>A smoke grenade: a can, two bands, a cap.</summary>
        public static Mesh SmokeCan() => new Builder()
            .Cylinder(Body, new Vector3(0f, 0.065f, 0f), 0.033f, 0.13f, I)
            .Cylinder(Trim, new Vector3(0f, 0.03f, 0f), 0.035f, 0.012f, I)
            .Cylinder(Trim, new Vector3(0f, 0.1f, 0f), 0.035f, 0.012f, I)
            .Cylinder(Trim, new Vector3(0f, 0.138f, 0f), 0.016f, 0.018f, I, 10)
            .Build("Smoke grenade");

        /// <summary>C4: a brick with its detonator and LED.</summary>
        public static Mesh C4() => new Builder()
            .Box(Body, new Vector3(0f, 0.025f, 0f), new Vector3(0.2f, 0.05f, 0.11f), I)
            .Box(Trim, new Vector3(0.055f, 0.064f, 0f), new Vector3(0.055f, 0.028f, 0.045f), I)
            .Box(Trim, new Vector3(-0.03f, 0.051f, 0f), new Vector3(0.012f, 0.004f, 0.112f), I)
            .Box(Glow, new Vector3(0.055f, 0.08f, 0.012f), new Vector3(0.012f, 0.008f, 0.012f), I)
            .Build("C4");

        /// <summary>A mine: a low disc, its pressure plate, four lights round the rim.</summary>
        public static Mesh Mine()
        {
            var b = new Builder()
                .Cylinder(Body, new Vector3(0f, 0.022f, 0f), 0.15f, 0.044f, I, 18)
                .Cylinder(Trim, new Vector3(0f, 0.051f, 0f), 0.085f, 0.016f, I, 16);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2f + Mathf.PI / 4f;
                b.Box(Glow, new Vector3(Mathf.Cos(a) * 0.12f, 0.046f, Mathf.Sin(a) * 0.12f), new Vector3(0.018f, 0.006f, 0.018f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }
            return b.Build("Mine");
        }

        /// <summary>An ammo pack: a crate with a handle, latches and a light strip each side.</summary>
        public static Mesh AmmoPack() => new Builder()
            .Box(Body, new Vector3(0f, 0.1f, 0f), new Vector3(0.36f, 0.2f, 0.24f), I)
            .Box(Trim, new Vector3(0f, 0.205f, 0f), new Vector3(0.37f, 0.012f, 0.25f), I)
            .Box(Trim, new Vector3(0f, 0.232f, 0f), new Vector3(0.18f, 0.025f, 0.025f), I)
            .Box(Trim, new Vector3(-0.08f, 0.218f, 0f), new Vector3(0.02f, 0.02f, 0.02f), I)
            .Box(Trim, new Vector3(0.08f, 0.218f, 0f), new Vector3(0.02f, 0.02f, 0.02f), I)
            .Box(Glow, new Vector3(0f, 0.14f, 0.121f), new Vector3(0.28f, 0.014f, 0.004f), I)
            .Box(Glow, new Vector3(0f, 0.14f, -0.121f), new Vector3(0.28f, 0.014f, 0.004f), I)
            .Build("Ammo pack");

        /// <summary>A recon dart: a spike (its point at the base, in what it struck), fins, a light.</summary>
        public static Mesh Dart()
        {
            var b = new Builder()
                .Cylinder(Body, new Vector3(0f, 0.07f, 0f), 0.011f, 0.2f, I, 8)
                .Cylinder(Trim, new Vector3(0f, -0.015f, 0f), 0.006f, 0.05f, I, 6);
            for (int k = 0; k < 3; k++)
                b.Box(Trim, new Vector3(0f, 0.15f, 0f) + Quaternion.Euler(0f, k * 120f, 0f) * new Vector3(0.02f, 0f, 0f), new Vector3(0.03f, 0.05f, 0.003f), Quaternion.Euler(0f, k * 120f, 0f));
            return b.Sphere(Glow, new Vector3(0f, 0.185f, 0f), new Vector3(0.016f, 0.016f, 0.016f), 10, 6).Build("Recon dart");
        }

        /// <summary>A turret's base: three splayed legs, the column and the neck.</summary>
        public static Mesh TurretBase()
        {
            var b = new Builder();
            var top = new Vector3(0f, 0.5f, 0f);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                var foot = new Vector3(Mathf.Cos(a) * 0.48f, 0.02f, Mathf.Sin(a) * 0.48f);
                var d = top - foot;
                b.Box(Body, (top + foot) * 0.5f, new Vector3(0.045f, d.magnitude, 0.05f), Quaternion.FromToRotation(Vector3.up, d.normalized));
                b.Box(Trim, foot + new Vector3(0f, 0.01f, 0f), new Vector3(0.1f, 0.02f, 0.1f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }
            b.Cylinder(Body, new Vector3(0f, 0.4f, 0f), 0.075f, 0.4f, I);
            b.Cylinder(Trim, new Vector3(0f, 0.62f, 0f), 0.11f, 0.05f, I, 18);
            b.Cylinder(Trim, new Vector3(0f, 0.72f, 0f), 0.045f, 0.18f, I, 10);
            return b.Build("Turret base");
        }

        /// <summary>A turret's head, its pivot at the origin (the gun's height), aiming
        /// +z: the housing, twin barrels, the shroud, the sensor eye and side strips.</summary>
        public static Mesh TurretHead() => new Builder()
            .Box(Body, new Vector3(0f, 0f, -0.02f), new Vector3(0.28f, 0.2f, 0.34f), I)
            .Box(Body, new Vector3(0f, -0.06f, -0.2f), new Vector3(0.2f, 0.12f, 0.1f), I)
            .Cylinder(Trim, new Vector3(0.055f, -0.01f, 0.35f), 0.022f, 0.4f, AlongZ, 10)
            .Cylinder(Trim, new Vector3(-0.055f, -0.01f, 0.35f), 0.022f, 0.4f, AlongZ, 10)
            .Cylinder(Trim, new Vector3(0.055f, -0.01f, 0.54f), 0.03f, 0.04f, AlongZ, 10)
            .Cylinder(Trim, new Vector3(-0.055f, -0.01f, 0.54f), 0.03f, 0.04f, AlongZ, 10)
            .Box(Trim, new Vector3(0f, 0.115f, 0.03f), new Vector3(0.2f, 0.03f, 0.22f), I)
            .Box(Glow, new Vector3(0f, 0.045f, 0.152f), new Vector3(0.1f, 0.035f, 0.01f), I)
            .Box(Glow, new Vector3(0.141f, 0.02f, -0.02f), new Vector3(0.004f, 0.025f, 0.24f), I)
            .Box(Glow, new Vector3(-0.141f, 0.02f, -0.02f), new Vector3(0.004f, 0.025f, 0.24f), I)
            .Build("Turret head");
    }
}
