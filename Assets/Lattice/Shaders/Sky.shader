// Lattice: the sky. The horizon's pale haze falling off to a deep zenith (each
// color by its own rate: red and green faster than blue, so the sky deepens to
// blue as it rises), a broad bluish glow and a tight glare around the sun, the
// sun's disk, and a layer of fair-weather
// cumulus 1.8 km over the battlefield drifting on the wind (World/CloudShadows.cs
// moves it and casts its shadows): where the view meets the layer, its density
// (Clouds.hlsl: the cloud noise from Tools/Blender/clouds.py, cut to the cover,
// edges eaten by finer cells); lit from the sun's side (thinner toward it,
// brighter), silver at the edges near the sun, fading into the haze toward the
// horizon. Colors are HDR, before the camera's tonemapping.
Shader "Lattice/Sky"
{
    Properties
    {
        [HDR] _Zenith("Zenith", Color) = (0.02, 0.06, 0.18, 1)
        [HDR] _Horizon("Horizon", Color) = (0.75, 1.08, 1.35, 1)
        _Falloff("Falloff from the Horizon (degrees, per color)", Vector) = (12.5, 12.2, 15, 0)
        [HDR] _Ground("Below the Horizon", Color) = (0.17, 0.18, 0.17, 1)
        [HDR] _Glow("Glow Around the Sun", Color) = (0.18, 0.22, 0.29, 1)
        _SunGlow("Sun Glow (glow degrees, glare, glare degrees)", Vector) = (37, 0.25, 2.5, 0)
        _SunDisk("Sun Disk (radius degrees, brightness)", Vector) = (1.0, 28, 0, 0)
        [NoScaleOffset] _CloudTex("Cloud Noise", 2D) = "black" {}
        _CloudCover("Cloud Cover", Range(0, 1)) = 0.46
        _CloudScale("Cloud Scale (m per tile)", Float) = 7000
        _CloudHeight("Cloud Height (m, world)", Float) = 1900
        _CloudWind("Cloud Wind (x, z m/s)", Vector) = (7, 2.5, 0, 0)
        [HDR] _CloudLit("Cloud Lit", Color) = (1.5, 1.45, 1.38, 1)
        [HDR] _CloudShade("Cloud Shade", Color) = (0.34, 0.39, 0.47, 1)
        _CloudOpacity("Cloud Opacity", Range(0, 1)) = 0.95
        // Kiln's day (K4, World/DayCycle.cs): the sun's own direction and color
        // (at night the main light is the ring's), the ring, the stars.
        _Cycle("Driven by the Day", Float) = 0
        _SunDir("Sun Direction", Vector) = (0, 1, 0, 0)
        [HDR] _SunColor("Sun Color", Color) = (0, 0, 0, 1)
        [HDR] _RingLight("The Ring's Light (on the clouds)", Color) = (0, 0, 0, 1)
        [HDR] _RingColor("The Ring", Color) = (0, 0, 0, 1)
        _RingShape("The Ring (plane normal xyz, half-width)", Vector) = (0, 0.643, 0.766, 0.03)
        _Stars("Stars", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            #define CLOUD_SAMPLE(uv) SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv)
            #include "Clouds.hlsl"

            // How far the wind has carried the clouds (m), from CloudShadows.
            float4 _CloudDrift;

            CBUFFER_START(UnityPerMaterial)
            half4 _Zenith, _Horizon, _Ground, _Glow, _CloudLit, _CloudShade;
            float4 _Falloff, _SunGlow, _SunDisk, _CloudWind;
            half _CloudCover, _CloudOpacity;
            float _CloudScale, _CloudHeight;
            float _Cycle, _Stars;
            float4 _SunDir, _RingShape;
            half4 _SunColor, _RingLight, _RingColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 v = normalize(i.dir);
                // Kiln's day hands the sun its own (the main light is the ring's at night).
                float3 sun = _Cycle > 0.5 ? normalize(_SunDir.xyz) : _MainLightPosition.xyz;
                half3 sunColor = _Cycle > 0.5 ? _SunColor.rgb : _MainLightColor.rgb;
                float e = v.y;
                float cs = dot(v, sun);

                // The sky: the horizon's haze falling off to the zenith, brighter toward the sun.
                float elevation = degrees(asin(saturate(e)));
                half3 sky = _Zenith.rgb + (_Horizon.rgb - _Zenith.rgb) * exp(-elevation / _Falloff.xyz);
                float fromSun = degrees(acos(clamp(cs, -1.0, 1.0)));
                sky += _Glow.rgb * exp(-fromSun / _SunGlow.x) + sunColor * (_SunGlow.y * exp(-fromSun / _SunGlow.z));
                // Past the map's edge, below the horizon: the ground's haze.
                sky = lerp(sky, _Ground.rgb, smoothstep(0.0, -0.08, e));

                // The ring (Tessel's): a band across the sky from the east to the west
                // horizon, its lanes and a gap; pale by day, lit at night.
                {
                    float x = dot(v, _RingShape.xyz) / _RingShape.w;
                    half band = smoothstep(1.0, 0.8, abs(x)) * (0.6 + 0.4 * sin(x * 19.0 + 1.3));
                    band *= 1.0 - 0.85 * smoothstep(0.07, 0.0, abs(x - 0.35));
                    band *= smoothstep(-0.01, 0.05, e);
                    sky += _RingColor.rgb * band;
                }
                // Stars, where the night's dark enough.
                if (_Stars > 0.0 && e > 0.0)
                {
                    float3 g = v * 230.0;
                    float h = frac(sin(dot(floor(g), float3(12.9898, 78.233, 45.164))) * 43758.5453);
                    half star = step(0.9965, h) * smoothstep(0.32, 0.0, length(frac(g) - 0.5)) * (h - 0.9965) * 300.0;
                    sky += star * _Stars * smoothstep(0.0, 0.12, e);
                }

                half alpha = 0.0;
                if (e > 0.004)
                {
                    // Where the view meets the cloud layer (world m).
                    float above = max(_CloudHeight - _WorldSpaceCameraPos.y, 50.0);
                    float2 at = _WorldSpaceCameraPos.xz + v.xz * (above / e);
                    half d = CloudDensity(at, _CloudDrift.xy, _CloudCover, _CloudScale);
                    // Lit from the sun's side: thinner toward the sun, brighter.
                    float2 toSun = normalize(sun.xz + 1e-4) * 420.0;
                    half shade = saturate(CloudShape((at + toSun + _CloudDrift.xy) / _CloudScale, _CloudCover) * 1.4);
                    half lit = exp(-shade * 2.2);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, lit) * ((sunColor + _RingLight.rgb) / 2.2);
                    // Silver where thin edges stand near the sun.
                    cloud += sunColor * pow(saturate(cs), 10.0) * (1.0 - d) * 0.5;
                    // Far off, toward the horizon, the haze takes them.
                    half far = 1.0 - smoothstep(0.03, 0.4, e);
                    cloud = lerp(cloud, sky, far * 0.75);
                    alpha = d * smoothstep(0.015, 0.14, e) * _CloudOpacity;
                    sky = lerp(sky, cloud, alpha);
                }

                // The sun's disk (behind the clouds).
                half r = radians(_SunDisk.x);
                half disk = smoothstep(cos(r), cos(r * 0.8), cs);
                sky += sunColor * (disk * _SunDisk.y * (1.0 - alpha) * smoothstep(-0.02, 0.01, e));
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
}
