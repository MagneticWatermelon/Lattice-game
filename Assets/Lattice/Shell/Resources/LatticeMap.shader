// The continent map (U4, GDD §9.2): its picture (MapTexture), tinted by
// region in its holder's color (the sites' Voronoi cells, worked out per
// pixel from their centers, so borders stay sharp at any zoom), with a
// line where two holders' regions meet (a front) and a fainter one between
// a holder's own; the 1 km grid; and a darkened rim. A uGUI shader: it
// takes the canvas's clipping (RectMask2D) and stencil like UI/Default.
Shader "Lattice/Map"
{
    Properties
    {
        [PerRendererData] _MainTex ("Map", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RegionTint ("Region tint", Range(0, 1)) = 0.13
        _GridAlpha ("Grid", Range(0, 1)) = 0.12

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 world : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _RegionTint, _GridAlpha;
            // Each site: its center (0-1 across the map), its holder (0-2; 3 no
            // one), and 1 if it's lit (selected, or a spawn of ours).
            float4 _Sites[96];
            float _SiteCount;
            float4 _Inks[4];

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv;
                fixed4 c = tex2D(_MainTex, p);
                // The nearest two sites: whose region this is, and how far its edge is.
                float d1 = 1e9, d2 = 1e9;
                float2 c1 = 0, c2 = 0;
                float o1 = 3, o2 = 3;
                int n = (int)_SiteCount;
                for (int k = 0; k < 96; k++)
                {
                    if (k >= n) break;
                    float4 s = _Sites[k];
                    float2 d = p - s.xy;
                    float dd = dot(d, d);
                    if (dd < d1)
                    {
                        d2 = d1; c2 = c1; o2 = o1;
                        d1 = dd; c1 = s.xy; o1 = s.z;
                    }
                    else if (dd < d2)
                    {
                        d2 = dd; c2 = s.xy; o2 = s.z;
                    }
                }
                // Distance to the bisector between the two (the region's edge), in map units.
                float edge = n > 1 ? (d2 - d1) / (2.0 * max(length(c2 - c1), 1e-6)) : 1;
                float px = max(fwidth(p.x), fwidth(p.y));
                fixed4 ink = _Inks[(int)clamp(o1, 0, 3)];
                c.rgb = lerp(c.rgb, ink.rgb, _RegionTint * (o1 < 2.5 ? 1 : 0.4));
                // A front (two holders) bright and wider; the same holder's faint.
                bool front = abs(o1 - o2) > 0.5;
                // (Each side draws its half: a band 2w pixels wide, soft at its edges.)
                float w = front ? 1.5 : 0.75;
                float band = saturate(w + 0.5 - edge / px);
                fixed3 edgeInk = front ? lerp(ink.rgb, fixed3(1, 1, 1), 0.35) : ink.rgb * 0.55;
                c.rgb = lerp(c.rgb, edgeInk, band * (front ? 0.9 : 0.45));
                // The grid: 8 squares a side.
                float2 g = abs(frac(p * 8 + 0.5) - 0.5) / (px * 8);
                float gl = saturate(1 - min(g.x, g.y));
                c.rgb = lerp(c.rgb, fixed3(1, 1, 1), gl * _GridAlpha);
                // A darker rim, so the map ends rather than stops.
                float2 rim = min(p, 1 - p);
                c.rgb *= lerp(0.55, 1, saturate(min(rim.x, rim.y) * 40));
                c *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
