using Lattice.Native;
using NUnit.Framework;
using UnityEngine;

namespace Lattice.Tests
{
    /// <summary>Mirrors lattice-net's client/src/coords.rs (and GDD §12.5).</summary>
    public class CoordsTests
    {
        static void Near(Vector3 want, Vector3 got, float tol = 1e-5f, string what = "")
        {
            Assert.Less(Vector3.Distance(want, got), tol, $"{what}: want {want:F5}, got {got:F5}");
        }

        [Test]
        public void FacingMatchesTheGame()
        {
            // Yaw 0 faces east (+x): Unity +x. Yaw 90° faces north (+y): Unity +z.
            Near(Vector3.right, Coords.Yaw(0f) * Vector3.forward, what: "yaw 0");
            Near(Vector3.forward, Coords.Yaw(Mathf.PI / 2f) * Vector3.forward, what: "yaw 90");
            Near(Vector3.left, Coords.Yaw(Mathf.PI) * Vector3.forward, what: "yaw 180");
            Assert.Greater((Coords.View(0f, 0.5f) * Vector3.forward).y, 0.4f, "pitch up looks up");
            Assert.Less((Coords.View(0f, -0.5f) * Vector3.forward).y, -0.4f, "pitch down looks down");
        }

        [Test]
        public void PositionsCenterTheMapOnTheOrigin()
        {
            Assert.AreEqual(new Vector3(1f - 4096f, 3f, 2f - 4096f), Coords.ToUnity(1f, 2f, 3f));
            Assert.AreEqual(Vector3.zero, Coords.ToUnity(4096f, 4096f, 0f));
            var g = new LtVec3 { x = 1234.5f, y = 6789.25f, z = 101.75f };
            var back = Coords.ToGame(Coords.ToUnity(g));
            Assert.AreEqual(g.x, back.x, 1e-3f);
            Assert.AreEqual(g.y, back.y, 1e-3f);
            Assert.AreEqual(g.z, back.z, 1e-3f);
        }

        [Test]
        public void TheViewAndTheAimAgree()
        {
            // Any (yaw, pitch): the view's forward is the game aim, converted.
            for (float yaw = -3f; yaw < 7f; yaw += 0.7f)
            for (float pitch = -1.4f; pitch < 1.5f; pitch += 0.35f)
            {
                var fwd = Coords.View(yaw, pitch) * Vector3.forward;
                Near(Coords.DirToUnity(Coords.Aim(yaw, pitch)), fwd, 1e-4f, $"yaw {yaw} pitch {pitch}");
                Coords.AnglesOf(fwd, out var y2, out var p2);
                Assert.AreEqual(Coords.Wrap(yaw), Coords.Wrap(y2), 1e-3f);
                Assert.AreEqual(pitch, p2, 1e-3f);
            }
        }

        [Test]
        public void TheViewHasNoRoll()
        {
            // Unity's up stays in the vertical plane through the aim.
            var q = Coords.View(1.1f, 0.6f);
            var right = q * Vector3.right;
            Assert.AreEqual(0f, right.y, 1e-5f, "right is level");
        }
    }
}
