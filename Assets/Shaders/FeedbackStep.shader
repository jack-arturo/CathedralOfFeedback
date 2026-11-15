// One step of the feedback loop. Reads last frame's field, moves it (rise + twist + warp),
// lets it echo into itself at double frequency (so the fractal depth comes from time, not
// from per-pixel iterations), then writes the live sources: the spectrum at the foot of the
// wall and whatever the player is touching. Runs once per frame on a 256x1024 target.
Shader "Hidden/Cathedral/FeedbackStep"
{
    Properties { _MainTex ("Previous", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "CathedralCommon.cginc"

            sampler2D _MainTex;
            float4 _Flow;        // x rise per frame (v), y twist per frame (u), z warp amplitude, w decay per frame
            float4 _Source;      // x source gain, y hue rotation per frame (rad), z echo gain, w beat flash
            float4 _SplatPos[16]; // u, v, radius (rad), unused
            float4 _SplatCol[16]; // rgb premultiplied by intensity

            half3 HueRotate(half3 c, float a)
            {
                const half3 k = half3(0.57735, 0.57735, 0.57735);
                float s, co; sincos(a, s, co);
                return c * co + cross(k, c) * s + k * dot(k, c) * (1.0 - co);
            }

            half4 frag(v2f_img i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float thetaMax = _CathedralZones.w;
                float t = _CathedralMix.z;

                // Transport. On the wall the light rises at constant speed (an undistorted
                // spectrogram); on the vault it slows towards the apex and starts to spiral.
                float wall = smoothstep(_CathedralZones.x - 0.10, _CathedralZones.x + 0.02, v);
                float rise = _Flow.x * lerp(0.25 + 1.3 * v, 1.0, wall);
                float twist = _Flow.y * (1.0 - wall) * (1.15 - v);
                float2 s = float2(u + twist, v + rise);
                // Warp must be odd in u across the mirror lines (and even in v's u-dependence)
                // or the kaleidoscope seams would tear.
                s.x += _Flow.z * sin(u * UNITY_PI * 2.0) * sin(v * 17.0 + t * 0.6);
                s.y += _Flow.z * 0.6 * cos(u * UNITY_PI * 2.0) * sin(v * 29.0 - t * 0.9);
                half3 col = tex2Dlod(_MainTex, float4(s, 0, 0)).rgb;

                // Echo: the vault shows the whole field again at twice the angular frequency.
                // Applied every frame, that nests copies inside copies towards the apex.
                float echoMask = smoothstep(0.46, 0.34, v);
                half3 echo = tex2Dlod(_MainTex, float4(u * 2.0, v * 2.15 + 0.012, 0, 0)).rgb;
                col = max(col, HueRotate(echo, 1.3) * _Source.z * echoMask);

                col = HueRotate(col, _Source.y) * _Flow.w;

                // Source 1: the live spectrum, one band per slice of the wedge, at the foot of the wall.
                float base = smoothstep(0.972, 0.992, v);
                float band = BandLerp(u * 15.0);
                half3 bandCol = Jewel(_CathedralMix.w + u * 0.42);
                // With the camera on, the light takes its hue from a slit across the real room.
                half3 slit = RoomSample(float2(0.5 + (u - 0.5) * 0.8, 0.5), 3.0);
                // Only where the room actually has colour: a grey room must not grey out the building.
                half hi = max(slit.r, max(slit.g, slit.b)), lo = min(slit.r, min(slit.g, slit.b));
                half sat = (hi - lo) / max(hi, 0.03);
                slit = max(lerp(hi.xxx, slit, 1.8), 0.0) / max(hi, 0.03);
                bandCol = lerp(bandCol, slit, _CathedralRoomParams.x * 0.6 * smoothstep(0.2, 0.55, sat));
                half3 src = bandCol * (pow(band, 2.1) * _Source.x) + half3(1.0, 0.9, 0.8) * _Source.w;
                col = lerp(col, src, base);

                // Source 2: touches (orbs, echoes, fingertips).
                float sinT = max(sin(v * thetaMax), 0.06);
                const float wedge = UNITY_PI / 8.0;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float pu = _SplatPos[k].x;
                    float du = min(abs(u - pu), min(u + pu, 2.0 - u - pu));
                    float2 d = float2(du * wedge * sinT, (v - _SplatPos[k].y) * thetaMax);
                    float r = max(_SplatPos[k].z, 1e-3);
                    col = max(col, _SplatCol[k].rgb * exp(-dot(d, d) / (r * r)));
                }
                return half4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
}
