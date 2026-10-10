using UnityEngine;

namespace Lattice.World
{
    /// <summary>Renders a realtime reflection probe once, at start (the sky doesn't move yet).</summary>
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class RenderOnce : MonoBehaviour
    {
        void Start()
        {
            GetComponent<ReflectionProbe>().RenderProbe();
        }
    }
}
