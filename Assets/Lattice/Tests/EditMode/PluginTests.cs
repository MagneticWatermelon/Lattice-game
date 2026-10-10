using Lattice.Native;
using NUnit.Framework;

namespace Lattice.Tests
{
    /// <summary>The plugin loads, matches its bindings, and its world is the game's.</summary>
    public unsafe class PluginTests
    {
        [Test]
        public void ThePluginLoadsAndMatchesItsBindings()
        {
            Assert.IsTrue(LtLibrary.EnsureLoaded(), LtLibrary.Error);
            Assert.AreEqual(LtConst.Version, LtLibrary.Api.lt_version());
            Assert.AreEqual(LtConst.LayoutHash, LtLibrary.Api.lt_layout_hash());
        }

        [Test]
        public void GeneratedConstantsAreTheGames()
        {
            Assert.AreEqual(6f, LtConst.RunSpeed);
            Assert.AreEqual(30, LtConst.TickHz);
            Assert.AreEqual(8192f, LtConst.WorldSize);
            Assert.AreEqual(2049, LtConst.TerrainN);
            Assert.AreEqual(1.6f, LtConst.EyeHeight);
            Assert.AreEqual(255, LtCode.SurfaceTerrain);
        }

        [Test]
        public void TheWorldFromItsSeed()
        {
            Assert.IsTrue(LtLibrary.EnsureLoaded(), LtLibrary.Error);
            using var w = LtWorldHandle.FromSeed(1);
            var info = w.Info;
            Assert.AreEqual(1UL, info.seed);
            Assert.AreEqual(LtConst.TerrainN, (int)info.samples);
            Assert.AreEqual(LtConst.TerrainRes, info.res);
            // Harrow v0 (GDD §3.1): 16 bases and 35 outposts.
            var sites = w.Sites();
            Assert.AreEqual(51, sites.Length);
            int bases = 0;
            foreach (var s in sites)
                bases += s.@base;
            Assert.AreEqual(16, bases);
            // Heights at samples are the terrain there.
            var h = new float[4];
            w.Heights(500, 700, 2, 2, h);
            Assert.AreEqual(w.Height(500 * 4f, 700 * 4f), h[0], 1e-4f);
            Assert.AreEqual(w.Height(501 * 4f, 701 * 4f), h[3], 1e-4f);
            // Straight down from the sky: the ground, facing up.
            var x = 3000f;
            var y = 5000f;
            var g = w.Height(x, y);
            Assert.IsTrue(w.Raycast(new LtVec3 { x = x, y = y, z = g + 50f }, new LtVec3 { x = x, y = y, z = g - 50f }, out var hit));
            Assert.AreEqual(g, hit.pos.z, 0.05f);
            Assert.Greater(hit.normal.z, 0.5f);
            Assert.AreEqual(LtCode.SurfaceTerrain, hit.surface);
            // Cover: plenty of it, each box with its bottom under its top.
            var boxes = w.Boxes();
            Assert.Greater(boxes.Length, 10000);
            foreach (var b in boxes)
                Assert.Less(b.min.z, b.max.z);
        }
    }
}
