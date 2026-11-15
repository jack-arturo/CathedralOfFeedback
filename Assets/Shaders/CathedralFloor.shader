// Polished checkerboard floor. The "reflection" is the feedback field looked up along the
// mirrored view ray, so it is per-eye correct and costs one fetch: no reflection camera.
Shader "Cathedral/Floor"
{
    Properties
    {
        _TileSize ("Tile size (m)", Float) = 0.8
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
            float4 _CathedralStep;        // xz of the tile last stepped onto, seconds since, 1 while worn
            float4 _CathedralFeet;        // xz under the player
            half4 _CathedralStepColor;

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

                // Footsteps: the tile under the player is lit, and each step onto a new tile
                // sends a ring across the floor that runs along the grout lines.
                float2 scell = floor(float2(_CathedralStep.x + _CathedralStep.y, _CathedralStep.x - _CathedralStep.y) / _TileSize);
                float2 dc = abs(floor(tg) - scell);
                float onTile = (1.0 - saturate(dc.x)) * (1.0 - saturate(dc.y));
                float age = _CathedralStep.z;
                float sd = length(i.wpos.xz - _CathedralStep.xy);
                float rw = (sd - age * 2.6) / 0.2;
                float ring = exp(-rw * rw) * exp(-age * 1.3);
                float2 fd = i.wpos.xz - _CathedralFeet.xy;
                float pool = exp(-dot(fd, fd) / 1.4);
                col += _CathedralStepColor.rgb * _CathedralStep.w *
                       (onTile * (1.0 - grout) * (0.10 + 0.5 * exp(-age * 2.2))
                        + ring * (0.12 + 0.9 * grout)
                        + pool * grout * 0.12);

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
