// Ceiling lattice. Rotation angle is accumulated on the CPU (_CathedralLatticeAngle) so
// treble speeds the spin without time discontinuities.
Shader "Cathedral/Lattice"
{
    Properties { _Color ("Color", Color) = (0.4, 0.9, 1, 1) _Density ("Density", Float) = 6 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float4 _CathedralMetrics;
            float _CathedralLatticeAngle;
            half4 _Color; half _Density;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv * 2 - 1; return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float s, c; sincos(_CathedralLatticeAngle, s, c);
                float2 p = float2(c * i.uv.x - s * i.uv.y, s * i.uv.x + c * i.uv.y) * _Density;
                float2 g = abs(frac(p) - 0.5);
                float line1 = smoothstep(0.06, 0.0, min(g.x, g.y));
                float diag = smoothstep(0.05, 0.0, abs(g.x - g.y));
                float lat = saturate(line1 + diag);
                float fade = saturate(1.2 - length(i.uv));
                return half4(_Color.rgb * lat * fade * (0.4 + _CathedralMetrics.z * 2), 1);
            }
            ENDCG
        }
    }
}
