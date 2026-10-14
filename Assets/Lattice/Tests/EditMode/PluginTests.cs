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
            // (Links between sites: a tunnel under a lane links what the lane does, K3.)
            var links = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var l in lanes)
            {
                Assert.Less((int)l.a, sites.Length);
                Assert.Less((int)l.b, sites.Length);
                Assert.GreaterOrEqual(l.count, 2u);
                Assert.LessOrEqual(l.first + l.count, (uint)pts.Length);
                if (l.a != l.b)
                    links.Add((System.Math.Min(l.a, l.b), System.Math.Max(l.a, l.b)));
            }
            Assert.That(links.Count, Is.InRange(100, 125));
            var volumes = w.Volumes();
            // Columns in a turned frame turned with it (API v8): inside their bounds.
            int turned = 0;
            foreach (var v in volumes)
            {
                Assert.AreEqual(1f, v.axisX * v.axisX + v.axisY * v.axisY, 1e-4f);
                Assert.AreEqual((v.min.x + v.max.x) / 2f, v.centerX, 1e-3f);
                float ex = System.Math.Abs(v.axisX) * v.halfAlong + System.Math.Abs(v.axisY) * v.halfAcross;
                Assert.AreEqual((v.max.x - v.min.x) / 2f, ex, 1e-2f);
                if (v.axisX != 1f)
                    turned++;
            }
            Assert.Greater(turned, 20, "the diagonal rifts' and the Heartwell's columns turned");
            foreach (var p in pts)
            {
                Assert.IsTrue(p.x >= 0f && p.x <= info.size && p.y >= 0f && p.y <= info.size);
                // On the ground (within a meter: waypoints are approximate) or above
                // it (a deck, a stair), or under it on a tunnel's floor, or carried
                // in a shaft (a lift's top, between two chambers stacked in the rock).
                bool carried = false;
                foreach (var v in volumes)
                    carried |= p.x >= v.min.x - 0.5f && p.x <= v.max.x + 0.5f && p.y >= v.min.y - 0.5f && p.y <= v.max.y + 0.5f && p.z >= v.min.z && p.z <= v.max.z + 1f;
                if (p.z < w.Height(p.x, p.y) - 1f && !carried)
                    Assert.AreEqual(p.z, w.Ground(p.x, p.y, p.z + 0.5f, 1f), 0.6f, $"a tunnel's floor at ({p.x}, {p.y})");
            }
            var points = w.Points();
            Assert.Greater(points.Length, 0);
            foreach (var p in points)
                Assert.Less((int)p.site, sites.Length);
        }

        [Test]
        public void TheCollisionKitsWorld()
        {
            Assert.IsTrue(LtLibrary.EnsureLoaded(), LtLibrary.Error);
            using var w = LtWorldHandle.FromSeed((ulong)LtCode.WorldKit);
            var info = w.Info;
            // A lift, a drop shaft and a spring; two trenches of holes.
            var vols = w.Volumes();
            Assert.AreEqual(3, vols.Length);
            Assert.AreEqual(LtCode.ForceLift, (int)vols[0].force);
            Assert.Less(vols[1].vel.z, 0f);
            Assert.AreEqual(24u, info.holes);
            var holes = new byte[4];
            fixed (byte* h = holes)
                w.Holes(1100 / 4, 1000 / 4, 2, 2, h);
            CollectionAssert.AreEqual(new byte[] { 1, 1, 0, 0 }, holes, "the trench's two rows: y 1000-1004 a hole, 1004-1008 not");
            // A turned box, a wedge, a shield.
            var boxes = w.Boxes();
            Assert.AreNotEqual(1f, boxes[0].axisX);
            Assert.AreEqual(1f, boxes[0].axisX * boxes[0].axisX + boxes[0].axisY * boxes[0].axisY, 1e-5f);
            bool wedge = false, shield = false;
            foreach (var b in boxes)
            {
                wedge |= b.low != b.max.z;
                shield |= b.kind == LtCode.CoverShield && b.pass != LtCode.Solid;
            }
            Assert.IsTrue(wedge && shield);
        }
    }
}
