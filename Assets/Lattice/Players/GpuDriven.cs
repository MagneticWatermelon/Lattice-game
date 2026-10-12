using System.Reflection;
using UnityEngine;

namespace Lattice.Players
{
    /// <summary>
    /// Keeps a mesh renderer out of the GPU Resident Drawer. The drawer is made
    /// for what stays put: a renderer that moves every frame (a rifle in a
    /// soldier's hands, the first-person view) or comes and goes (with a
    /// soldier's band) costs it a re-upload or a rebuilt instance each time,
    /// where the plain path (the SRP Batcher) costs nothing extra. URP's own
    /// switch for this (DisallowGPUDrivenRendering) is internal, as is the
    /// renderer property it sets: set it once per renderer, by reflection.
    /// </summary>
    public static class GpuDriven
    {
        static readonly PropertyInfo Allow = typeof(Renderer).GetProperty("allowGPUDrivenRendering",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static bool warned;

        public static void Exclude(Renderer r)
        {
            if (Allow == null)
            {
                if (!warned)
                    Debug.LogWarning("Lattice: no Renderer.allowGPUDrivenRendering (stripped or renamed): moving renderers stay in the GPU Resident Drawer");
                warned = true;
                return;
            }
            Allow.SetValue(r, false);
        }
    }
}
