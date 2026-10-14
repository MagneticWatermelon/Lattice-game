// Kiln's life (K4, docs/kiln.md "The life"), cosmetic and the same on every
// client: World/Life.cs sets the clock as globals (_LifeMinute: minutes into
// the two-hour cycle; _LifeSecs: seconds into it; _LifeNight: how dark, 0-1)
// and each mesh carries what it is per vertex. Nothing here hides a player:
// the membranes are high and see-through, the fliers points of light, the
// coral flat on the rock, the sand-whales' ridges low.
//
// _Mode 0, a sunsail's membrane (blended): uv0 (how far out from the stalk,
// 0-1; around it, 0-1), uv1.x a hash of the sail, uv2.xyz the stalk's top
// (the pivot). Open by day, folding up through dusk to stand closed at night,
// opening through dawn, each a little late or early; swaying in the wind.
// Lit through from behind (the sun shining through it).
// _Mode 1, a night flier (added on): uv0 the quad's corner, uv1 (seed, its
// flock's phase), uv2 (flock's anchor xyz, its circle's radius). After dark,
// rising out of its rift to circle over it.
// _Mode 2, lattice coral (opaque): lit, glowing from within at night.
// _Mode 3, a sand-whale (blended): uv0 (along, across: m from its middle),
// uv1 (which whale, 0 the ridge / 1 the spray); _Whales[i] (x, z, heading,
// 1 if it swims). The ridge rides the ground (the terrain's heights,
// _LifeHeights), the spray off its back.
Shader "Lattice/Life"
{
    Properties
    {
        _Color("Color", Color) = (0.9, 0.78, 0.42, 1)
        [HDR] _Glow("Glow", Color) = (0.4, 1.6, 1.4, 1)
        _Mode("Mode (0 sail, 1 flier, 2 coral, 3 whale)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite("Z Write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Life"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _Glow;
            float _Mode;
            CBUFFER_END

            float _LifeMinute, _LifeSecs, _LifeNight;
            float4 _Whales[2];
            TEXTURE2D(_LifeHeights);
            SAMPLER(sampler_LifeHeights);
            float4 _LifeMap; // x: half the world, y: height spacing, z: samples per side, w: the world (m)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float fog : TEXCOORD4;
                float extra : TEXCOORD5;
                float2 puff : TEXCOORD6;
            };

            float Hash(float x) { return frac(sin(x * 127.1 + 3.7) * 43758.5453); }

            // How open a sunsail is at minute m, `late` minutes behind the rest:
            // folding from 70 (dusk) over 5 minutes, opening from 110 (dawn) over 6.
            float SailOpen(float m, float late)
            {
                float t = m - late;
                if (t >= 110.0)
                    return saturate((t - 110.0) / 6.0);
                if (t >= 70.0)
                    return 1.0 - saturate((t - 70.0) / 5.0);
                return t < 0.0 ? 0.0 : 1.0;
            }

            float Ground(float2 xz)
            {
                float2 game = xz + _LifeMap.x;
                return SAMPLE_TEXTURE2D_LOD(_LifeHeights, sampler_LifeHeights, (game / _LifeMap.y + 0.5) / _LifeMap.z, 0).r;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;
                float3 n = v.normalOS;
                o.extra = 1.0;
                o.puff = 0;
                if (_Mode < 0.5)
                {
                    // A sunsail's membrane, folded up about its stalk's top.
                    float3 pivot = v.uv2.xyz;
                    float3 d = p - pivot;
                    float r = length(d.xz);
                    float2 dir = r > 1e-3 ? d.xz / r : float2(1, 0);
                    float open = SailOpen(_LifeMinute, v.uv1.x * 3.0 - 1.0);
                    float sway = sin(_LifeSecs * (0.35 + 0.2 * v.uv1.x) + v.uv1.x * 40.0 + v.uv.y * 6.283) * 0.05 * v.uv.x;
                    float tilt = (1.0 - open) * 1.35 + sway;
                    float s, c;
                    sincos(tilt, s, c);
                    p = pivot + float3(dir.x * r * c, d.y + r * s, dir.y * r * c);
                    n = normalize(float3(-dir.x * s, c, -dir.y * s));
                    o.extra = open;
                }
                else if (_Mode < 1.5)
                {
                    // A flier: circling its flock's anchor, risen with the night.
                    float seed = v.uv1.x, phase = v.uv1.y;
                    float t = _LifeSecs;
                    float ang = t * (0.05 + 0.02 * Hash(phase)) + phase * 6.283 + (seed - 0.5) * 0.9;
                    float radius = v.uv2.w * (0.6 + 0.4 * Hash(seed * 3.1));
                    float rise = smoothstep(0.0, 1.0, _LifeNight) * (60.0 + 90.0 * Hash(phase * 1.7));
                    float3 at = v.uv2.xyz + float3(cos(ang) * radius, rise + 14.0 * sin(t * 0.7 + seed * 50.0), sin(ang) * radius);
                    at += float3(Hash(seed * 7.3) - 0.5, Hash(seed * 9.1) - 0.5, Hash(seed * 4.7) - 0.5) * 30.0;
                    float3 view = TransformWorldToView(at);
                    view.xy += v.uv * (0.6 + 0.4 * Hash(seed * 2.3));
                    o.positionCS = TransformWViewToHClip(view);
                    o.positionWS = at;
                    o.normalWS = float3(0, 1, 0);
                    o.uv = v.uv;
                    o.uv1 = v.uv1;
                    o.fog = ComputeFogFactor(o.positionCS.z);
                    o.extra = 0.6 + 0.4 * sin(t * (3.0 + 4.0 * seed) + seed * 30.0);
                    return o;
                }
                else if (_Mode > 2.5)
                {
                    // A sand-whale: its ridge on the ground where it swims.
                    float4 w = _Whales[(int)round(v.uv1.x)];
                    float sn, cs;
                    sincos(w.z, sn, cs);
                    float2 local = v.uv;
                    float2 xz = w.xy + float2(local.x * cs - local.y * sn, local.x * sn + local.y * cs);
                    float shape = exp(-(local.x * local.x) / 900.0 - (local.y * local.y) / 30.0);
                    float wave = 0.5 + 0.5 * sin(local.x * 0.25 - _LifeSecs * 3.0);
                    float h = v.uv1.y > 0.5 ? p.y : 0.85 * shape * (0.8 + 0.2 * wave);
                    p = float3(xz.x, Ground(xz) + h + 0.05, xz.y);
                    if (v.uv1.y > 0.5)
                    {
                        // A puff of the spray: turned to the camera about the upright,
                        // uv2 (its height, which side, bottom 0 / top 1).
                        float3 foot = float3(xz.x, Ground(xz) + 0.3, xz.y);
                        float3 toCam = GetCameraPositionWS() - foot;
                        float3 right = normalize(float3(toCam.z, 0.0, -toCam.x) + 1e-4);
                        p = foot + right * (v.uv2.y * v.uv2.x * 0.9) + float3(0, v.uv2.z * v.uv2.x, 0);
                        o.puff = v.uv2.yz;
                    }
                    // Its slope, steepened fourfold for the light (the ridge is low: it
                    // reads by its shading and its plume, never by hiding anyone).
                    float2 slope = 4.0 * h * float2(-2.0 * local.x / 900.0, -2.0 * local.y / 30.0);
                    float2 sw = float2(slope.x * cs - slope.y * sn, slope.x * sn + slope.y * cs);
                    n = v.uv1.y > 0.5 ? float3(0, 1, 0) : normalize(float3(-sw.x, 1.0, -sw.y));
                    // The churned sand round it: wider than the ridge, fading out.
                    float churn = exp(-(local.x * local.x) / 1600.0 - (local.y * local.y) / 90.0);
                    o.extra = (v.uv1.y > 0.5 ? 1.0 : churn) * w.w;
                }
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                Light main = GetMainLight();
                if (_Mode < 0.5)
                {
                    float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                    float3 toEye = normalize(GetCameraPositionWS() - i.positionWS);
                    // Ribs out from the stalk, and the membrane thinning to its rim.
                    float rib = smoothstep(0.93, 1.0, abs(frac(i.uv.y * 9.0) * 2.0 - 1.0));
                    half3 diffuse = main.color * saturate(dot(n, main.direction)) + SampleSH(n);
                    // The sun through it, from behind.
                    // (And some of the sky's light through it from anywhere above: it glows.)
                    half through = pow(saturate(dot(-toEye, main.direction)), 2.0) * 2.0 + 0.45 * saturate(-n.y * (front ? 1.0 : -1.0) + 0.5);
                    half3 c = _Color.rgb * (diffuse * 0.6 + main.color * through * (1.0 - 0.6 * rib)) * (1.0 - 0.45 * rib);
                    // Darker toward the stalk, thinning (and brighter) toward the rim.
                    c *= 0.75 + 0.35 * i.uv.x;
                    half a = (0.62 - 0.3 * smoothstep(0.7, 1.0, i.uv.x) + 0.25 * rib) * lerp(0.6, 1.0, i.extra);
                    return half4(MixFog(c, i.fog), a);
                }
                if (_Mode < 1.5)
                {
                    half d = saturate(1.0 - dot(i.uv, i.uv));
                    half3 c = _Glow.rgb * (d * d * i.extra * _LifeNight);
                    return half4(MixFogColor(c, half3(0, 0, 0), i.fog), 1.0);
                }
                if (_Mode < 2.5)
                {
                    float3 n = normalize(i.normalWS);
                    half3 lit = _Color.rgb * (main.color * saturate(dot(n, main.direction)) + SampleSH(n));
                    float pulse = 0.75 + 0.25 * sin(_LifeSecs * 0.8 + dot(i.positionWS, float3(0.13, 0.07, 0.11)));
                    lit += _Glow.rgb * (0.08 + 1.8 * _LifeNight * pulse);
                    return half4(MixFog(lit, i.fog), 1.0);
                }
                // The whale's sand: lit by its slope, darker where it's churned; the spray
                // thin and moving.
                float3 wn = normalize(i.normalWS);
                half3 sand = _Color.rgb * (main.color * saturate(dot(wn, main.direction)) + SampleSH(wn));
                half a = i.extra;
                if (i.uv1.y < 0.5)
                    sand *= 0.62 + 0.38 * smoothstep(0.35, 0.9, i.extra);
                if (i.uv1.y > 0.5)
                {
                    float grain = frac(sin(dot(floor(i.positionWS.xz * 0.8 + _LifeSecs * float2(2.0, 0.7)), float2(12.9898, 78.233))) * 43758.5453);
                    // Soft at its sides, thinning as it rises.
                    a *= 0.75 * (0.6 + 0.4 * grain) * (1.0 - i.puff.x * i.puff.x) * (1.0 - i.puff.y) * saturate(1.0 - abs(i.uv.y) / 8.0);
                }
                return half4(MixFog(sand, i.fog), saturate(a));
            }
            ENDHLSL
        }
    }
}
