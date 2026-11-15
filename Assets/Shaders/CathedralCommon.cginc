// Shared globals for every Cathedral shader. All values are pushed once per frame from C#
// (SpectrumAnalyzer, CathedralDirector, FeedbackField, CathedralArchitect).
#ifndef CATHEDRAL_COMMON_INCLUDED
#define CATHEDRAL_COMMON_INCLUDED

// The feedback field: u = azimuth inside one of 16 kaleidoscope wedges (mirror-wrapped),
// v = polar angle from the apex (0) down to the foot of the wall (1).
sampler2D _CathedralField;
float4 _CathedralSpectrum[4];   // 16 smoothed bands, 0..1
float4 _CathedralMetrics;       // x bass, y mid, z high, w beat phase (0..1)
float4 _CathedralBeat;          // x pulse (1 on the beat, decays), y bar phase, z energy, w beats (float)
float4 _CathedralZones;         // x springing line v, y mid ring v, z top ring v, w thetaMax (rad)
float4 _CathedralCenter;        // xyz field centre (world), w exposure
float4 _CathedralMix;           // x reveal 0..1, y passthrough 0..1, z time (s), w hue offset
float4 _CathedralView;          // x windows show the real room behind the glass 0..1
float4 _CathedralPlayhead;      // x amount 0..1 (notes are planted), y step position 0..16 (one bay per sixteenth)
float4 _CathedralHit;           // xyz where a thrown orb last struck, w strength (decays)

// Live passthrough camera frame (black when unavailable).
sampler2D _CathedralRoom;
float4 _CathedralRoomParams;    // x shown 0..1, y needs sRGB decode, z flip y, w strip width

half3 RoomSample(float2 uv, float lod)
{
    uv.y = lerp(uv.y, 1.0 - uv.y, _CathedralRoomParams.z);
    half3 c = tex2Dlod(_CathedralRoom, float4(uv, 0, lod)).rgb;
    return lerp(c, c * c * (c * 0.305 + 0.682) + c * 0.0125, _CathedralRoomParams.y);
}

// Exposure for the room picture: a dark room (a hackerspace at night) still has to read as
// glass, so scale by the frame's average brightness, taken from its smallest mip.
half RoomGain()
{
    half avg = dot(RoomSample(float2(0.5, 0.5), 9.0), half3(0.3, 0.5, 0.2));
    return clamp(0.32 / max(avg, 0.02), 1.0, 8.0);
}

half3 RoomGlass(float2 uv, float lod, half gain)
{
    half3 c = RoomSample(uv, lod) * gain;
    half g = dot(c, half3(0.3, 0.5, 0.2));
    return max(lerp(g.xxx, c, 1.7), 0.0);          // stained glass is more saturated than a camera
}

float Band(int i) { return _CathedralSpectrum[i >> 2][i & 3]; }

float BandLerp(float x)
{
    x = clamp(x, 0.0, 15.0);
    int i = (int)floor(x);
    int j = min(i + 1, 15);
    return lerp(Band(i), Band(j), x - i);
}

half3 HueToRgb(half h)
{
    half3 k = half3(0.0, 2.0 / 3.0, 1.0 / 3.0);
    return saturate(abs(frac(h + k) * 6.0 - 3.0) - 1.0);
}

// Stained-glass palette: sapphire, amethyst, magenta, ruby, amber, emerald. A plain hue wheel
// spends a third of its time in yellow-green, which reads as LED strip rather than glass.
half3 Jewel(half t)
{
    const half3 c0 = half3(0.04, 0.20, 1.00);
    const half3 c1 = half3(0.45, 0.08, 1.00);
    const half3 c2 = half3(1.00, 0.06, 0.55);
    const half3 c3 = half3(1.00, 0.09, 0.08);
    const half3 c4 = half3(1.00, 0.60, 0.05);
    const half3 c5 = half3(0.00, 0.80, 0.62);
    t = frac(t) * 6.0;
    half3 a = t < 1.0 ? c0 : t < 2.0 ? c1 : t < 3.0 ? c2 : t < 4.0 ? c3 : t < 5.0 ? c4 : c5;
    half3 b = t < 1.0 ? c1 : t < 2.0 ? c2 : t < 3.0 ? c3 : t < 4.0 ? c4 : t < 5.0 ? c5 : c0;
    return lerp(a, b, smoothstep(0.0, 1.0, frac(t)));
}

half3 Tonemap(half3 c) { return 1.0 - exp(-c * _CathedralCenter.w); }

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif
