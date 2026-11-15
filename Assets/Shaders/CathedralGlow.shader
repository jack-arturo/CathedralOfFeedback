// Unlit additive glow for orb and echo ghosts. _Color alpha scales intensity.
Shader "Cathedral/Glow"
{
    Properties { _Color ("Color", Color) = (1,0.6,0.2,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend One One ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            half4 _Color;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v){ v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.color=v.color; return o; }
            half4 frag(v2f i):SV_Target{ return half4(_Color.rgb*_Color.a*i.color.rgb*i.color.a,1); }
            ENDCG
        }
    }
}
