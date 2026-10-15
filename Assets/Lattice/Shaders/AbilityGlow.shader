// The classes' abilities as others see them (U6): light on a body's own
// surface, brightest at its edges (a rim) and crossed by bands that run up
// it. A Bulwark is a shell of it over the soldier in its faction's color; a
// cloaked soldier is drawn with nothing but a faint one (the cloak's
// shimmer). Added on top, HDR, so bloom carries the brightest of it.
Shader "Lattice/AbilityGlow"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.6, 1.2, 2.0, 1)
        _Rim("Rim power", Float) = 2.5
        _Fill("Light facing the eye (of the rim's)", Float) = 0.08
        _Bands("Bands' strength", Float) = 0.6
        _BandScale("Bands per meter", Float) = 3
        _BandSpeed("Bands' speed, m/s", Float) = 0.8
        _Strength("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Rim;
            float _Fill;
            float _Bands;
            float _BandScale;
            float _BandSpeed;
            float _Strength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float facing = saturate(abs(dot(normalize(i.normalWS), view)));
                float rim = pow(1.0 - facing, _Rim);
                // Bands running up the body, thin and soft.
                float b = frac(i.positionWS.y * _BandScale - _Time.y * _BandSpeed);
                float band = smoothstep(0.0, 0.08, b) * (1.0 - smoothstep(0.08, 0.3, b));
                float k = (rim + _Fill) * (1.0 + _Bands * band);
                return half4(_Color.rgb * k * _Strength, 1);
            }
            ENDHLSL
        }
    }
}
