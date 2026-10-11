using System;
using System.Collections.Generic;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Players
{
    /// <summary>
    /// Everyone else, drawn from lt_render (GDD §14.4): the nearest players as
    /// full soldiers (<see cref="SoldierView"/>, pooled: nothing is made during
    /// a fight), the rest as GPU-instanced soldiers in a pose baked from the
    /// same model (standing, running, fallen), tinted by faction. Our own body
    /// is a full soldier too: seen from outside in the chase and spectator
    /// views, and only as a shadow in first person.
    /// </summary>
    public sealed class Crowd : MonoBehaviour
    {
        public SoldierLook Look;
        /// <summary>Full soldiers at most (GDD: ≤ 96), and how far out.</summary>
        public int RichCount = 64;
        public float RichDistance = 150f;

        /// <summary>Our own player: drawn from the local player's view (set by the game).</summary>
        public Func<(Vector3 feet, float yaw, float pitch, bool airborne, bool dead, bool ads, bool thirdPerson)> Own;

        SoldierView[] pool;
        SoldierView own;
        readonly Dictionary<ushort, SoldierView> rich = new Dictionary<ushort, SoldierView>();
        readonly List<ushort> releasing = new List<ushort>();
        // Per entity id (they're u16): smoothed velocity for everyone not drawn in full.
        readonly Vector3[] lastPos = new Vector3[65536];
        readonly Vector2[] velocity = new Vector2[65536];
        readonly float[] seenAt = new float[65536];
        // Ranking scratch, reused.
        float[] keys = new float[4096];
        int[] order = new int[4096];

        enum Pose { Stand, Run, Dead }
        Mesh[] poses;
        readonly List<Matrix4x4>[,] batches = new List<Matrix4x4>[3, 3];
        Matrix4x4[] chunk = new Matrix4x4[1023];
        RenderParams[] crowdParams;
        RenderParams rifleParams;

        public int RichDrawn => rich.Count;
        public int CrowdDrawn { get; private set; }

        void Start()
        {
            if (Look == null || Look.Model == null)
            {
                enabled = false;
                return;
            }
            pool = new SoldierView[RichCount];
            for (int i = 0; i < pool.Length; i++)
                pool[i] = Make($"Soldier {i}");
            own = Make("Own soldier");
            BakePoses();
            crowdParams = new RenderParams[3];
            for (int f = 0; f < 3; f++)
            {
                crowdParams[f] = new RenderParams(Look.Factions[f])
                {
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 20000f),
                };
            }
            rifleParams = new RenderParams(Look.RifleMaterial)
            {
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                layer = gameObject.layer,
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 20000f),
            };
            for (int f = 0; f < 3; f++)
            for (int p = 0; p < 3; p++)
                batches[f, p] = new List<Matrix4x4>(1024);
        }

        SoldierView Make(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<SoldierView>();
            v.Init(Look);
            return v;
        }

        /// <summary>The crowd's poses: the lightest LOD, posed by the animator and
        /// baked into plain meshes, with the rifle in its hands.</summary>
        void BakePoses()
        {
            poses = new Mesh[3];
            poses[(int)Pose.Stand] = own.Bake(0f, false, Look.Rifle);
            poses[(int)Pose.Run] = own.Bake(4.5f, false, Look.Rifle);
            poses[(int)Pose.Dead] = own.Bake(0f, true, null);
        }

        void OnEnable() => NetDriver.FrameReady += Place;

        void OnDisable() => NetDriver.FrameReady -= Place;

        /// <summary>Each frame, before animation: who's drawn how, and where.</summary>
        void Place()
        {
            if (pool == null)
                return;
            var f = NetDriver.Frame;
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            var ents = f.Entities;
            int n = f.EntityCount;
            var me = f.Own.pos;
            var center = new Vector3(me.x, me.y, me.z);

            // Shots drawn firing this frame kick their soldiers' rifles.
            for (int i = 0; i < f.FxCount; i++)
            {
                var fx = f.Fx[i];
                if (fx.kind == LtCode.FxShot && fx.shooter != LtCode.Nobody && rich.TryGetValue(fx.shooter, out var sv))
                    sv.Fired(now);
            }

            // The nearest (within reach) are drawn in full; those already full keep
            // their place a little farther out, so the band doesn't flicker.
            if (keys.Length < n)
            {
                keys = new float[Mathf.NextPowerOfTwo(n)];
                order = new int[keys.Length];
            }
            for (int i = 0; i < n; i++)
            {
                var e = ents[i];
                float dx = e.pos.x - center.x, dy = e.pos.y - center.y, dz = e.pos.z - center.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (rich.ContainsKey(e.id))
                    d2 *= 0.8f;
                keys[i] = d2;
                order[i] = i;
            }
            Array.Sort(keys, order, 0, n);
            float maxD2 = RichDistance * RichDistance;
            int richWanted = 0;
            for (int k = 0; k < n && richWanted < pool.Length; k++)
            {
                if (keys[k] > maxD2)
                    break;
                richWanted++;
            }

            // Release the full soldiers that fell out of the band.
            releasing.Clear();
            foreach (var kv in rich)
            {
                bool keep = false;
                for (int k = 0; k < richWanted; k++)
                {
                    if (ents[order[k]].id == kv.Key)
                    {
                        keep = true;
                        break;
                    }
                }
                if (!keep)
                    releasing.Add(kv.Key);
            }
            foreach (var id in releasing)
            {
                rich[id].Release();
                rich.Remove(id);
            }

            for (int fac = 0; fac < 3; fac++)
            for (int p = 0; p < 3; p++)
                batches[fac, p].Clear();
            int crowd = 0;
            for (int k = 0; k < n; k++)
            {
                var e = ents[order[k]];
                var feet = new Vector3(e.pos.x, e.pos.y, e.pos.z);
                bool deadE = (e.flags & LtCode.FlagDead) != 0;
                if (k < richWanted)
                {
                    if (!rich.TryGetValue(e.id, out var v))
                    {
                        v = Free();
                        if (v == null)
                            continue;
                        v.Assign(e.id, e.faction);
                        rich[e.id] = v;
                    }
                    v.Place(feet, e.yaw, e.pitch, (e.flags & LtCode.FlagAirborne) != 0, deadE, (e.flags & LtCode.FlagAds) != 0, dt, now);
                    continue;
                }
                // The crowd: velocity for the pose, then an instance.
                int id = e.id;
                if (now - seenAt[id] > 0.5f || (feet - lastPos[id]).sqrMagnitude > LtConst.Teleport * LtConst.Teleport)
                    velocity[id] = Vector2.zero;
                else
                {
                    var v2 = new Vector2(feet.x - lastPos[id].x, feet.y - lastPos[id].y) / Mathf.Max(dt, 1e-4f);
                    velocity[id] = Vector2.Lerp(velocity[id], v2, 1f - Mathf.Exp(-dt / 0.15f));
                }
                lastPos[id] = feet;
                seenAt[id] = now;
                var pose = deadE ? Pose.Dead : velocity[id].sqrMagnitude > 1f ? Pose.Run : Pose.Stand;
                var m = Matrix4x4.TRS(Coords.ToUnity(feet.x, feet.y, feet.z), Coords.Yaw(e.yaw), Vector3.one);
                batches[Mathf.Clamp(e.faction, 0, 2), (int)pose].Add(m);
                crowd++;
            }
            CrowdDrawn = crowd;

            // Our own body.
            if (Own != null && f.Welcomed)
            {
                var o = Own();
                if (!own.Assigned || own.Entity != f.Own.entity)
                    own.Assign(f.Own.entity, f.Own.faction);
                own.SetShadowsOnly(!o.thirdPerson);
                own.Place(o.feet, o.yaw, o.pitch, o.airborne, o.dead, o.ads, dt, now);
            }
        }

        SoldierView Free()
        {
            foreach (var v in pool)
                if (!v.Assigned)
                    return v;
            return null;
        }

        void LateUpdate()
        {
            if (pool == null)
                return;
            // Levels of detail by size on screen (zooming down the sights brings detail in).
            var cam = Camera.main;
            if (cam != null)
            {
                var eye = cam.transform.position;
                float scale = QualitySettings.lodBias / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                foreach (var kv in rich)
                    kv.Value.Lod(eye, scale);
                if (own.Assigned)
                    own.Lod(eye, scale);
            }
            foreach (var kv in rich)
                kv.Value.Rig();
            if (own.Assigned)
                own.Rig();
            for (int fac = 0; fac < 3; fac++)
            for (int p = 0; p < 3; p++)
            {
                var list = batches[fac, p];
                for (int start = 0; start < list.Count; start += chunk.Length)
                {
                    int count = Mathf.Min(chunk.Length, list.Count - start);
                    list.CopyTo(start, chunk, 0, count);
                    Graphics.RenderMeshInstanced(crowdParams[fac], poses[p], 0, chunk, count);
                    if (poses[p].subMeshCount > 1)
                        Graphics.RenderMeshInstanced(rifleParams, poses[p], 1, chunk, count);
                }
            }
        }

        /// <summary>The full soldier drawing entity <paramref name="id"/>, if any.</summary>
        public SoldierView Rich(ushort id) => rich.TryGetValue(id, out var v) ? v : null;
    }
}
