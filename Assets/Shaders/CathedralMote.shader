// Dust motes hanging in the air: tiny round additive billboards that pick up the colour of
// the glass behind them (one blurred field lookup per vertex). They are the nearest things
// in the scene, so they give the eyes parallax and make the space read as a volume of air.
Shader "Cathedral/Mote"
{
    Properties { _Gain ("Gain", Float) = 1.4 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "CathedralCommon.cginc"

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
                float3 d = normalize(mul(unity_ObjectToWorld, v.vertex).xyz - _CathedralCenter.xyz);
                float2 fuv = float2(atan2(d.z, d.x) * (16.0 / (2.0 * UNITY_PI)), acos(clamp(d.y, -1.0, 1.0)) / _CathedralZones.w);
                half3 light = tex2Dlod(_CathedralField, float4(fuv, 0, 4)).rgb;
                o.color = v.color.rgb * v.color.a * (0.06 + light * _Gain) * (1.0 + _CathedralBeat.x * 0.6) * saturate(_CathedralMix.x * 2.0);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half a = saturate(1.0 - length(i.uv - 0.5) * 2.0);
                return half4(Tonemap(i.color) * a * a, 0);
            }
            ENDCG
        }
    }
}
