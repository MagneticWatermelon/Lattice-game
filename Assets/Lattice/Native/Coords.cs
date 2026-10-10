using UnityEngine;

namespace Lattice.Native
{
    /// <summary>
    /// Game space and Unity (GDD §12.5). The game is right-handed with z up:
    /// x east, y north, meters, the map [0, 8192] on x and y. Unity is
    /// left-handed with y up; the map is centered on Unity's origin, for float
    /// precision at its edges.
    ///
    /// | | game | Unity |
    /// |---|---|---|
    /// | position | (x, y, z) | (x − 4096, z, y − 4096) |
    /// | yaw θ (counterclockwise from east) | θ | Euler(0, 90° − θ, 0) |
    /// | view (θ, pitch φ up) | (θ, φ) | Euler(−φ, 90° − θ, 0) |
    ///
    /// The view's canonical form is the game's (θ, φ): Unity rotations are
    /// derived from it, never the other way round. Moving the mouse right
    /// decreases θ; moving it up increases φ.
    /// </summary>
    public static class Coords
    {
        public const float Half = LtConst.WorldSize / 2f;

        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x - Half, z, y - Half);

        public static Vector3 ToUnity(in LtVec3 p) => new Vector3(p.x - Half, p.z, p.y - Half);

        /// <summary>A direction (no offset).</summary>
        public static Vector3 DirToUnity(in LtVec3 d) => new Vector3(d.x, d.z, d.y);

        public static LtVec3 ToGame(Vector3 u) => new LtVec3 { x = u.x + Half, y = u.z + Half, z = u.y };

        public static LtVec3 DirToGame(Vector3 u) => new LtVec3 { x = u.x, y = u.z, z = u.y };

        /// <summary>Facing game yaw θ (radians).</summary>
        public static Quaternion Yaw(float yaw) => Quaternion.Euler(0f, 90f - yaw * Mathf.Rad2Deg, 0f);

        /// <summary>Looking along game yaw θ, pitched φ up (radians).</summary>
        public static Quaternion View(float yaw, float pitch) => Quaternion.Euler(-pitch * Mathf.Rad2Deg, 90f - yaw * Mathf.Rad2Deg, 0f);

        /// <summary>The unit aim of game (yaw, pitch), in game space.</summary>
        public static LtVec3 Aim(float yaw, float pitch)
        {
            float cp = Mathf.Cos(pitch);
            return new LtVec3 { x = cp * Mathf.Cos(yaw), y = cp * Mathf.Sin(yaw), z = Mathf.Sin(pitch) };
        }

        /// <summary>Game yaw of a Unity direction (its horizontal part).</summary>
        public static float YawOf(Vector3 u) => Mathf.Atan2(u.z, u.x);

        /// <summary>Game (yaw, pitch) of a Unity direction.</summary>
        public static void AnglesOf(Vector3 u, out float yaw, out float pitch)
        {
            yaw = Mathf.Atan2(u.z, u.x);
            pitch = Mathf.Atan2(u.y, Mathf.Sqrt(u.x * u.x + u.z * u.z));
        }

        /// <summary>An angle wrapped to [0, 2π).</summary>
        public static float Wrap(float a)
        {
            const float Tau = Mathf.PI * 2f;
            a %= Tau;
            return a < 0f ? a + Tau : a;
        }
    }
}
