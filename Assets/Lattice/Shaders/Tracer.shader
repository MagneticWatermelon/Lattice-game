// Tracers: every streak the plugin flies (lt_tracers), drawn in one call.
// Each tracer is a camera-facing quad from its tail to its head, never
// thinner than a pixel or two (so a distant fight's tracers still read),
// bright at the head and fading to its tail, added on top (bloom does the glow).
// Positions arrive in game space (x east, y north, z up) and are taken to
// Unity's here: (x - 4096, z, y - 4096).
Shader "Lattice/Tracer"
{
    Properties
    {
        [HDR] _OwnColor("Own", Color) = (6, 4.6, 2.6, 1)
        [HDR] _SeenColor("Others", Color) = (6, 2.8, 1.0, 1)
        [HDR] _AmbientColor("Distant", Color) = (5, 2.4, 0.9, 1)
        _Width("Width (m)", Float) = 0.035
        _MinPixels("Width at least (px)", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Tracer"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Matches LtTracer (32 bytes): head, tail, kind:u8 faction:u8 reserved:u16, age.
            struct Tracer
            {
                float3 head;
                float3 tail;
                uint kindFaction;
                float age;
            };
            StructuredBuffer<Tracer> _Tracers;

            CBUFFER_START(UnityPerMaterial)
            float4 _OwnColor;
            float4 _SeenColor;
            float4 _AmbientColor;
            float _Width;
            float _MinPixels;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;       // x: along (0 tail, 1 head), y: across (-1..1)
                nointerpolation float4 color : TEXCOORD1;
            };

            float3 ToUnity(float3 g) { return float3(g.x - 4096.0, g.z, g.y - 4096.0); }

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o;
                Tracer t = _Tracers[vid / 6];
                uint c = vid % 6;
                // Two triangles: (0,1,2) (2,1,3) over corners (along, side).
                float along = (c == 1 || c == 3 || c == 4) ? 1.0 : 0.0;
                float side = (c == 2 || c == 3 || c == 5) ? 1.0 : -1.0;
                float3 head = ToUnity(t.head);
                float3 tail = ToUnity(t.tail);
                float3 p = lerp(tail, head, along);
                float3 dir = head - tail;
                float len = max(length(dir), 1e-4);
                dir /= len;
                float3 toCam = GetCameraPositionWS() - p;
                float dist = length(toCam);
                float3 across = cross(dir, toCam / max(dist, 1e-4));
                float acrossLen = length(across);
                across = acrossLen > 1e-4 ? across / acrossLen : float3(0, 1, 0);
                // A pixel's size at this distance, from the projection.
                float pixel = 2.0 * dist / (_ScreenParams.y * UNITY_MATRIX_P._m11);
                float halfWidth = 0.5 * max(_Width, _MinPixels * pixel);
                p += across * side * halfWidth;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = float2(along, side);
                uint kind = t.kindFaction & 0xFF;
                float4 col = kind == 0 ? _OwnColor : kind == 1 ? _SeenColor : _AmbientColor;
                // Thinner than its true width at range: keep its energy, so
                // distant tracers don't swell into bright bars.
                col.rgb *= saturate(_Width / (2.0 * halfWidth)) * 0.85 + 0.15;
                // Just fired: a touch hotter.
                col.rgb *= 1.0 + 0.6 * saturate(1.0 - t.age * 8.0);
                o.color = col;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = 1.0 - i.uv.y * i.uv.y;
                float core = across * across;
                float fade = i.uv.x * i.uv.x;
                return half4(i.color.rgb * core * fade, 0);
            }
            ENDHLSL
        }
    }
}
