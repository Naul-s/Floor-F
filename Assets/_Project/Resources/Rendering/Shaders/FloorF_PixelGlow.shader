// Low-resolution glow for the 640x360 pixel target (PixelGlow). Two cheap passes at quarter resolution (160x90):
//   0 Prefilter : 4x4 source texels per output texel, each thresholded on its brightest channel (only emissive-bright
//                 pixels - neon, lit windows, lamps, the HOTEL sign - contribute), averaged.
//   1 Blur      : 9 bilinear taps (~5x5 quarter texels).
// The UI composite (FloorF/PixelScreenGlow) adds it back quantised + dithered on the low-res pixel grid.
// WebGL2 / GLES3 safe: plain tex2D, no inline samplers, fixed loops.
Shader "FloorF/PixelGlow"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.72
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Threshold;

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert (appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        half3 Bright(float2 uv)
        {
            half3 c = tex2D(_MainTex, uv).rgb;
            half m = max(c.r, max(c.g, c.b));
            half w = saturate((m - _Threshold) / max(1e-3, 1.0 - _Threshold));
            return c * w;
        }
        ENDCG

        Pass
        {
            Name "Prefilter"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                // output texel = 4x4 source texels; source texel centres at -1.5, -0.5, 0.5, 1.5 around uv
                float2 t = _MainTex_TexelSize.xy;
                half3 s = 0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        s += Bright(i.uv + float2(x - 1.5, y - 1.5) * t);
                return half4(s / 16.0, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "Blur"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy * 1.5;
                half3 s = tex2D(_MainTex, i.uv).rgb * 0.2;
                s += (tex2D(_MainTex, i.uv + float2(t.x, 0)).rgb + tex2D(_MainTex, i.uv - float2(t.x, 0)).rgb
                    + tex2D(_MainTex, i.uv + float2(0, t.y)).rgb + tex2D(_MainTex, i.uv - float2(0, t.y)).rgb) * 0.125;
                s += (tex2D(_MainTex, i.uv + t).rgb + tex2D(_MainTex, i.uv - t).rgb
                    + tex2D(_MainTex, i.uv + float2(t.x, -t.y)).rgb + tex2D(_MainTex, i.uv + float2(-t.x, t.y)).rgb) * 0.075;
                return half4(s, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
