// Lattice: the grass GrassField draws in one indirect draw. Each instance is a
// clump from Grass.compute's buffer: the clump mesh's blades, turned, sized and
// swayed by the wind. Opaque, two-sided, no texture: the color runs from root
// to tip, green on grass and straw on meadow. Lit by the sun (with shadows, and
// through the blades against the light) and the sky; no shadows of its own.
Shader "Lattice/Grass"
{
    Properties
    {
        _GrassRoot("Grass Root", Color) = (0.25, 0.29, 0.13, 1)
        _GrassTip("Grass Tip", Color) = (0.52, 0.56, 0.30, 1)
        _MeadowRoot("Meadow Root", Color) = (0.38, 0.34, 0.20, 1)
        _MeadowTip("Meadow Tip", Color) = (0.64, 0.58, 0.39, 1)
        _Translucency("Light Through Blades", Range(0, 2)) = 0.5
        _Ambient("Sky Light at the Root", Range(0, 1)) = 0.6
        _Flatten("Lit as the Ground", Range(0, 1)) = 0.75
        _Wrap("Wrap Lighting", Range(0, 1)) = 0.2
        _Wind("Wind (direction x, z, strength, speed)", Vector) = (0.8, 0.6, 0.12, 1.3)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Clump
        {
            float3 pos;
            float yaw;
            float height;
            float width;
            float tint;
            float phase;
        };
        StructuredBuffer<Clump> _Clumps;

        CBUFFER_START(UnityPerMaterial)
        half4 _GrassRoot, _GrassTip, _MeadowRoot, _MeadowTip;
        float4 _Wind;
        half _Translucency, _Ambient, _Flatten, _Wrap;
        CBUFFER_END

        // The world size of a pixel 1 m away (2 tan(fov / 2) / screen height), from GrassField.
        float _GrassPixel;

        struct Attributes
        {
            float3 positionOS : POSITION;   // the blade's centerline, for a clump 1 m tall
            float3 normalOS : NORMAL;       // the blade's face
            float4 tangentOS : TANGENT;     // xyz: this vertex's offset across the blade
            float2 uv : TEXCOORD0;          // x: across, y: along the blade (0 root, 1 tip)
            float2 blade : TEXCOORD1;       // x: the blade's random, y: its half width at the root (m)
            float2 root : TEXCOORD2;        // the blade's root in the clump (m)
            uint instanceID : SV_InstanceID;
        };

        struct Blade
        {
            float3 positionWS;
            float3 normalWS;
            float along;
            float tint;
            float rand;
        };

        float3 Turn(float3 v, float sn, float cs)
        {
            return float3(v.x * cs - v.z * sn, v.y, v.x * sn + v.z * cs);
        }

        Blade PlaceBlade(Attributes v)
        {
            Clump c = _Clumps[v.instanceID];
            float sn, cs;
            sincos(c.yaw, sn, cs);
            float3 center = float3(v.root.x, 0.0, v.root.y) + v.positionOS * c.height;
            // Blades stay at least about a pixel wide: far out they'd shimmer as specks.
            float dist = distance(_WorldSpaceCameraPos, c.pos);
            float widen = max(1.0, dist * _GrassPixel * 0.5 / max(v.blade.y * c.width, 1e-4));
            float3 side = v.tangentOS.xyz * (c.width * widen);
            float3 p = c.pos + Turn(center + side, sn, cs);

            // Wind: broad waves rolling across the field, slow gusts, a flutter per blade.
            float2 dir = _Wind.xy;
            float wave = sin(dot(c.pos.xz, dir) * 0.11 - _Time.y * _Wind.w) * 0.5 + 0.5;
            float gust = sin(dot(c.pos.xz, float2(-dir.y, dir.x)) * 0.023 + _Time.y * 0.31) * 0.5 + 0.5;
            float flutter = sin(_Time.y * _Wind.w * 3.1 + c.phase + v.blade.x * 6.2831853);
            float bend = _Wind.z * (0.25 + 0.75 * wave * (0.5 + gust)) + 0.03 * flutter;
            float t2 = v.positionOS.y * v.positionOS.y;
            p.xz += dir * (bend * t2 * c.height);
            p.y -= 0.3 * bend * bend * t2 * c.height;

            Blade b;
            b.positionWS = p;
            b.normalWS = Turn(v.normalOS, sn, cs);
            b.along = v.uv.y;
            b.tint = c.tint;
            b.rand = v.blade.x;
            return b;
        }

        // Blades are lit mostly as the ground they stand on: their own faces would flicker.
        float3 SoftNormal(float3 n, bool front)
        {
            n = normalize(front ? n : -n);
            return normalize(lerp(n, float3(0.0, 1.0, 0.0), _Flatten));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 shade : TEXCOORD2;   // along, tint, random
                float fog : TEXCOORD3;
            };

            Varyings ForwardVert(Attributes v)
            {
                Blade b = PlaceBlade(v);
                Varyings o;
                o.positionWS = b.positionWS;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.normalWS = b.normalWS;
                o.shade = float3(b.along, b.tint, b.rand);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 ForwardFrag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half along = i.shade.x;
                half3 root = lerp(_GrassRoot.rgb, _MeadowRoot.rgb, i.shade.y);
                half3 tip = lerp(_GrassTip.rgb, _MeadowTip.rgb, i.shade.y);
                half3 albedo = lerp(root, tip, smoothstep(0.0, 1.0, along)) * (0.85 + 0.3 * i.shade.z);
                float3 n = SoftNormal(i.normalWS, front);

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half wrap = saturate((dot(n, light.direction) + _Wrap) / (1.0 + _Wrap));
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                half through = pow(saturate(dot(view, -light.direction)), 3.0) * _Translucency * along;
                half3 direct = light.color * (light.shadowAttenuation * (wrap + through));
                half3 ambient = SampleSH(n) * lerp(_Ambient, 1.0, along);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor occlusion = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                ambient *= occlusion.indirectAmbientOcclusion;
                direct *= occlusion.directAmbientOcclusion;
                #endif
                half3 color = albedo * (direct + ambient);
                return half4(MixFog(color, i.fog), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(Attributes v) : SV_POSITION
            {
                return TransformWorldToHClip(PlaceBlade(v).positionWS);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(Attributes v)
            {
                Blade b = PlaceBlade(v);
                NormalsVaryings o;
                o.positionCS = TransformWorldToHClip(b.positionWS);
                o.normalWS = b.normalWS;
                return o;
            }

            half4 NormalsFrag(NormalsVaryings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = SoftNormal(i.normalWS, front);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(n) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0.0);
                #else
                return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
