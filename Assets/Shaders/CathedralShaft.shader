// Shafts of coloured light slanting in from each lancet window. Each shaft is an open prism
// drawn additively; its colour is the feedback field at the window it comes from, sampled per
// vertex, so the beams change colour as the music's light climbs through the glass.
// uv = (along the beam 0..1, unused), uv2 = field coordinates of the window point it starts at.
Shader "Cathedral/Shaft"
{
    Properties { _Gain ("Gain", Float) = 0.3 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" }
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
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float along : TEXCOORD0; half3 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.along = v.uv.x;
                half3 light = tex2Dlod(_CathedralField, float4(v.uv2, 0, 4.5)).rgb;
                half peak = max(light.r, max(light.g, light.b));
                light *= (1.0 - exp(-peak * _CathedralCenter.w)) / max(peak, 1e-4);
                // A face seen edge-on fades out, so the prism reads as haze rather than as a box.
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float facing = abs(dot(normalize(_WorldSpaceCameraPos - wpos), UnityObjectToWorldNormal(v.normal)));
                o.color = light * _Gain * (0.75 + 0.5 * _CathedralBeat.x) * saturate(_CathedralMix.x * 1.5 - 0.4) * (0.15 + 0.85 * facing * facing);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half a = pow(saturate(1.0 - i.along), 1.7);
                return half4(i.color * a, 0);
            }
            ENDCG
        }
    }
}
