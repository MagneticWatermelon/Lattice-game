using Lattice.Combat;
using Lattice.Diagnostics;
using Lattice.LocalPlayer;
using Lattice.Native;
using Lattice.Net;
using Lattice.Players;
using UnityEngine;

namespace Lattice.Audio
{
    /// <summary>
    /// The fight's sound (GDD §9.4), from the plugin's frame. Every shot by a
    /// shooter we draw is a one-shot placed at its muzzle: the near sound or
    /// the far one by distance, quieter (inverse distance) and darker (the
    /// air takes the top) the farther, and late by the speed of sound. Slugs
    /// passing close crack. Impacts near us thud or ring. Our rifle, hits
    /// (body, head, kill), hurts (from the shooter's side), death and respawn
    /// play in the head; footsteps ours and near soldiers'. Handling: our
    /// reload as its motion passes each sound, the trigger pulled dry, a spent
    /// cell landing, crouching down and up; near soldiers' reloads where they
    /// stand. Distant fights are a bed of far gunfire per direction, as loud as
    /// the shooting there.
    ///
    /// Voices are pooled (made once, stolen by priority when all are busy);
    /// nothing allocates per frame.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class BattleAudio : MonoBehaviour
    {
        public AudioLook Look;
        public Gun Gun;
        public Crowd Crowd;

        const int WorldVoices = 40, HeadVoices = 10, Sectors = 8;

        struct Voice
        {
            public AudioSource Src;
            public AudioLowPassFilter Air;
            public double Until;
            public float Priority;
        }

        Voice[] world, head;
        AudioSource wind;
        AudioSource[] bedSmall, bedLarge;
        readonly float[] heat = new float[Sectors];
        readonly float[] bedLevel = new float[Sectors * 2];

        // Cracks heard lately (a tracer can pass close over two frames).
        readonly Vector3[] crackAt = new Vector3[8];
        readonly float[] crackWhen = new float[8];
        int crackNext;

        // Footsteps: ours, and near soldiers' (per entity id).
        float ownStride, ownAir;
        bool ownGrounded = true;
        readonly float[] stride = new float[65536];
        readonly Vector3[] lastFeet = new Vector3[65536];
        readonly float[] seenAt = new float[65536];

        bool wasAds, wasDead, wasCrouched;
        // Near soldiers seen reloading (per entity id), so each reload sounds once.
        readonly bool[] reloadHeard = new bool[65536];

        // For the report: what played, and what came out.
        int nOwn, nNear, nFar, nCrack, nImpact, nFeedback, nSteps, nStolen, nFarBedded, nHandling, nOthersReload;
        MasterBus bus;
        float busMaxDb;
        // Far shots heard one by one, at most so many a second; the rest are the bed's.
        float farTokens;
        readonly float[] meter = new float[1024];
        float outPeak, rmsSum;
        int meterFrames, clipFrames, loudFrames;
        byte life;
        uint rng = 0xA0D10u;

        float Rand()
        {
            rng = rng * 1664525u + 1013904223u;
            return (rng >> 8) / 16777216f;
        }

        AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[(int)(Rand() * clips.Length) % clips.Length];

        void Start()
        {
            if (Look == null)
            {
                enabled = false;
                return;
            }
            world = new Voice[WorldVoices];
            for (int i = 0; i < WorldVoices; i++)
                world[i] = MakeVoice($"Voice {i}", true);
            head = new Voice[HeadVoices];
            for (int i = 0; i < HeadVoices; i++)
                head[i] = MakeVoice($"Head voice {i}", false);
            wind = Loop("Wind", Look.Wind, false);
            bedSmall = new AudioSource[Sectors];
            bedLarge = new AudioSource[Sectors];
            for (int s = 0; s < Sectors; s++)
            {
                bedSmall[s] = Loop($"Battle {s} small", Look.BattleSmall, true);
                bedLarge[s] = Loop($"Battle {s} large", Look.BattleLarge, true);
            }
            if (Gun != null)
            {
                Gun.Fired += OnOwnShot;
                Gun.ReloadCue += OnReload;
                Gun.DryFire += OnDryFire;
                Gun.CellLanded += OnCellLanded;
            }
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && !listener.TryGetComponent(out bus))
                bus = listener.gameObject.AddComponent<MasterBus>();
            Report.Sections.Add(Summary);
        }

        string Summary()
        {
            float rms = meterFrames > 0 ? rmsSum / meterFrames : 0f;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  audio: played own {0}, near {1}, far {2} (+{13} left to the bed), cracks {3}, impacts {4}, feedback {5}, steps {6}, handling {15} (+{16} others' reloads); voices stolen {7}\n" +
                "  audio out: peak {8:F1} dBFS, mean rms {9:F1} dBFS over {10} frames; frames over -1 dBFS {11}, over -6 dBFS {12}; the bus took off at most {14:F1} dB\n",
                nOwn, nNear, nFar, nCrack, nImpact, nFeedback, nSteps, nStolen,
                20f * Mathf.Log10(Mathf.Max(outPeak, 1e-6f)), 20f * Mathf.Log10(Mathf.Max(rms, 1e-6f)), meterFrames, clipFrames, loudFrames, nFarBedded, busMaxDb,
                nHandling, nOthersReload);
        }

        /// <summary>The mix as it leaves: peak and loudness, for the report.</summary>
        void Meter()
        {
            float pk = 0f, sum = 0f;
            for (int ch = 0; ch < 2; ch++)
            {
                AudioListener.GetOutputData(meter, ch);
                for (int i = 0; i < meter.Length; i++)
                {
                    float x = meter[i];
                    sum += x * x;
                    if (x > pk) pk = x;
                    else if (-x > pk) pk = -x;
                }
            }
            meterFrames++;
            if (bus != null)
                busMaxDb = Mathf.Max(busMaxDb, bus.ReadAndResetReduction());
            rmsSum += Mathf.Sqrt(sum / (meter.Length * 2));
            outPeak = Mathf.Max(outPeak, pk);
            if (pk > 0.891f) clipFrames++;
            if (pk > 0.5f) loudFrames++;
        }

        void OnDestroy()
        {
            if (Gun != null)
            {
                Gun.Fired -= OnOwnShot;
                Gun.ReloadCue -= OnReload;
                Gun.DryFire -= OnDryFire;
                Gun.CellLanded -= OnCellLanded;
            }
            // (The report is written on quitting, before anything is destroyed.)
            Report.Sections.Remove(Summary);
        }

        Voice MakeVoice(string name, bool inWorld)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = inWorld ? 1f : 0f;
            src.dopplerLevel = 0f;
            src.spread = 0f;
            // We set the level ourselves (game-tuned curves): Unity's rolloff stays flat.
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 5000f;
            src.maxDistance = 10000f;
            var lp = inWorld ? go.AddComponent<AudioLowPassFilter>() : null;
            if (lp != null)
                lp.cutoffFrequency = 22000f;
            return new Voice { Src = src, Air = lp, Until = 0, Priority = 0f };
        }

        AudioSource Loop(string name, AudioClip clip, bool inWorld)
        {
            if (clip == null)
                return null;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = inWorld ? 1f : 0f;
            src.dopplerLevel = 0f;
            src.spread = inWorld ? 90f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 5000f;
            src.maxDistance = 10000f;
            src.volume = 0f;
            src.timeSamples = (int)(Rand() * (clip.samples - 1));
            src.Play();
            return src;
        }

        // ---- playing ------------------------------------------------------------

        int Steal(Voice[] pool, float priority)
        {
            double now = AudioSettings.dspTime;
            int best = -1;
            float lowest = priority;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Until <= now)
                    return i;
                if (pool[i].Priority < lowest)
                    (lowest, best) = (pool[i].Priority, i);
            }
            if (best >= 0)
                nStolen++;
            return best;
        }

        /// <summary>A one-shot in the world: <paramref name="volume"/> and the air's corner set by the caller.</summary>
        void PlayAt(AudioClip clip, Vector3 pos, float volume, float cutoff, float delay, float pitch)
        {
            if (clip == null || volume < 0.003f)
                return;
            int k = Steal(world, volume);
            if (k < 0)
                return;
            ref var v = ref world[k];
            v.Src.Stop();
            v.Src.transform.position = pos;
            v.Src.clip = clip;
            v.Src.volume = volume * Look.Master;
            v.Src.pitch = pitch;
            v.Air.cutoffFrequency = cutoff;
            double start = AudioSettings.dspTime + delay;
            v.Src.PlayScheduled(start);
            v.Until = start + clip.length / pitch;
            v.Priority = volume;
        }

        /// <summary>A one-shot in the head (feedback, our own rifle), panned -1 (left) to 1.</summary>
        void PlayHead(AudioClip clip, float volume, float pitch = 1f, float pan = 0f)
        {
            if (clip == null)
                return;
            int k = Steal(head, volume + 1f);
            if (k < 0)
                return;
            ref var v = ref head[k];
            v.Src.Stop();
            v.Src.clip = clip;
            v.Src.volume = volume * Look.Master;
            v.Src.pitch = pitch;
            v.Src.panStereo = pan;
            v.Src.Play();
            v.Until = AudioSettings.dspTime + clip.length / pitch;
            v.Priority = volume + 1f;
        }

        void OnOwnShot()
        {
            nOwn++;
            PlayHead(Pick(Look.ShotOwn), Look.OwnShot, 0.97f + 0.06f * Rand());
        }

        /// <summary>Our reload, as its motion passes each sound: a little left of
        /// middle, where the left hand works.</summary>
        void OnReload(Reload.Cue cue)
        {
            (AudioClip clip, float gain) = cue switch
            {
                Reload.Cue.Release => (Look.CellRelease, 0.8f),
                Reload.Cue.Out => (Look.CellOut, 0.7f),
                Reload.Cue.Pouch => (Look.CellPouch, 0.45f),
                Reload.Cue.In => (Look.CellIn, 0.7f),
                Reload.Cue.Seat => (Look.CellSeat, 0.95f),
                _ => (Look.CellCharge, 0.7f),
            };
            nHandling++;
            PlayHead(clip, Look.Handling * gain, 0.98f + 0.04f * Rand(), -0.15f);
        }

        void OnDryFire()
        {
            nHandling++;
            PlayHead(Look.DryFire, Look.Handling * 0.8f, 0.97f + 0.06f * Rand());
        }

        void OnCellLanded(Vector3 pos, float hard)
        {
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, pos) : 2f;
            nHandling++;
            PlayAt(Pick(Look.CellClink), pos, Look.Handling * (0.3f + 0.7f * hard) * Mathf.Clamp01(1.6f / Mathf.Max(d, 0.5f)), Air(d * 4f), 0f, 0.94f + 0.12f * Rand());
        }

        float Air(float d) => Mathf.Clamp(22000f * Mathf.Exp(-d / Look.AirDistance), 700f, 22000f);

        // ---- each frame ---------------------------------------------------------

        void LateUpdate()
        {
            var cam = Camera.main;
            var f = NetDriver.Frame;
            if (cam == null || world == null)
                return;
            var ear = cam.transform.position;
            float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            farTokens = Mathf.Min(farTokens + dt * Look.FarPerSecond, Look.FarPerSecond);

            for (int i = 0; i < f.FxCount; i++)
            {
                var fx = f.Fx[i];
                if (fx.kind == LtCode.FxShot && fx.tracer != LtCode.TracerOwn)
                    Shot(fx, ear);
                else if (fx.kind == LtCode.FxImpact)
                    Impact(fx, ear);
            }
            Cracks(f, ear, now);
            Events(f, p);
            Vitals(f);
            Steps(f, p, ear, now, dt);
            OthersReloading(f, ear);
            Beds(ear, dt);
            Meter();
        }

        void Shot(in LtFx fx, Vector3 ear)
        {
            Vector3 pos;
            var v = fx.shooter != LtCode.Nobody && Crowd != null ? Crowd.Rich(fx.shooter) : null;
            pos = v != null ? v.Muzzle : Coords.ToUnity(fx.pos);
            var to = pos - ear;
            float d = to.magnitude;
            AddHeat(to, Look.ShotNearDistance / Mathf.Max(d, Look.ShotNearDistance));
            if (d > Look.Audible)
                return;
            bool far = d > Look.FarFrom;
            if (far)
            {
                // A big fight fires hundreds of shots a second: past the near band only a
                // few are heard one by one, the rest are the battle bed's.
                if (d > Look.FarIndividually || farTokens < 1f)
                {
                    nFarBedded++;
                    return;
                }
                farTokens -= 1f;
            }
            float level = Look.Shots * Mathf.Clamp01(Look.ShotNearDistance / Mathf.Max(d, 1f));
            // Distant fights' shots (no shooter we draw) are quieter: the bed carries them.
            if (fx.tracer == LtCode.TracerAmbient)
                level *= 0.6f;
            if (far) nFar++; else nNear++;
            PlayAt(Pick(far ? Look.ShotFar : Look.ShotNear), pos, level, Air(d), d / Look.SoundSpeed, 0.95f + 0.1f * Rand());
        }

        void Impact(in LtFx fx, Vector3 ear)
        {
            var pos = Coords.ToUnity(fx.pos);
            float d = Vector3.Distance(pos, ear);
            if (d > 45f)
                return;
            bool ground = fx.surface == LtCode.SurfaceTerrain;
            float level = Look.Shots * 0.55f * Mathf.Clamp01(2.5f / Mathf.Max(d, 0.5f));
            nImpact++;
            PlayAt(Pick(ground ? Look.ImpactDirt : Look.ImpactHard), pos, level, Air(d * 3f), d / Look.SoundSpeed, 0.92f + 0.16f * Rand());
        }

        /// <summary>Slugs passing within a few meters: a crack where each passes closest.</summary>
        void Cracks(NetFrame f, Vector3 ear, float now)
        {
            for (int i = 0; i < f.TracerCount; i++)
            {
                var t = f.Tracers[i];
                if (t.kind == LtCode.TracerOwn)
                    continue;
                var head = Coords.ToUnity(t.head);
                var tail = Coords.ToUnity(t.tail);
                var seg = head - tail;
                float len2 = seg.sqrMagnitude;
                if (len2 < 1e-4f)
                    continue;
                float u = Vector3.Dot(ear - tail, seg) / len2;
                if (u <= 0.02f || u >= 0.98f)
                    continue;
                var at = tail + seg * u;
                float d = Vector3.Distance(at, ear);
                if (d > 4f)
                    continue;
                bool heard = false;
                for (int k = 0; k < crackAt.Length; k++)
                    if (now - crackWhen[k] < 0.15f && (crackAt[k] - at).sqrMagnitude < 144f)
                        heard = true;
                if (heard)
                    continue;
                crackAt[crackNext] = at;
                crackWhen[crackNext] = now;
                crackNext = (crackNext + 1) % crackAt.Length;
                nCrack++;
                PlayAt(Pick(Look.Crack), at, Look.Shots * Mathf.Clamp01(1.25f - d / 4f), 22000f, 0f, 0.93f + 0.14f * Rand());
            }
        }

        void Events(NetFrame f, Lattice.LocalPlayer.LocalPlayer p)
        {
            for (int i = 0; i < f.EventCount; i++)
            {
                var e = f.Events[i];
                if (e.kind == LtCode.EventHit || e.kind == LtCode.EventHurt)
                    nFeedback++;
                if (e.kind == LtCode.EventHit)
                {
                    bool head = (e.flags & LtCode.EventHead) != 0;
                    PlayHead(head ? Look.HitHead : Look.HitBody, Look.Feedback * (head ? 0.85f : 0.75f), 0.98f + 0.04f * Rand());
                    if ((e.flags & LtCode.EventKilled) != 0)
                        PlayHead(Look.Kill, Look.Feedback * 0.8f);
                }
                else if (e.kind == LtCode.EventHurt)
                {
                    // From the shooter's side: left is a pan left.
                    float rel = e.dir / 256f * Mathf.PI * 2f - (p != null ? p.Yaw : 0f);
                    PlayHead(Pick(Look.Hurt), Look.Feedback * Mathf.Clamp01(0.55f + e.amount / 60f), 0.95f + 0.1f * Rand(), -Mathf.Sin(rel) * 0.6f);
                }
            }
        }

        void Vitals(NetFrame f)
        {
            if (!f.Welcomed)
                return;
            bool dead = f.Own.dead != 0;
            if (dead && !wasDead)
                PlayHead(Look.Death, Look.Feedback * 0.8f);
            else if (!dead && wasDead)
                PlayHead(Look.Respawn, Look.Feedback * 0.5f);
            wasDead = dead;
            if (Gun != null && !dead)
            {
                if (Gun.Ads != wasAds)
                    PlayHead(Gun.Ads ? Look.SightsUp : Look.SightsDown, Look.Feedback * 0.45f, 0.95f + 0.1f * Rand());
                wasAds = Gun.Ads;
            }
            // Getting down or up: gear and cloth, the knee on the ground.
            bool crouched = f.Own.crouched != 0 && !dead;
            if (crouched != wasCrouched && !dead)
            {
                nHandling++;
                PlayHead(crouched ? Look.CrouchDown : Look.CrouchUp, Look.Footsteps * 0.95f, 0.95f + 0.1f * Rand());
            }
            wasCrouched = crouched;
        }

        void Steps(NetFrame f, Lattice.LocalPlayer.LocalPlayer p, Vector3 ear, float now, float dt)
        {
            // Ours: by the distance walked on the ground; a heavier pair on landing.
            if (f.Welcomed && f.Own.dead == 0)
            {
                var v = f.Own.vel;
                float speed = Mathf.Sqrt(v.x * v.x + v.y * v.y);
                bool grounded = f.Own.grounded != 0;
                if (grounded && !ownGrounded && ownAir > 0.3f)
                {
                    PlayHead(Pick(Look.Footstep), Look.Footsteps * 1.1f, 0.9f);
                    PlayHead(Pick(Look.Footstep), Look.Footsteps * 0.8f, 0.85f);
                    ownStride = 0f;
                }
                ownAir = grounded ? 0f : ownAir + dt;
                ownGrounded = grounded;
                if (grounded && speed > 0.8f)
                {
                    ownStride += speed * dt;
                    float every = 0.7f + 0.1f * speed;
                    if (ownStride >= every)
                    {
                        ownStride -= every;
                        nSteps++;
                        PlayHead(Pick(Look.Footstep), Look.Footsteps * Mathf.Clamp(0.35f + speed / 12f, 0.35f, 1f), 0.9f + 0.2f * Rand(), (Rand() - 0.5f) * 0.2f);
                    }
                }
            }
            // Others within 25 m, by how far each is drawn moving.
            for (int i = 0; i < f.EntityCount; i++)
            {
                var e = f.Entities[i];
                var feet = Coords.ToUnity(e.pos);
                float d = Vector3.Distance(feet, ear);
                int id = e.id;
                if (d > 25f || (e.flags & (LtCode.FlagDead | LtCode.FlagAirborne)) != 0 || now - seenAt[id] > 0.3f)
                {
                    (lastFeet[id], seenAt[id], stride[id]) = (feet, now, 0f);
                    continue;
                }
                float moved = new Vector2(feet.x - lastFeet[id].x, feet.z - lastFeet[id].z).magnitude;
                (lastFeet[id], seenAt[id]) = (feet, now);
                if (moved > 1.5f)
                    continue;
                float speed = moved / Mathf.Max(dt, 1e-4f);
                stride[id] += moved;
                float every = 0.7f + 0.1f * speed;
                if (speed > 0.8f && stride[id] >= every)
                {
                    stride[id] -= every;
                    PlayAt(Pick(Look.Footstep), feet, Look.Footsteps * Mathf.Clamp01(2f / Mathf.Max(d, 1f)), Air(d * 6f), 0f, 0.85f + 0.2f * Rand());
                }
            }
        }

        /// <summary>
        /// Near soldiers' reloads, where each stands as it starts: the cell released,
        /// pulled, the fresh one in and seated, on the first-person reload's beats.
        /// Others' rounds aren't known, so it's a tactical reload's.
        /// </summary>
        void OthersReloading(NetFrame f, Vector3 ear)
        {
            float reach = Look.HandlingDistance;
            for (int i = 0; i < f.EntityCount; i++)
            {
                var e = f.Entities[i];
                bool reloading = (e.flags & LtCode.FlagReloading) != 0 && (e.flags & LtCode.FlagDead) == 0;
                if (!reloading)
                {
                    reloadHeard[e.id] = false;
                    continue;
                }
                if (reloadHeard[e.id])
                    continue;
                reloadHeard[e.id] = true;
                var at = Coords.ToUnity(e.pos.x, e.pos.y, e.pos.z + 1.1f);
                float d = Vector3.Distance(at, ear);
                if (d > reach)
                    continue;
                nOthersReload++;
                float level = Look.Handling * 0.75f * Mathf.Clamp01(1.5f / Mathf.Max(d, 0.5f));
                float air = Air(d * 6f), late = d / Look.SoundSpeed;
                PlayAt(Look.CellRelease, at, level * 0.8f, air, late + 0.33f, 1f);
                PlayAt(Look.CellOut, at, level * 0.7f, air, late + 0.43f, 1f);
                PlayAt(Look.CellIn, at, level * 0.7f, air, late + 1.5f, 1f);
                PlayAt(Look.CellSeat, at, level, air, late + 1.7f, 1f);
            }
        }

        // ---- distant fights -----------------------------------------------------

        void AddHeat(Vector3 to, float amount)
        {
            float bearing = Mathf.Atan2(to.x, to.z);
            int s = ((int)Mathf.Round(bearing / (Mathf.PI * 2f) * Sectors) % Sectors + Sectors) % Sectors;
            heat[s] += amount;
        }

        void Beds(Vector3 ear, float dt)
        {
            if (wind != null)
                wind.volume = Look.Ambience * Look.Master;
            float decay = Mathf.Exp(-dt / 1.6f);
            for (int s = 0; s < Sectors; s++)
            {
                heat[s] *= decay;
                // Heat is in near-shot units per 1.6 s: a sector of a handful of distant
                // shots a second is a skirmish; dozens, a battle.
                float h = heat[s];
                float small = Mathf.Clamp01(h * 2.2f) * (1f - Mathf.Clamp01((h - 0.6f) * 0.8f) * 0.5f);
                float large = Mathf.Clamp01((h - 0.35f) * 0.9f);
                bedLevel[s] = Mathf.MoveTowards(bedLevel[s], small, dt * 0.6f);
                bedLevel[s + Sectors] = Mathf.MoveTowards(bedLevel[s + Sectors], large, dt * 0.4f);
                float angle = s / (float)Sectors * Mathf.PI * 2f;
                var at = ear + new Vector3(Mathf.Sin(angle), 0.15f, Mathf.Cos(angle)) * 60f;
                if (bedSmall[s] != null)
                {
                    bedSmall[s].transform.position = at;
                    bedSmall[s].volume = bedLevel[s] * Look.Battle * Look.Master;
                }
                if (bedLarge[s] != null)
                {
                    bedLarge[s].transform.position = at;
                    bedLarge[s].volume = bedLevel[s + Sectors] * Look.Battle * Look.Master;
                }
            }
        }
    }
}
