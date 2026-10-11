// Muzzle flashes, drawn procedurally on FlashMesh: two crossed quads along
// the barrel (uv.y 0..1, the fire's length) and one facing back at the
// shooter (uv.y 2..3, a star). Added on top, HDR, so bloom carries the glow.
// Each flash lives a frame or two; _Seed and the instance id vary its shape.
Shader "Lattice/Flash"
{
    Properties
    {
        [HDR] _Core("Core", Color) = (14, 11, 7, 1)
        [HDR] _Edge("Edge", Color) = (8, 3.2, 0.8, 1)
        _Seed("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Flash"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Core;
            float4 _Edge;
            float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float seed : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                o.seed = frac(_Seed + dot(origin, float3(12.9898, 78.233, 37.719)) * 0.137);
                return o;
            }

            float hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col;
                if (i.uv.y < 1.5)
                {
                    // Along the barrel: a cone of fire, widest near the muzzle.
                    float along = i.uv.y;
                    float across = abs(i.uv.x * 2.0 - 1.0);
                    float width = 0.18 + 0.82 * pow(1.0 - along, 0.7);
                    float body = saturate(1.0 - across / width);
                    float flicker = 0.75 + 0.5 * hash(floor(along * 7.0) + i.seed * 31.0);
                    float k = body * body * pow(1.0 - along, 1.6) * flicker;
                    col = lerp(_Edge.rgb, _Core.rgb, saturate(body * (1.0 - along) * 1.4)) * k;
                }
                else
                {
                    // Facing back: a star of 5-7 points around a hot core.
                    float2 c = float2(i.uv.x - 0.5, i.uv.y - 2.5) * 2.0;
                    float r = length(c);
                    float ang = atan2(c.y, c.x);
                    float spikes = pow(abs(cos(ang * 3.0 + i.seed * 6.2831)), 10.0) * 0.6
                                 + pow(abs(cos(ang * 2.5 + i.seed * 3.17 + 1.1)), 18.0) * 0.4;
                    float reach = 0.28 + 0.72 * spikes;
                    float k = saturate(1.0 - r / reach);
                    k = k * k;
                    float core = saturate(1.0 - r * 3.2);
                    col = lerp(_Edge.rgb, _Core.rgb, core) * (k + core * 0.6);
                }
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
}
