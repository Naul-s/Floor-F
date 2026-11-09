// Palette texels added on top of the world (wish stars, rain): ZTest Always by default, drawn after every opaque and
// transparent object of the world camera, so it is never hidden by the city or the hotel. _ZTest = LEqual (4) makes a
// depth-tested variant (star-night meteors: in front of the sky and the far towers, behind the hotel and the near row). Point-sampled palette (layer units =
// target pixels), colour * _Tint added to the target. WebGL2 / GLES3 safe (no inline samplers, no derivatives).
Shader "FloorF/PaletteAdditive"
{
    Properties
    {
        _MainTex ("Palette", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PaletteAdditive"
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Tint;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Tint;
                return fixed4(c.rgb, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
