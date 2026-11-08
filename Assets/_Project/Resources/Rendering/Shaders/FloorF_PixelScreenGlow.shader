// The pixel screen (uGUI CanvasRenderer, Screen Space - Camera canvas) with the low-res glow added back:
// _MainTex = the 640x360 world target (point), _GlowTex = PixelGlow's blurred quarter-res bright pass (bilinear).
// The glow is sampled at each LOW-RES pixel's centre, quantised to _Steps levels with a 4x4 ordered (Bayer) dither on
// the low-res grid and screen-blended - so it stays pixel art (chunky dithered halos), no smooth modern bloom.
Shader "FloorF/PixelScreenGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Pixel Target", 2D) = "black" {}
        _GlowTex ("Glow", 2D) = "black" {}
        _GlowStrength ("Strength", Float) = 1.0
        _Steps ("Steps", Float) = 12
        _Res ("Low-res size", Vector) = (640, 360, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend Off

        Pass
        {
            Name "PixelScreenGlow"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _GlowTex;
            float _GlowStrength;
            float _Steps;
            float4 _Res;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = floor(i.uv * _Res.xy);
                p = clamp(p, 0, _Res.xy - 1);
                fixed4 c = tex2D(_MainTex, (p + 0.5) / _Res.xy);
                half3 g = tex2D(_GlowTex, (p + 0.5) / _Res.xy).rgb * _GlowStrength;
                int idx = (int)(fmod(p.y, 4.0)) * 4 + (int)(fmod(p.x, 4.0));
                float d = (Bayer[idx] + 0.5) / 16.0;
                // quantise the INTENSITY (hue kept): one dither threshold per pixel, no coloured speckle
                half m = max(g.r, max(g.g, g.b));
                half q = floor(m * _Steps + d) / _Steps;
                g = m > 1e-4 ? g * (q / m) : 0;
                c.rgb = 1 - (1 - c.rgb) * (1 - saturate(g));
                c.a = 1;
                return c * i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
