// A medic's Mend field (U6) on the ground round them: a disc (uv 0..1
// across it) with a bright edge at its reach, a faint fill, and rings of
// light running out from the middle to the edge, a beat each half second
// (the server heals on the beat). Added on top, HDR; tested against depth
// (never seen through a wall), a little over the ground.
Shader "Lattice/MendField"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.4, 2.2, 1.0, 1)
        _Strength("Strength", Float) = 1
        _Beat("Rings a second", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Field"
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
            float4 _Color;
            float _Strength;
            float _Beat;
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
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                if (r > 1.0)
                    discard;
                float aa = max(fwidth(r), 1e-4);
                float edge = smoothstep(0.93 - aa, 0.985, r) * (1.0 - smoothstep(0.985, 1.0, r));
                float fill = 0.06 * smoothstep(0.0, 0.9, r);
                // A ring each beat, running out and fading as it goes.
                float t = frac(_Time.y * _Beat);
                float ring = exp(-pow((r - t) / 0.035, 2.0)) * (1.0 - t) * 0.6;
                float k = edge + fill + ring;
                return half4(_Color.rgb * k * _Strength, 1);
            }
            ENDHLSL
        }
    }
}
