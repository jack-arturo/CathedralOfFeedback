// Sparks: small additive particles with a hot core, tinted by the particle colour. Drawn as
// stretched billboards so a fast spark reads as a streak.
Shader "Cathedral/Spark"
{
    Properties { _Gain ("Gain", Float) = 2.4 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            half _Gain;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half3 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color.rgb * v.color.a * _Gain;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half a = saturate(1.0 - length(i.uv - 0.5) * 2.0);
                half core = a * a * a * a;
                half lum = dot(i.color, half3(0.33, 0.34, 0.33));
                return half4(i.color * a * a + core * lum * 0.8, 0);
            }
            ENDCG
        }
    }
}
