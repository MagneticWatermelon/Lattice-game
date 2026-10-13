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

        [Test]
        public void KilnWithItsLanes()
        {
            Assert.IsTrue(LtLibrary.EnsureLoaded(), LtLibrary.Error);
            using var w = LtWorldHandle.FromSeed((ulong)LtCode.WorldKiln);
            var info = w.Info;
            Assert.AreEqual(LtCode.ContinentKiln, (int)info.continent);
            // docs/kiln.md: 67 sites, three Landings (one a sector) round one Hollow.
            var sites = w.Sites();
            Assert.AreEqual(67, sites.Length);
            int landings = 0, hollows = 0;
            foreach (var s in sites)
            {
                landings += s.kind == LtCode.SiteLanding ? 1 : 0;
                hollows += s.kind == LtCode.SiteHollow ? 1 : 0;
            }
            Assert.AreEqual(3, landings);
            Assert.AreEqual(1, hollows);
            // About 110 lanes, each joining sites through points of the world.
            var lanes = w.Lanes();
            var pts = w.LanePoints();
            Assert.AreEqual(info.lanes, (uint)lanes.Length);
            Assert.AreEqual(info.lanePoints, (uint)pts.Length);
            int between = 0;
            foreach (var l in lanes)
            {
                Assert.Less((int)l.a, sites.Length);
                Assert.Less((int)l.b, sites.Length);
                Assert.GreaterOrEqual(l.count, 2u);
                Assert.LessOrEqual(l.first + l.count, (uint)pts.Length);
                between += l.a != l.b ? 1 : 0;
            }
            Assert.That(between, Is.InRange(100, 125));
            foreach (var p in pts)
            {
                Assert.IsTrue(p.x >= 0f && p.x <= info.size && p.y >= 0f && p.y <= info.size);
                // On the ground or above it (a deck, a stair), never under it.
                Assert.GreaterOrEqual(p.z, w.Height(p.x, p.y) - 0.5f);
            }
            var points = w.Points();
            Assert.Greater(points.Length, 0);
            foreach (var p in points)
                Assert.Less((int)p.site, sites.Length);
        }
    }
}
