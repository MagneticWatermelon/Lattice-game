using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Combat
{
    /// <summary>
    /// Where tracers land: a puff of the ground they hit (dirt from the
    /// terrain, dust and chips from cover), with sparks off hard surfaces.
    /// Two particle systems made in code, emitted into without allocating.
    /// </summary>
    public sealed class Impacts
    {
        readonly ParticleSystem dust, sparks;
        uint rng = 0x1A3Cu;

        static readonly Color Dirt = new Color(0.36f, 0.29f, 0.2f, 0.85f);
        static readonly Color Concrete = new Color(0.62f, 0.61f, 0.58f, 0.8f);

        float Rand()
        {
            rng = rng * 1664525u + 1013904223u;
            return (rng >> 8) / 16777216f;
        }

        public Impacts(Transform parent, Material dustMat, Material sparkMat)
        {
            dust = Make(parent, "Impact dust", dustMat, 1024);
            var m = dust.main;
            m.gravityModifier = 0.08f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
            var col = dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.6f));
            var vel = dust.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.dampen = 0.12f;
            vel.limit = 0.5f;

            sparks = Make(parent, "Impact sparks", sparkMat, 512);
            var sm = sparks.main;
            sm.gravityModifier = 1f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            var sr = sparks.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.03f;
            sr.lengthScale = 1f;
        }

        static ParticleSystem Make(Transform parent, string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = false;
            m.playOnAwake = false;
            m.maxParticles = max;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var e = ps.emission;
            e.enabled = false;
            var s = ps.shape;
            s.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        /// <summary>A tracer's impact: a puff of what it hit, sparks off cover.</summary>
        public void Hit(in LtFx fx)
        {
            var pos = Coords.ToUnity(fx.pos);
            var n = Coords.DirToUnity(fx.normal).normalized;
            var d = Coords.DirToUnity(fx.dir).normalized;
            bool ground = fx.surface == LtCode.SurfaceTerrain;
            var tint = ground ? Dirt : Concrete;
            // Out of the surface, kicked back along the shot's reflection.
            var refl = Vector3.Reflect(d, n);
            int count = ground ? 6 : 5;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                var jitter = new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f);
                var v = (n * (0.9f + 1.6f * Rand()) + refl * (0.6f * Rand()) + jitter * 0.9f) * (ground ? 1.8f : 1.3f);
                p.position = pos + n * 0.04f;
                p.velocity = v;
                p.startSize = (ground ? 0.22f : 0.16f) * (0.7f + 0.8f * Rand());
                p.startLifetime = 0.55f + 0.8f * Rand();
                float shade = 0.82f + 0.3f * Rand();
                p.startColor = new Color(tint.r * shade, tint.g * shade, tint.b * shade, tint.a);
                p.rotation = Rand() * 360f;
                dust.Emit(p, 1);
            }
            if (!ground)
            {
                int ns = 2 + (int)(Rand() * 4f);
                for (int i = 0; i < ns; i++)
                {
                    var jitter = new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f);
                    p.position = pos + n * 0.02f;
                    p.velocity = (refl * 4f + n * 2f + jitter * 5f) * (0.8f + 0.8f * Rand());
                    p.startSize = 0.025f;
                    p.startLifetime = 0.12f + 0.2f * Rand();
                    p.startColor = Color.white;
                    sparks.Emit(p, 1);
                }
            }
        }
    }
}
