using System.Collections.Generic;
using System.Diagnostics;
using Lattice.Native;
using Lattice.Net;
using UnityEngine;

namespace Lattice.World
{
    /// <summary>
    /// Builds the drawn world once the server's Welcome names it, a slice per
    /// frame (the game goes on meanwhile: a frame held for long makes the server
    /// stand in for our inputs, and a moving player's are then corrected): the
    /// terrain (from background jobs), the cover and the rocks (drawn by a
    /// <see cref="StaticField"/> once both are in), then the grass. In
    /// the editor, it can also show a world from a seed without a server
    /// (<see cref="PreviewSeed"/>), for looking at it.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public WorldLook Look;
        /// <summary>Builds this world at start without waiting for a server (0: wait).</summary>
        public ulong PreviewSeed;

        public TerrainBuilder Terrain { get; private set; }
        public GrassField Grass { get; private set; }
        /// <summary>Kiln: the desert has no grass and no meadow rocks; its lanes are
        /// lit (<see cref="LatticeVeins"/>).</summary>
        public bool Kiln { get; private set; }
        public GameObject Veins { get; private set; }
        /// <summary>The cover and the rocks.</summary>
        public StaticField Statics { get; private set; }
        /// <summary>Measuring (--off cover, --off rocks): leave them out.</summary>
        public static bool NoCover, NoRocks;
        bool staticsDone;
        /// <summary>Frames drawn since everything was made: the first ones upload the
        /// terrain's and grass's textures and build the GPU's pipelines (a frame held
        /// ~100 ms), so the world isn't ready until a few are behind it, under the join
        /// card.</summary>
        int builtFrames;
        const int WarmFrames = 4;
        bool Built => Terrain != null && Terrain.Done && staticsDone && grassTried;
        public bool Started { get; private set; }
        /// <summary>All of it built.</summary>
        public bool Ready => Built && builtFrames >= WarmFrames;
        /// <summary>How much of a frame building may take, past the terrain's slice (s).</summary>
        const double Budget = 0.008;
        LtWorldHandle preview;
        IEnumerator<float> cover, rocks;
        float coverDone, rocksDone;

        /// <summary>How much of the world is built, 0 to 1 (the cover is most of the work).</summary>
        public float Progress => Terrain == null ? 0f
            : Ready ? 1f
            : 0.25f * Terrain.Progress + 0.6f * coverDone + 0.15f * rocksDone;
        bool grassTried;

        void OnEnable()
        {
            NetDriver.Welcomed += Build;
        }

        void OnDisable()
        {
            NetDriver.Welcomed -= Build;
        }

        void Start()
        {
            if (PreviewSeed != 0 && !Started && LtLibrary.EnsureLoaded())
            {
                preview = LtWorldHandle.FromSeed(PreviewSeed);
                Build(preview);
            }
        }

        public void Build(LtWorldHandle world)
        {
            if (Started || Look == null)
                return;
            Started = true;
            // Nearest first: what's around the camera appears before the far side.
            var cam = Camera.main;
            var near = cam != null ? cam.transform.position : Vector3.zero;
            Terrain = new TerrainBuilder(world, Look, transform, near);
            Statics = new StaticField();
            Kiln = world.Info.continent == LtCode.ContinentKiln;
            Veins = LatticeVeins.Build(world, Look, transform);
            if (!NoCover)
                cover = CoverBuilder.Build(world, Look, Statics, near);
            if (!NoRocks && !Kiln)
                rocks = RockScatter.Build(world, Look, Statics);
        }

        void Update()
        {
            if (Terrain == null)
                return;
            long start = Stopwatch.GetTimestamp();
            Terrain.Step();
            if (Terrain.Done && !grassTried)
            {
                grassTried = true;
                if (!Kiln)
                    Grass = GrassField.Create(transform, Look, Terrain.HeightTexture, Terrain.GrassTexture);
            }
            // The cover, then the rocks, while the frame's budget lasts.
            while (cover != null && Spent(start) < Budget)
            {
                if (cover.MoveNext())
                    coverDone = cover.Current;
                else
                    (cover, coverDone) = (null, 1f);
            }
            while (cover == null && rocks != null && Spent(start) < Budget)
            {
                if (rocks.MoveNext())
                    rocksDone = rocks.Current;
                else
                    (rocks, rocksDone) = (null, 1f);
            }
            if (!staticsDone && Statics != null && cover == null && rocks == null)
            {
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                Statics.Finish(urp != null ? urp.shadowDistance : 260f);
                (staticsDone, coverDone, rocksDone) = (true, 1f, 1f);
            }
        }

        void LateUpdate()
        {
            if (staticsDone)
                Statics.Update(Camera.main);
            if (Built && builtFrames < WarmFrames)
                builtFrames++;
        }

        static double Spent(long start) => (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;

        void OnDestroy()
        {
            Statics?.Dispose();
            Terrain?.Dispose();
            preview?.Dispose();
        }
    }
}
