// Kiln's clockwork (K4, docs/kiln.md "Clockwork"), drawn from the game step:
// World/Clockwork.cs sets the clock as globals each frame (_ClockVent: steps
// into a vent's 30 s cycle before its phase; _ClockCycle: steps into the
// two-hour cycle; both with the frame's fraction of a step), and each mesh
// carries what it is per vertex. What's drawn follows what the plugin's
// collision does at that step (the constants are LtConst's, passed in by
// Clockwork.cs): it never shows a bridge standing that isn't.
//
// _Mode 0, a vent lift's column (additive): uv0 (across its face, height m),
// color alpha its fade from foot to top, uv1.x its phase (steps). Running,
// bright streaks rising; resting, dim, and its glow counts up to the next
// lift; its last 3 s running, it flickers.
// _Mode 1, a sandfall (blended): uv1.x its set (LT_WHEN_FALLS_A or _B).
// Pouring, streaks of sand falling fast; dry, a thin trickle. It thins over
// two seconds when its turn ends (the drop it slowed is gone at once).
// _Mode 2, a living bridge's coral (lit, blended, depth written): uv1.x how
// far it is from the bridge's nearer end to its middle (0..1). Through dusk it
// grows from both ends toward the middle, see-through (not yet stood on); at
// night, whole and glowing; the first minute of dawn, cracking and dimming;
// then gone. By day, nothing.
Shader "Lattice/Clockwork"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.5, 1.5, 2.1, 1)
        [HDR] _Glow("Glow (coral's veins)", Color) = (0.3, 1.4, 1.6, 1)
        [NoScaleOffset] _MainTex("Soft Edge (vein glow)", 2D) = "white" {}
        _Mode("Mode (0 vent, 1 sand, 2 coral)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
        [Enum(Off, 0, On, 1)] _ZWrite("Z Write", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Clockwork"
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _Glow;
            float _Mode;
            CBUFFER_END

            // The clock (Clockwork.cs), in steps with the frame's fraction.
            float _ClockVent, _ClockCycle;
            // LtConst's: a vent's cycle and run; the sandfalls' turn; where dusk,
            // night and dawn start, and where the bridges come down.
            float4 _ClockVentConst;   // (VENT_CYCLE, VENT_ON, TICK_HZ, FALLS_STEPS)
            float4 _ClockDayConst;    // (DUSK, NIGHT, DAWN, BRIDGE_DOWN)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                half4 color : COLOR;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            half4 Vent(Varyings i)
            {
                float cycle = _ClockVentConst.x, on = _ClockVentConst.y, hz = _ClockVentConst.z;
                float t = fmod(_ClockVent + i.uv1.x, cycle);
                half edge = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 0.5)).a;
                half fade = i.color.a;
                half b;
                if (t < on)
                {
                    // Running: streaks rising up it, about 12 m/s.
                    float rise = frac(i.uv.y * 0.08 - t / hz * 1.0);
                    half streak = 0.55 + 0.45 * smoothstep(0.6, 1.0, rise);
                    // Its last 3 s, flickering: about to rest.
                    float left = (on - t) / hz;
                    half flicker = left < 3.0 ? 0.55 + 0.45 * step(0.5, frac(left * (5.0 - left))) : 1.0;
                    b = streak * flicker;
                }
                else
                {
                    // Resting: its glow counts up to the next lift.
                    float r = (t - on) / (cycle - on);
                    b = 0.08 + 0.32 * r * (0.75 + 0.25 * sin(r * r * 60.0));
                }
                half3 c = _Color.rgb * (b * edge * fade);
                return half4(MixFogColor(c, half3(0, 0, 0), i.fog), 1.0);
            }

            half4 Sand(Varyings i)
            {
                float turn = _ClockVentConst.w, hz = _ClockVentConst.z;
                float t = fmod(_ClockCycle, 2.0 * turn);
                // Which set pours, and how long since its turn began or ended.
                bool firstSet = i.uv1.x < 2.5;
                float since = firstSet ? t : t - turn;   // < 0 or >= turn: not its turn
                half pour;
                if (since >= 0.0 && since < turn)
                    pour = 1.0;
                else
                {
                    float after = firstSet ? t - turn : t;   // since its turn ended
                    after = after < 0.0 ? after + 2.0 * turn : after;
                    pour = 1.0 - saturate(after / (2.0 * hz));
                }
                float secs = _ClockCycle / hz;
                // Streaks of sand falling (about 25 m/s), each its own speed.
                float lane = floor(i.uv.x * 9.0);
                float h = Hash(lane + i.uv1.y * 13.0);
                float fall = frac(i.uv.y * 0.05 + secs * (1.1 + 0.5 * h) + h);
                half streak = 0.45 + 0.55 * smoothstep(0.2, 0.9, fall);
                half edge = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 0.5)).a;
                // Dry: a thin trickle.
                half a = lerp(0.06 * step(0.8, h), streak * 0.85, pour) * edge * i.color.a;
                half3 lit = _Color.rgb * (_MainLightColor.rgb * 0.35 + SampleSH(float3(0, 1, 0)));
                return half4(MixFog(lit, i.fog), a);
            }

            half4 Coral(Varyings i)
            {
                float dusk = _ClockDayConst.x, night = _ClockDayConst.y, dawn = _ClockDayConst.z, down = _ClockDayConst.w;
                float hz = _ClockVentConst.z;
                float c = _ClockCycle;
                float grown;
                half alpha, glow = 1.0;
                if (c >= night && c < down)
                {
                    grown = 1.0;
                    alpha = 1.0;
                    if (c >= dawn)
                    {
                        // Its warning: cracking (dark seams), dimming, flickering.
                        float w = (c - dawn) / (down - dawn);
                        float seam = frac(i.positionWS.x * 0.37 + i.positionWS.z * 0.29 + i.positionWS.y * 0.11);
                        glow = (1.0 - 0.7 * w) * (0.6 + 0.4 * step(0.5, frac(c / hz * (1.0 + 4.0 * w))));
                        clip(seam - 0.9 * w * w);
                    }
                }
                else if (c >= dusk && c < night)
                {
                    // Growing from both ends to the middle, not yet there.
                    grown = (c - dusk) / (night - dusk);
                    alpha = 0.25;
                }
                else if (c >= down && c < down + 2.0 * hz)
                {
                    // Withered: gone in two seconds.
                    grown = 1.0;
                    alpha = 0.5 * (1.0 - (c - down) / (2.0 * hz));
                }
                else
                {
                    grown = 0.0;
                    alpha = 0.0;
                }
                clip(grown - i.uv1.x - 1e-3);
                clip(alpha - 1e-3);
                float3 n = normalize(i.normalWS);
                Light main = GetMainLight();
                // Lit, and a little of its own color through (it glows faintly from within).
                half3 lit = _Color.rgb * (main.color * saturate(dot(n, main.direction)) + SampleSH(n) + 0.3);
                // Veins of light through it, brightest at its growing edge.
                float vein = frac(i.positionWS.x * 0.11 + i.positionWS.z * 0.07 + i.positionWS.y * 0.23);
                half edgeGlow = smoothstep(0.06, 0.0, grown - i.uv1.x) * (grown < 1.0 ? 3.0 : 0.0);
                lit += _Glow.rgb * glow * (smoothstep(0.92, 1.0, vein) * 0.8 + 0.12 + edgeGlow);
                return half4(MixFog(lit, i.fog), alpha);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Mode < 0.5)
                    return Vent(i);
                if (_Mode < 1.5)
                    return Sand(i);
                return Coral(i);
            }
            ENDHLSL
        }
    }
}
