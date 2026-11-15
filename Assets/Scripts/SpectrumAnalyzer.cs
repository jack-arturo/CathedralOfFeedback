using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Turns the synth's output into what the shaders need: 16 log-spaced bands with per-band
    /// automatic gain (so every band uses its full range whatever the mix is doing), bass/mid/
    /// high, and the beat clock. Uses its own FFT on a tap of the mix instead of the audio
    /// graph, so it behaves identically on device and in the offline preview.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SpectrumAnalyzer : MonoBehaviour
    {
        public const int BandCount = 16;
        public static SpectrumAnalyzer Instance { get; private set; }

        [SerializeField] CathedralAudio audioHost;
        [SerializeField] float attack = 30f, release = 7f;

        const int N = 1024;
        static readonly int SpectrumId = Shader.PropertyToID("_CathedralSpectrum");
        static readonly int MetricsId = Shader.PropertyToID("_CathedralMetrics");
        static readonly int BeatId = Shader.PropertyToID("_CathedralBeat");

        readonly float[] re = new float[N], im = new float[N], window = new float[N];
        readonly float[] bands = new float[BandCount], peaks = new float[BandCount];
        readonly Vector4[] packed = new Vector4[BandCount / 4];
        int[] edges;
        CathedralDsp dsp;

        public float Bass { get; private set; }
        public float Mid { get; private set; }
        public float High { get; private set; }
        public float BeatPulse { get; private set; }
        /// <summary>RMS of the synth's output over the last ~20 ms. Zero means the engine is silent.</summary>
        public float Level { get; private set; }
        public float Energy { get; set; }
        public float Bpm => dsp != null ? dsp.Bpm : 120f;
        public float BeatInterval => 60f / Bpm;
        public double BeatPosition { get; private set; }
        public float BeatPhase => (float)(BeatPosition - System.Math.Floor(BeatPosition));
        public event System.Action<int> OnBeat;
        public float GetBand(int i) => bands[i];
        int lastBeat = -1;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Bind(CathedralDsp d)
        {
            Instance = this;
            dsp = d;
            for (int i = 0; i < N; i++) window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (N - 1));
            // 16 log bands from 55 Hz to 12 kHz, at least one FFT bin each.
            float binHz = d.Rate / (float)N;
            edges = new int[BandCount + 1];
            for (int i = 0; i <= BandCount; i++)
            {
                int bin = Mathf.RoundToInt(55f * Mathf.Pow(12000f / 55f, i / (float)BandCount) / binHz);
                edges[i] = Mathf.Clamp(Mathf.Max(bin, i == 0 ? 1 : edges[i - 1] + 1), 1, N / 2);
            }
            for (int i = 0; i < BandCount; i++) peaks[i] = 0.02f;
        }

        void Update()
        {
            if (dsp == null && audioHost != null && audioHost.Dsp != null) Bind(audioHost.Dsp);
            if (dsp == null) return;
            Tick(Time.deltaTime, audioHost != null ? audioHost.BeatPosition : dsp.BeatAt(dsp.SamplePos));
        }

        public void Tick(float dt, double beatPosition)
        {
            BeatPosition = beatPosition;
            dsp.CopyTap(re);
            float sq = 0f;
            for (int i = 0; i < N; i++) { sq += re[i] * re[i]; re[i] *= window[i]; im[i] = 0f; }
            Level = Mathf.Sqrt(sq / N);
            Fft.Transform(re, im);

            for (int b = 0; b < BandCount; b++)
            {
                float sum = 0f; int n = edges[b + 1] - edges[b];
                for (int k = edges[b]; k < edges[b + 1]; k++) sum += re[k] * re[k] + im[k] * im[k];
                float mag = Mathf.Sqrt(sum / Mathf.Max(1, n)) * (2f / N);
                // Per-band AGC: follow the loudest recent level, never amplify silence.
                peaks[b] = Mathf.Max(peaks[b] * Mathf.Exp(-dt / 10f), mag, 0.004f);
                float target = Mathf.Pow(Mathf.Clamp01(mag / peaks[b]), 1.3f);
                float rate = target > bands[b] ? attack : release;
                bands[b] = Mathf.Lerp(bands[b], target, 1f - Mathf.Exp(-rate * dt));
                packed[b >> 2][b & 3] = bands[b];
            }

            Bass = (bands[0] + bands[1] + bands[2] + bands[3]) * 0.25f;
            Mid = (bands[4] + bands[5] + bands[6] + bands[7] + bands[8] + bands[9]) / 6f;
            High = (bands[10] + bands[11] + bands[12] + bands[13] + bands[14] + bands[15]) / 6f;

            int beat = (int)System.Math.Floor(BeatPosition);
            BeatPulse *= Mathf.Exp(-dt * 6f);
            if (beat != lastBeat) { lastBeat = beat; BeatPulse = 1f; OnBeat?.Invoke(beat); }

            Shader.SetGlobalVectorArray(SpectrumId, packed);
            Shader.SetGlobalVector(MetricsId, new Vector4(Bass, Mid, High, BeatPhase));
            Shader.SetGlobalVector(BeatId, new Vector4(BeatPulse, (float)((BeatPosition / 4.0) % 1.0), Energy, (float)(BeatPosition % 4096.0)));
        }
    }

    /// <summary>In-place radix-2 FFT. Length must be a power of two.</summary>
    public static class Fft
    {
        public static void Transform(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                float ang = -2f * Mathf.PI / len;
                float wr = Mathf.Cos(ang), wi = Mathf.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        float nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }
    }
}
