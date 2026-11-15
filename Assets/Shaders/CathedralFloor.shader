// Polished checkerboard floor. The "reflection" is the feedback field looked up along the
// mirrored view ray, so it is per-eye correct and costs one fetch: no reflection camera.
Shader "Cathedral/Floor"
{
    Properties
    {
        _TileSize ("Tile size (m)", Float) = 1.1
        _Reflect ("Reflectivity", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "CathedralCommon.cginc"

            half _TileSize, _Reflect;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 view : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.view = o.wpos - _WorldSpaceCameraPos;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 I = normalize(i.view);
                float3 R = float3(I.x, abs(I.y), I.z);               // mirror about the floor
                float theta = acos(clamp(R.y, -1.0, 1.0));
                float phi = atan2(R.z, R.x);
                float2 fuv = float2(phi * (16.0 / (2.0 * UNITY_PI)), theta / _CathedralZones.w);
                half3 refl = tex2Dlod(_CathedralField, float4(fuv, 0, 2.5)).rgb;

                // Diamond checkerboard.
                float2 tg = float2(i.wpos.x + i.wpos.z, i.wpos.x - i.wpos.z) / _TileSize;
                float2 tf = abs(frac(tg) - 0.5);
                float2 tw = fwidth(tg);
                float grout = smoothstep(0.5 - 0.03 - max(tw.x, tw.y), 0.5 - 0.03, max(tf.x, tf.y));
                float checker = fmod(abs(floor(tg.x) + floor(tg.y)), 2.0);

                float fres = pow(1.0 - saturate(abs(I.y)), 3.0);
                float k = _Reflect * lerp(0.45, 1.0, checker) * (0.30 + 0.70 * fres);
                half3 col = half3(0.006, 0.007, 0.011) * (1.0 + checker) + refl * k;
                col *= 1.0 - 0.85 * grout;

                float reveal = smoothstep(0.0, 0.3, _CathedralMix.x * 1.6);
                col = Tonemap(col) * reveal;
                half lum = dot(col, half3(0.3, 0.5, 0.2));
                half alpha = lerp(1.0, saturate(lum * 3.0), _CathedralMix.y);
                return half4(col, alpha);
            }
            ENDCG
        }
    }
}
