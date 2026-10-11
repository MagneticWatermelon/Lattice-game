// The holographic sight's glass (Combat/Rifle/HoloSight): faintly tinted,
// brighter at glancing angles, and on the front window the reticle, a dot
// in a ring, drawn at infinity: where the view ray runs parallel to the
// sight's axis (the rifle mesh's -x; its top is -z). So it stays put on the
// target while the eye moves, as a real holographic sight's does, and shows
// where the rifle points when it isn't held quite on the line of sight.
// Premultiplied: the glass darkens a little, the reticle adds light (HDR).
Shader "Lattice/HoloGlass"
{
    Properties
    {
        _Tint("Glass tint (alpha: how much it darkens)", Color) = (0.62, 0.8, 0.95, 0.06)
        _Reflect("Glancing reflection", Color) = (0.6, 0.72, 0.85, 1)
        [HDR] _ReticleColor("Reticle (black: none)", Color) = (5, 0.55, 0.32, 1)
        _Dot("Dot radius (radians)", Float) = 0.0024
        _Ring("Ring radius (radians)", Float) = 0.034
        _RingWidth("Ring width (radians)", Float) = 0.0013
        _FrontX("The reticle's window: object x below this", Float) = 0.17
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glass"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float4 _Reflect;
            float4 _ReticleColor;
            float _Dot;
            float _Ring;
            float _RingWidth;
            float _FrontX;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float objectX : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.objectX = v.positionOS.x;
                return o;
            }

            float Band(float x, float edge, float aa)
            {
                return 1.0 - smoothstep(edge - aa, edge + aa, x);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 view = normalize(i.positionWS - _WorldSpaceCameraPos);
                // Coated glass: it darkens what's behind it a touch (never lifts it, so
                // blacks stay black), with a trace of its tint and more reflection at
                // glancing angles.
                float facing = abs(dot(normalize(i.normalWS), view));
                float fres = pow(1.0 - saturate(facing), 4.0) * 0.45;
                float alpha = saturate(_Tint.a + fres);
                float3 color = _Tint.rgb * (_Tint.a * 0.15) + _Reflect.rgb * fres;

                // The reticle, at infinity along the sight's axis.
                float3 axis = TransformObjectToWorldDir(float3(-1, 0, 0));
                float3 up = TransformObjectToWorldDir(float3(0, 0, -1));
                float3 right = normalize(cross(up, axis));
                float along = dot(view, axis);
                if (i.objectX < _FrontX && along > 0.5)
                {
                    float2 p = float2(dot(view, right), dot(view, up)) / along;
                    float r = length(p);
                    float aa = max(fwidth(r), 1e-5);
                    float dotMask = Band(r, _Dot, aa);
                    float ring = Band(abs(r - _Ring), _RingWidth * 0.5, aa);
                    // A faint bloom of the projection around the dot.
                    float halo = exp(-r / (_Dot * 2.5)) * 0.12;
                    color += _ReticleColor.rgb * saturate(dotMask + ring + halo);
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
