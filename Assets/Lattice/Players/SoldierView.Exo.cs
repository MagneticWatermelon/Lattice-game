using System.Collections.Generic;
using Lattice.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Players
{
    /// <summary>
    /// The Exo's armor (U6): plates on the soldier's own bones (torso, power
    /// pack, helmet, shoulder pads, forearm housings, thigh plates, shin
    /// guards) and its arm weapons (a gun barrel on each forearm, or a
    /// launcher tube), placed each frame after the rig. No Exo model exists:
    /// the parts are boxes and tubes, dark metal tinted toward the faction.
    /// The crowd's Exos are baked with the same parts (<see cref="Bake"/>).
    /// </summary>
    public sealed partial class SoldierView
    {
        enum Part { Torso, Pack, Helmet, PadL, PadR, ArmL, ArmR, GunL, GunR, ThighL, ThighR, ShinL, ShinR, Count }

        Transform[] exo;
        MeshRenderer[] exoRenderers;
        MaterialPropertyBlock exoProps;
        bool exoOn, launcher;
        int exoFaction = -1, nextArm;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>An Exo's weapons on its arms (no rifle in its hands).</summary>
        public bool IsExo => exoOn;

        void ExoShow(bool on, int arms)
        {
            if (on && exo == null)
                ExoBuild();
            launcher = arms == LtCode.WeaponExoLauncher;
            if (exo == null)
                return;
            exoOn = on;
            for (int i = 0; i < exo.Length; i++)
                exo[i].gameObject.SetActive(on);
            rifleRenderer.enabled = !on && !dead;
            if (holoRenderer != null)
                holoRenderer.enabled = !on && !dead;
        }

        void ExoBuild()
        {
            exo = new Transform[(int)Part.Count];
            exoRenderers = new MeshRenderer[exo.Length];
            exoProps = new MaterialPropertyBlock();
            for (int i = 0; i < exo.Length; i++)
            {
                var g = new GameObject(((Part)i).ToString());
                g.transform.SetParent(transform, false);
                bool barrel = i == (int)Part.GunL || i == (int)Part.GunR;
                g.AddComponent<MeshFilter>().sharedMesh = barrel ? ExoMeshes.Tube : ExoMeshes.Box;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = barrel ? look.HoloBody : look.ExoPlate;
                r.shadowCastingMode = ShadowCastingMode.On;
                GpuDriven.Exclude(r);
                exo[i] = g.transform;
                exoRenderers[i] = r;
                g.SetActive(false);
            }
            ExoShadows();
        }

        /// <summary>The parts as the body is drawn: a shadow only (ours in first person), or seen.</summary>
        void ExoShadows()
        {
            if (exoRenderers == null)
                return;
            var mode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in exoRenderers)
                r.shadowCastingMode = mode;
        }

        void ExoFaction(int faction)
        {
            if (exo == null || faction == exoFaction)
                return;
            exoFaction = faction;
            var ink = SoldierColors.Ink(faction);
            exoProps.SetColor(BaseColorId, Color.Lerp(new Color(0.16f, 0.17f, 0.18f), ink, 0.38f));
            for (int i = 0; i < exoRenderers.Length; i++)
                if (i != (int)Part.GunL && i != (int)Part.GunR)
                    exoRenderers[i].SetPropertyBlock(exoProps);
        }

        /// <summary>The parts onto the bones as they stand now, the barrels along <paramref name="aim"/>.</summary>
        void ExoPlace(Quaternion aim)
        {
            if (!exoOn || hips == null || neck == null || rUpper == null || lUpper == null)
                return;
            var up = (neck.position - hips.position).normalized;
            var right = (rUpper.position - lUpper.position).normalized;
            var fwd = Vector3.Cross(right, up).normalized;
            var body = Quaternion.LookRotation(fwd, up);
            var chestAt = Vector3.Lerp(chest.position, upperChest.position, 0.6f) + fwd * 0.03f;
            Set(Part.Torso, chestAt, body, new Vector3(0.5f, 0.44f, 0.34f));
            Set(Part.Pack, chestAt - fwd * 0.23f + up * 0.03f, body, new Vector3(0.4f, 0.42f, 0.16f));
            Set(Part.Helmet, head.position + up * 0.06f + fwd * 0.02f, Quaternion.LookRotation(aim * Vector3.forward, up), new Vector3(0.3f, 0.31f, 0.33f));
            Set(Part.PadL, lUpper.position + up * 0.06f - right * 0.05f, body, new Vector3(0.2f, 0.14f, 0.26f));
            Set(Part.PadR, rUpper.position + up * 0.06f + right * 0.05f, body, new Vector3(0.2f, 0.14f, 0.26f));
            Along(Part.ArmL, lLower.position, lHand.position, up, new Vector2(0.17f, 0.17f), 0.06f);
            Along(Part.ArmR, rLower.position, rHand.position, up, new Vector2(0.17f, 0.17f), 0.06f);
            var dir = aim * Vector3.forward;
            float r = launcher ? 0.075f : 0.035f, len = launcher ? 0.42f : 0.45f;
            var barrel = Quaternion.LookRotation(dir, up);
            Set(Part.GunL, lHand.position + dir * 0.04f, barrel, new Vector3(r, r, len));
            Set(Part.GunR, rHand.position + dir * 0.04f, barrel, new Vector3(r, r, len));
            Leg(Part.ThighL, lThigh, lShin, fwd, new Vector2(0.2f, 0.22f));
            Leg(Part.ThighR, rThigh, rShin, fwd, new Vector2(0.2f, 0.22f));
            Leg(Part.ShinL, lShin, lFoot, fwd, new Vector2(0.17f, 0.2f));
            Leg(Part.ShinR, rShin, rFoot, fwd, new Vector2(0.17f, 0.2f));
            // Shots leave the arms by turns.
            var arm = (nextArm & 1) == 0 ? rHand : lHand;
            Muzzle = arm.position + dir * (0.04f + len);
            MuzzleForward = dir;
        }

        void Set(Part p, Vector3 at, Quaternion rot, Vector3 size)
        {
            var t = exo[(int)p];
            t.SetPositionAndRotation(at, rot);
            t.localScale = size;
        }

        /// <summary>A box from <paramref name="a"/> to <paramref name="b"/> (its z), <paramref name="pad"/> past each end.</summary>
        void Along(Part p, Vector3 a, Vector3 b, Vector3 up, Vector2 thick, float pad)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f)
                return;
            Set(p, (a + b) * 0.5f, Quaternion.LookRotation(d / len, up), new Vector3(thick.x, thick.y, len + 2f * pad));
        }

        /// <summary>A plate along a leg bone (its y), a little in front of it.</summary>
        void Leg(Part p, Transform from, Transform to, Vector3 fwd, Vector2 thick)
        {
            if (from == null || to == null)
                return;
            var d = from.position - to.position;
            float len = d.magnitude;
            if (len < 1e-4f)
                return;
            var along = d / len;
            var face = Vector3.ProjectOnPlane(fwd, along).normalized;
            if (face.sqrMagnitude < 1e-6f)
                face = fwd;
            Set(p, (from.position + to.position) * 0.5f + face * 0.04f, Quaternion.LookRotation(face, along), new Vector3(thick.x, len, thick.y));
        }

        /// <summary>The parts, placed, as one mesh in this soldier's space (the crowd's bake).</summary>
        Mesh ExoMesh(Matrix4x4 toRoot)
        {
            var parts = new List<CombineInstance>();
            for (int i = 0; i < exo.Length; i++)
                parts.Add(new CombineInstance { mesh = exoRenderers[i].GetComponent<MeshFilter>().sharedMesh, transform = toRoot * exo[i].localToWorldMatrix });
            var m = new Mesh { name = "Exo armor" };
            m.CombineMeshes(parts.ToArray(), true, true);
            return m;
        }
    }

    /// <summary>The Exo's shapes: a unit box and a unit tube along +z (radius 1, 0..1),
    /// their UVs on the rifle texture's darkest texel (the crowd draws them with it).</summary>
    public static class ExoMeshes
    {
        /// <summary>Where the third-person rifle texture is near black (Tools/Blender/weapons_fbx.py).</summary>
        public static readonly Vector2 Dark = new Vector2(0.8667f, 0.8384f);
        static Mesh box, tube;

        public static Mesh Box => box ??= MakeBox();
        public static Mesh Tube => tube ??= MakeTube(12);

        static Mesh MakeBox()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            void Face(Vector3 normal, Vector3 u, Vector3 w)
            {
                int i = v.Count;
                var c = normal * 0.5f;
                v.Add(c - u * 0.5f - w * 0.5f);
                v.Add(c + u * 0.5f - w * 0.5f);
                v.Add(c + u * 0.5f + w * 0.5f);
                v.Add(c - u * 0.5f + w * 0.5f);
                for (int k = 0; k < 4; k++)
                    n.Add(normal);
                t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.left, Vector3.forward);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            var m = new Mesh { name = "Exo plate" };
            m.SetVertices(v);
            m.SetNormals(n);
            var uv = new Vector2[v.Count];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = Dark;
            m.uv = uv;
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        static Mesh MakeTube(int segs)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d);
                n.Add(d);
                v.Add(d + Vector3.forward);
                n.Add(d);
            }
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2;
                t.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });
            }
            // The muzzle's face, dark: a cap at z 1.
            int c = v.Count;
            v.Add(Vector3.forward);
            n.Add(Vector3.forward);
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2f / segs;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f));
                n.Add(Vector3.forward);
            }
            for (int i = 0; i < segs; i++)
                t.AddRange(new[] { c, c + 1 + i, c + 2 + i });
            var m = new Mesh { name = "Exo barrel" };
            m.SetVertices(v);
            m.SetNormals(n);
            var uv = new Vector2[v.Count];
            for (int i = 0; i < uv.Length; i++)
                uv[i] = Dark;
            m.uv = uv;
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
