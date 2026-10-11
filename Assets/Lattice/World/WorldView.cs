using Lattice.Native;
using Lattice.Net;
using UnityEngine;

namespace Lattice.World
{
    /// <summary>
    /// Builds the drawn world once the server's Welcome names it: the terrain
    /// (over a few frames, from background jobs) and the cover. In the editor,
    /// it can also show a world from a seed without a server
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
        public bool Ready => Terrain != null && Terrain.Done;
        LtWorldHandle preview;
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
            Terrain = new TerrainBuilder(world, Look, transform);
            CoverBuilder.Build(world, Look, transform);
            RockScatter.Build(world, Look, transform);
        }

        void Update()
        {
            if (Terrain == null)
                return;
            Terrain.Step();
            if (Terrain.Done && !grassTried)
            {
                grassTried = true;
                Grass = GrassField.Create(transform, Look, Terrain.HeightTexture, Terrain.GrassTexture);
            }
        }

        void OnDestroy()
        {
            Terrain?.Dispose();
            preview?.Dispose();
        }
    }
}
