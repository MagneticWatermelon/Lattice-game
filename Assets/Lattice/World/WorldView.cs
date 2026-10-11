using System.Collections;
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
    /// terrain (from background jobs), the cover, the rocks, then the grass. In
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
        public bool Started { get; private set; }
        /// <summary>All of it built.</summary>
        public bool Ready => Terrain != null && Terrain.Done && cover == null && rocks == null && grassTried;
        /// <summary>How much of a frame building may take, past the terrain's slice (s).</summary>
        const double Budget = 0.008;
        LtWorldHandle preview;
        IEnumerator cover, rocks;
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
            cover = CoverBuilder.Build(world, Look, transform, near);
            rocks = RockScatter.Build(world, Look, transform);
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
                Grass = GrassField.Create(transform, Look, Terrain.HeightTexture, Terrain.GrassTexture);
            }
            // The cover, then the rocks, while the frame's budget lasts.
            while (cover != null && Spent(start) < Budget)
                if (!cover.MoveNext())
                    cover = null;
            while (cover == null && rocks != null && Spent(start) < Budget)
                if (!rocks.MoveNext())
                    rocks = null;
        }

        static double Spent(long start) => (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;

        void OnDestroy()
        {
            Terrain?.Dispose();
            preview?.Dispose();
        }
    }
}
