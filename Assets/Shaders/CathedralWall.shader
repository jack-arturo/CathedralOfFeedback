// Spectrum-driven kaleidoscopic fractal tile. One draw, no textures, no branches on
// per-pixel data: cheap enough for Quest fragment budgets. Spectrum arrives as
// float4[4] (16 bands) set once per frame via Shader.SetGlobalVectorArray.
Shader "Cathedral/FractalWall"
{
    Properties
    {
        _TileScale ("Tile Scale", Float) = 3
        _ColorA ("Color A", Color) = (0.05, 0.1, 0.4, 1)
        _ColorB ("Color B", Color) = (1.0, 0.3, 0.8, 1)
        _Pulse ("Bass Pulse Strength", Range(0,2)) = 1
        _Fold ("Fold Offset", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _CathedralSpectrum[4];   // 16 smoothed FFT bands, 0..1
            float4 _CathedralMetrics;       // x bass, y mid, z high, w beat phase (0..1)
            half4 _ColorA, _ColorB;
            half _TileScale, _Pulse, _Fold;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float bass : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            float Band(int i) { return _CathedralSpectrum[i >> 2][i & 3]; }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Bass breathes the wall slightly along its normal-ish axis (object-space z).
                float bass = _CathedralMetrics.x;
                v.vertex.xyz += float3(0, 0, -1) * bass * 0.02 * _Pulse;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _TileScale;
                o.bass = bass;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 p = frac(i.uv) * 2 - 1;
                float trap = 1e3;
                float energy = 0;
                // 4 kaleidoscopic folds; each iteration is driven by a different bass-heavy band.
                [unroll] for (int k = 0; k < 4; k++)
                {
                    p = abs(p) - _Fold;
                    float a = 0.6 + Band(k) * 1.2;
                    float s, c; sincos(a, s, c);
                    p = float2(c * p.x - s * p.y, s * p.x + c * p.y) * 1.35;
                    trap = min(trap, abs(length(p) - 0.5));
                    energy += Band(k * 2);
                }
                float edge = smoothstep(0.12, 0.0, trap);
                float glow = edge * (0.4 + energy * 0.6 * _Pulse + i.bass * _Pulse);
                half3 col = lerp(_ColorA.rgb, _ColorB.rgb, saturate(energy * 0.5 + _CathedralMetrics.w * 0.15));
                return half4(col * (0.15 + glow), 1);
            }
            ENDCG
        }
    }
}
