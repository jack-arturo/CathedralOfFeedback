// Ribs, rings, columns and the lantern. Dark stone lit only by the light spilling out of the
// glass behind it (a heavily blurred lookup into the same feedback field), plus a pulse that
// runs up every rib once per beat. uv = field coords of the vertex, uv2 = (along, emissive).
Shader "Cathedral/Stone"
{
    Properties
    {
        _Tint ("Stone tint", Color) = (0.85, 0.9, 1.0, 1)
        _Glow ("Lantern colour", Color) = (1.0, 0.75, 0.4, 1)
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

            half4 _Tint, _Glow;

            struct appdata
            {
                float4 vertex : POSITION; float3 normal : NORMAL;
                float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1;
                float3 n : TEXCOORD2; float3 view : TEXCOORD3; float3 wpos : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; o.uv2 = v.uv2;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.view = _WorldSpaceCameraPos - o.wpos;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 V = normalize(i.view);
                float3 n = normalize(i.n);
                float ndv = abs(dot(n, V));                 // double sided
                float rim = pow(1.0 - ndv, 2.5);

                half3 light = tex2Dlod(_CathedralField, float4(i.uv, 0, 5)).rgb;
                half3 col = half3(0.005, 0.006, 0.010) + light * _Tint.rgb * (0.02 + 0.55 * rim);

                // One pulse per beat, travelling from the floor to the apex.
                float phase = _CathedralMetrics.w;
                float front = exp(-abs(i.uv2.x - phase) * 16.0) * (1.0 - phase);
                col += half3(0.55, 0.75, 1.0) * front * (0.15 + _CathedralMetrics.x * 0.7) * (0.3 + rim);

                col += _Glow.rgb * i.uv2.y * (0.5 + 1.6 * _CathedralBeat.x);

                // A struck column rings with light around the point of impact.
                float3 dh = i.wpos - _CathedralHit.xyz;
                col += half3(1.0, 0.85, 0.6) * _CathedralHit.w * exp(-dot(dh, dh) * 0.35) * (0.5 + rim * 2.0);

                // Columns remember being struck: each stays lit in its own colour (trunk, branches
                // and its share of the lantern) until all eight are, and then the vault blooms.
                float cidx = floor(frac(atan2(i.wpos.z, i.wpos.x) / (2.0 * UNITY_PI) + 1.0) * 8.0);
                float clit = dot(saturate(1.0 - abs(cidx - float4(0, 1, 2, 3))), _CathedralColsA)
                           + dot(saturate(1.0 - abs(cidx - float4(4, 5, 6, 7))), _CathedralColsB);
                float canopy = 1.0 - smoothstep(5.3, 6.2, length(i.wpos.xz));          // not the wall ribs
                col += HueToRgb(cidx / 8.0) * clit * canopy * (0.16 + 0.6 * rim + 0.3 * _CathedralBeat.x);
                col += half3(1.0, 0.82, 0.5) * _CathedralBloom * (0.3 + 1.0 * rim);

                float reveal = smoothstep(0.0, 0.3, _CathedralMix.x * 1.3 - i.uv2.x * 0.6);
                col = Tonemap(col) * reveal;
                // Passthrough: stone thins to a rim-lit outline over the real room.
                half lum = dot(col, half3(0.3, 0.5, 0.2));
                half alpha = lerp(1.0, saturate(lum * 3.0 + 0.12 + i.uv2.y), _CathedralMix.y);
                return half4(col, alpha);
            }
            ENDCG
        }
    }
}
