// Additive energy ball for orbs, their halos, echo ghosts and hands. _Rim = 0 gives a bright
// core that fades to the silhouette; _Rim = 1 gives a hollow fresnel shell. A depth prepass
// keeps overlapping fingers from stacking up when it is used on the hand mesh.
Shader "Cathedral/Orb"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.6, 0.2, 1)
        _Power ("Falloff", Float) = 2
        _Rim ("Rim (0 core, 1 shell)", Range(0, 1)) = 0
        _Hot ("White-hot centre", Range(0, 2)) = 0.5
        [Toggle] _Prepass ("Depth prepass (hands)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "DEPTH"
            ZWrite On ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            half _Prepass;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // Without the prepass, push the vertex behind the far plane so nothing is written.
                if (_Prepass < 0.5) o.pos = float4(0, 0, -2, 1);
                return o;
            }
            half4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Name "GLOW"
            Blend One One ZWrite Off ZTest LEqual Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            half4 _Color; half _Power, _Rim, _Hot;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 view : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.view = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                half ndv = saturate(dot(normalize(i.n), normalize(i.view)));
                half a = lerp(pow(ndv, _Power), pow(1.0 - ndv, _Power), _Rim);
                half3 col = _Color.rgb * a + pow(ndv, 10.0) * _Hot * (1.0 - _Rim);
                return half4(col * _Color.a, 0);
            }
            ENDCG
        }
    }
}
