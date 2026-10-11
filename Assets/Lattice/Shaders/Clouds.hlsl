// Lattice: the cloud layer's density, shared by the sky (Sky.shader) and the
// clouds' shadows (CloudShadow.compute), so the shadows lie under the clouds.
// Define CLOUD_SAMPLE(uv) to sample the cloud noise (Tools/Blender/clouds.py)
// before including: the sky filters by screen derivatives, the compute shader
// samples mip 0.
#ifndef LATTICE_CLOUDS_INCLUDED
#define LATTICE_CLOUDS_INCLUDED

// The clouds' shapes at uv: two turned samples (against tiling), cut to the cover.
float CloudShape(float2 uv, float cover)
{
    float a = CLOUD_SAMPLE(uv).r;
    float b = CLOUD_SAMPLE(float2(uv.y, -uv.x) * 1.37 + 0.31).r;
    float s = a * 0.65 + b * 0.35;
    return saturate((s - (1.0 - cover)) / max(cover, 1e-3));
}

// How thick the layer is (0..1) over world (x, z), drifted by the wind (m so
// far), at a cover and a scale (m per tile of the noise): the shapes, their
// edges eaten by finer cells.
float CloudDensity(float2 xz, float2 drift, float cover, float scale)
{
    float2 uv = (xz + drift) / scale;
    float d = CloudShape(uv, cover);
    float detail = CLOUD_SAMPLE(uv * 3.7 + 0.53).g;
    d = saturate(d - (1.0 - detail) * 0.35);
    return smoothstep(0.0, 0.55, d);
}

#endif
