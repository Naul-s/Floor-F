// Banded pixel lighting for the 640x360 target (PixelGlow). Halos are a few FLAT colour bands (posterised steps, no
// dither, no noise), like hand-made pixel-art light:
//   0 Prefilter : 4x4 target pixels per light texel on a WORLD-anchored grid (_Offset = camera pixel position mod 4, so
//                 the buffer content does not change when the camera pans/shakes by whole pixels). Each pixel adds its
//                 HUE (max channel = 1) times a saturating weight of its brightness: neon/lamps/signs (>= _Strong.y)
//                 count fully whatever their slow hum, lit windows (_Weak range) a little, everything else nothing.
//   1 Blur H, 2 Blur V : 7 bilinear taps = 13-texel gaussian (sigma 3 light texels ~ 12 target pixels).
//   3 Composite : target + light -> lit target. light = gain * blurred buffer (bilinear at the pixel centre) + authored
//                 radial pools (ellipses, up to 24, in target pixels); its max channel is cut into 4 flat bands
//                 (_Bands thresholds -> _Levels) and its hue is screen-blended in gamma space. Pixels below the first
//                 band are returned untouched.
//   4 LightOnly : the banded light alone (tests: band count / no checkerboard).
//   5 Dissolve  : lit target vs a snapshot of the previous zoom rung, ordered 4x4 Bayer threshold (scenic zoom).
// WebGL2 / GLES3 safe: plain tex2D, fixed loops with an early break.
Shader "FloorF/PixelGlow"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _GlowTex ("Light", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _GlowTex;
        float4 _GlowTex_TexelSize;
        float4 _Offset;   // xy = grid offset in target pixels (0..3), zw = light buffer size
        float4 _Weak;     // x,y = gamma brightness range, z = weight
        float4 _Strong;   // x,y = gamma brightness range
        float4 _Bands;    // light thresholds (max channel)
        float4 _Levels;   // band brightness (gamma)
        float _Gain;
        float _Linear;    // 1 = project in linear colour space
        float _PoolCount;
        float4 _PoolRect[24];  // centre xy (target pixels), 1/radius xy
        float4 _PoolColor[24]; // rgb = hue * intensity

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert (appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        half3 Source(float2 uv)
        {
            half3 c = tex2D(_MainTex, uv).rgb;
            half m = max(c.r, max(c.g, c.b));
            if (m <= 1e-4) return 0;
            half g = _Linear > 0.5 ? LinearToGammaSpaceExact(m) : m;
            half w = _Weak.z * smoothstep(_Weak.x, _Weak.y, g) + (1.0 - _Weak.z) * smoothstep(_Strong.x, _Strong.y, g);
            return c * (w / m);
        }

        half3 Blur(float2 uv, float2 dir)
        {
            float2 t = _MainTex_TexelSize.xy * dir;
            half3 s = tex2D(_MainTex, uv).rgb * 0.1370;
            s += (tex2D(_MainTex, uv + t * 1.458).rgb + tex2D(_MainTex, uv - t * 1.458).rgb) * 0.2394;
            s += (tex2D(_MainTex, uv + t * 3.404).rgb + tex2D(_MainTex, uv - t * 3.404).rgb) * 0.1395;
            s += (tex2D(_MainTex, uv + t * 5.352).rgb + tex2D(_MainTex, uv - t * 5.352).rgb) * 0.0526;
            return s;
        }

        // banded light at target pixel p (integer index)
        half3 Light(float2 p)
        {
            float2 lowUv = ((p + 0.5 + _Offset.xy) * 0.25) * _GlowTex_TexelSize.xy;
            half3 l = tex2D(_GlowTex, lowUv).rgb * _Gain;
            float2 pc = p + 0.5;
            for (int k = 0; k < 24; k++)
            {
                if (k >= (int)_PoolCount) break;
                float2 q = (pc - _PoolRect[k].xy) * _PoolRect[k].zw;
                float f = saturate(1.0 - dot(q, q));
                l += _PoolColor[k].rgb * (f * f);
            }
            half m = max(l.r, max(l.g, l.b));
            half v = m >= _Bands.w ? _Levels.w : m >= _Bands.z ? _Levels.z : m >= _Bands.y ? _Levels.y : m >= _Bands.x ? _Levels.x : 0;
            return v > 0 ? l * (v / m) : 0;
        }

        float2 Pixel(float2 uv) { return clamp(floor(uv * _MainTex_TexelSize.zw), 0, _MainTex_TexelSize.zw - 1); }
        ENDCG

        Pass
        {
            Name "Prefilter"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                // light texel (ix, iy) covers target pixels [4 ix - offset, 4 ix - offset + 4)
                float2 cell = floor(i.uv * _Offset.zw);
                float2 origin = cell * 4.0 - _Offset.xy;
                half3 s = 0;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        s += Source((origin + float2(x, y) + 0.5) * _MainTex_TexelSize.xy);
                return half4(s / 16.0, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "BlurH"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Blur(i.uv, float2(1, 0)), 1); }
            ENDCG
        }

        Pass
        {
            Name "BlurV"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Blur(i.uv, float2(0, 1)), 1); }
            ENDCG
        }

        Pass
        {
            Name "Composite"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                float2 p = Pixel(i.uv);
                half4 c = tex2D(_MainTex, (p + 0.5) * _MainTex_TexelSize.xy);
                half3 l = Light(p);
                if (max(l.r, max(l.g, l.b)) <= 0) return half4(c.rgb, 1);
                half3 g = _Linear > 0.5 ? LinearToGammaSpace(c.rgb) : c.rgb;
                g = 1 - (1 - saturate(g)) * (1 - saturate(l));
                return half4(_Linear > 0.5 ? GammaToLinearSpace(g) : g, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "LightOnly"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                half3 l = saturate(Light(Pixel(i.uv)));
                return half4(_Linear > 0.5 ? GammaToLinearSpace(l) : l, 1);
            }
            ENDCG
        }

        Pass
        {
            // 5: ordered pixel dissolve (scenic zoom rungs): new frame where the 4x4 Bayer threshold is below _Mix,
            // the snapshot of the previous rung elsewhere. Whole target pixels only - no blending, no new colours.
            Name "Dissolve"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _PrevTex;
            float _Mix;
            float Bayer2(float2 v) { return 2.0 * abs(v.x - v.y) + v.y; }
            half4 frag (v2f i) : SV_Target
            {
                float2 p = Pixel(i.uv);
                float2 uv = (p + 0.5) * _MainTex_TexelSize.xy;
                float2 lo = fmod(p, 2.0);
                float2 hi = floor(fmod(p, 4.0) * 0.5);
                float t = (4.0 * Bayer2(lo) + Bayer2(hi) + 0.5) / 16.0;
                return t < _Mix ? half4(tex2D(_MainTex, uv).rgb, 1) : half4(tex2D(_PrevTex, uv).rgb, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
