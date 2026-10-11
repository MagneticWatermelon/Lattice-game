using Lattice.Native;
using Lattice.Net;
using Lattice.Players;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Combat
{
    /// <summary>
    /// What shots look like (GDD §15), from the plugin's frame: every tracer
    /// it flies drawn in one call (a buffer filled straight from lt_tracers),
    /// a muzzle flash wherever someone is drawn firing (at a full soldier's
    /// rifle, or where the plugin puts a crowd shooter's muzzle), and a puff
    /// where a tracer hits the ground or cover. Nothing allocates per frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class Effects : MonoBehaviour
    {
        public CombatLook Look;
        public Crowd Crowd;

        GraphicsBuffer tracerBuffer;
        RenderParams tracerParams;
        RenderParams flashParams;
        static readonly int TracersId = Shader.PropertyToID("_Tracers");

        const int MaxFlashes = 256;
        const float FlashLife = 0.05f;
        readonly Matrix4x4[] flashes = new Matrix4x4[MaxFlashes];
        readonly float[] flashUntil = new float[MaxFlashes];
        readonly Matrix4x4[] drawn = new Matrix4x4[MaxFlashes];
        int flashNext;
        uint rng = 0xF1A5u;
        Impacts impacts;

        public int TracersDrawn { get; private set; }

        float Rand()
        {
            rng = rng * 1664525u + 1013904223u;
            return (rng >> 8) / 16777216f;
        }

        void Start()
        {
            tracerBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LtConst.MaxTracers, 32);
            var mat = new Material(Look.Tracer);
            mat.SetBuffer(TracersId, tracerBuffer);
            tracerParams = new RenderParams(mat)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 20000f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            flashParams = new RenderParams(Look.Flash)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 20000f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            impacts = new Impacts(transform, Look.Dust, Look.Sparks);
        }

        void OnDestroy()
        {
            tracerBuffer?.Release();
            tracerBuffer = null;
        }

        void LateUpdate()
        {
            var f = NetDriver.Frame;
            float now = Time.unscaledTime;
            if (tracerBuffer == null)
                return;

            for (int i = 0; i < f.FxCount; i++)
            {
                var fx = f.Fx[i];
                if (fx.kind == LtCode.FxShot)
                    Flash(fx, now);
                else if (fx.kind == LtCode.FxImpact)
                    impacts.Hit(fx);
            }

            int n = f.TracerCount;
            TracersDrawn = n;
            if (n > 0)
            {
                tracerBuffer.SetData(f.Tracers, 0, 0, n);
                Graphics.RenderPrimitives(tracerParams, MeshTopology.Triangles, n * 6);
            }

            int live = 0;
            for (int i = 0; i < MaxFlashes; i++)
                if (flashUntil[i] > now)
                    drawn[live++] = flashes[i];
            if (live > 0)
                Graphics.RenderMeshInstanced(flashParams, FlashMesh.Get(), 0, drawn, live);
        }

        /// <summary>A shooter drawn firing: a flash at its muzzle, along its aim.</summary>
        void Flash(in LtFx fx, float now)
        {
            if (fx.tracer == LtCode.TracerOwn)
                return; // ours is the first-person rifle's
            Vector3 pos;
            Vector3 dir = Coords.DirToUnity(fx.dir);
            var v = fx.shooter != LtCode.Nobody && Crowd != null ? Crowd.Rich(fx.shooter) : null;
            if (v != null)
            {
                pos = v.Muzzle;
                dir = v.MuzzleForward;
            }
            else
                pos = Coords.ToUnity(fx.pos);
            if (dir.sqrMagnitude < 1e-6f)
                return;
            // Far flashes are scaled up a little, so a distant fight still twinkles.
            var cam = Camera.main;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, pos) : 50f;
            float size = (0.75f + 0.5f * Rand()) * Mathf.Clamp(dist / 60f, 1f, 6f);
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Rand() * 360f);
            int k = flashNext++ % MaxFlashes;
            flashes[k] = Matrix4x4.TRS(pos, rot, new Vector3(size, size, size * (1f + Rand())));
            flashUntil[k] = now + FlashLife;
        }
    }
}
