// The shell of the cathedral: leaded diamond panes, each one lit by the feedback field at
// its own centre. Quantising to panes turns a soft 256x1024 light field into crisp stained
// glass, and costs two texture fetches per pixel regardless of how deep the fractal is.
// On the walls the glass sits in one tall lancet window per bay, set in dark stone; the
// vault above is glass from rib to rib.
Shader "Cathedral/Glass"
{
    Properties
    {
        _PaneRows ("Pane rows (floor to apex)", Float) = 26
        _Lead ("Lead width", Range(0, 0.3)) = 0.10
        _Window ("Lancet: apex v, shoulder drop, sill v, half width", Vector) = (0.632, 0.085, 0.952, 0.66)
        _Saturation ("Saturation", Range(0, 2)) = 1.3
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

            half _PaneRows, _Lead, _Saturation;
            float4 _Window;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float onWall = step(_CathedralZones.x, v);

                // Fewer panes per wedge as the vault narrows; the stone rings hide the steps.
                // The windows get twice the pane density of the vault: finer glass, and enough
                // panes for the camera mosaic to read as a picture.
                float K = v > _CathedralZones.x ? 16.0 : (v > _CathedralZones.y ? 4.0 : (v > _CathedralZones.z ? 2.0 : 1.0));
                float rows = _PaneRows * (1.0 + onWall);
                float2 g = float2(u * K, v * rows);
                float2 d = float2(g.x + g.y, g.x - g.y);          // diamond lattice
                float2 id = floor(d);
                float2 f = d - id - 0.5;
                float2 c = id + 0.5;
                float2 cuv = float2((c.x + c.y) * 0.5 / K, (c.x - c.y) * 0.5 / rows);

                float2 fw = fwidth(d);
                float e = 0.5 - max(abs(f.x), abs(f.y));          // distance to the lead, in cells
                float aa = max(fw.x, fw.y) * 1.5 + 1e-4;
                float pane = smoothstep(_Lead * 0.5, _Lead * 0.5 + aa, e);

                float h = Hash21(id + K * 7.13);
                half3 paneCol = tex2Dlod(_CathedralField, float4(cuv, 0, 1)).rgb;
                half3 softCol = tex2D(_CathedralField, float2(u, v)).rgb;
                half3 col = lerp(paneCol, softCol, 0.3) * (0.65 + 0.7 * h);

                // Light is cool on one side of the room and warm on the other, like sunrise and
                // sunset windows. It also gives the kaleidoscope a sense of direction.
                float warm = 0.5 + 0.5 * cos(u * (UNITY_PI / 8.0));
                col *= lerp(half3(0.72, 0.92, 1.18), half3(1.2, 0.95, 0.7), warm);
                half grey = dot(col, half3(0.3, 0.5, 0.2));
                col = max(lerp(grey.xxx, col, _Saturation), 0.0);

                // The room in the glass: each lancet is a leaded mosaic of the passthrough camera,
                // mirrored bay to bay, brightened wherever the music's light is passing through it.
                float mwin = abs(frac(cuv.x * 0.5) * 2.0 - 1.0);
                float2 ruv = float2((mwin - (0.5 - _Window.w * 0.5)) / _Window.w, (_Window.z - cuv.y) / (_Window.z - _Window.x));
                half rgain = RoomGain();
                half3 room = RoomGlass(float2(0.5 + (ruv.x - 0.5) * _CathedralRoomParams.w, lerp(0.04, 0.96, saturate(ruv.y))), 1.0, rgain);
                // A little of the unquantised picture inside each pane keeps faces readable.
                float mpix = abs(frac(u * 0.5) * 2.0 - 1.0);
                float2 puv = float2((mpix - (0.5 - _Window.w * 0.5)) / _Window.w, (_Window.z - v) / (_Window.z - _Window.x));
                room = lerp(room, RoomGlass(float2(0.5 + (puv.x - 0.5) * _CathedralRoomParams.w, lerp(0.04, 0.96, saturate(puv.y))), 0.0, rgain), 0.4);
                half fieldPeak = max(col.r, max(col.g, col.b));
                col = lerp(col, room * (0.8 + 0.4 * h) * (0.5 + 1.3 * fieldPeak) + col * 0.6, _CathedralRoomParams.x * onWall);

                // Night glass: the building stays readable when the music is quiet.
                col += half3(0.008, 0.012, 0.034) * (0.4 + h);
                // Hi-hats scatter sparkles across random panes.
                float tw = step(0.972, frac(h * 61.7 + floor(_CathedralBeat.w * 4.0) * 0.381966));
                col += tw * _CathedralMetrics.z * half3(0.9, 0.95, 1.0) * 1.2;
                // Hand-blown glass is brighter in the middle of the pane.
                col *= 0.72 + 0.55 * smoothstep(0.0, 0.35, e);

                // Sequencer playhead: the bay whose step is sounding lights up, then fades.
                float lit = step(abs(floor(u) - floor(_CathedralPlayhead.y)), 0.5) * (1.0 - 0.8 * frac(_CathedralPlayhead.y));
                col = col * (1.0 + 1.3 * lit * _CathedralPlayhead.x * onWall) + half3(0.05, 0.05, 0.06) * lit * _CathedralPlayhead.x * onWall;

                // Hue-preserving tonemap: bright glass stays coloured instead of bleaching.
                half peak = max(col.r, max(col.g, col.b));
                col *= (1.0 - exp(-peak * _CathedralCenter.w)) / max(peak, 1e-4);

                // The building lights up from the floor on entry.
                float reveal = smoothstep(0.0, 0.25, _CathedralMix.x * 1.25 - (1.0 - v));
                col = col * pane * reveal + half3(0.0025, 0.0025, 0.0035);

                // Lancet windows: pointed arch per bay on the wall, solid stone around it.
                float x = abs(frac(u) - 0.5) * 2.0;
                float2 px = float2(fwidth(u) * 2.0, fwidth(v)) * 1.5 + 1e-5;
                float arch = _Window.x + _Window.y * pow(saturate(x / _Window.w), 1.8);
                float win = smoothstep(0.0, px.x, _Window.w - x) * smoothstep(0.0, px.y, v - arch) * smoothstep(0.0, px.y, _Window.z - v);
                float glass = lerp(1.0, win, onWall);
                half3 spill = tex2Dlod(_CathedralField, float4(u, v, 0, 5)).rgb;
                half3 stone = half3(0.006, 0.007, 0.011) + spill * 0.035 * reveal;
                col = lerp(stone, col, glass);

                // Passthrough: the wall and unlit glass dissolve into the real room; lit glass stays.
                half lum = dot(col, half3(0.3, 0.5, 0.2));
                half aOpen = saturate(lum * 2.6 + (1.0 - glass) * 0.10);
                // "Windows" mode: the real room is what is outside. Panes turn into tinted, see-through
                // glass over stereo passthrough; the leads, the wall and the vault stay solid.
                half aWindow = lerp(1.0, saturate(0.2 + lum * 1.1), pane * glass * onWall);
                half alpha = lerp(lerp(1.0, aWindow, _CathedralView.x), aOpen, _CathedralMix.y);
                return half4(col, alpha);
            }
            ENDCG
        }
    }
}
