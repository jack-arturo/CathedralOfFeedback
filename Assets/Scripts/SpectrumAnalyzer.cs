using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Reads FFT data once per frame, collapses it into 16 log-spaced bands, smooths them,
    /// and uploads them as ONE float4[4] global array plus a metrics vector. Zero allocations
    /// after Awake. Shaders do all per-pixel work; nothing else touches the spectrum.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SpectrumAnalyzer : MonoBehaviour
    {
        public const int BandCount = 16;
        public static SpectrumAnalyzer Instance { get; private set; }

        [SerializeField] AudioSource source;
        [SerializeField] float attack = 25f, release = 6f, gain = 12f;
        [SerializeField] float bpm = 120f;

        const int FftSize = 512;
        static readonly int SpectrumId = Shader.PropertyToID("_CathedralSpectrum");
        static readonly int MetricsId = Shader.PropertyToID("_CathedralMetrics");
        static readonly int LatticeId = Shader.PropertyToID("_CathedralLatticeAngle");

        readonly float[] fft = new float[FftSize];
        readonly float[] bands = new float[BandCount];
        readonly Vector4[] packed = new Vector4[BandCount / 4];
        int[] edges;
        float latticeAngle;

        public float Bass { get; private set; }
        public float Mid { get; private set; }
        public float High { get; private set; }
        public float Bpm => bpm;
        public float BeatInterval => 60f / bpm;
        /// <summary>Continuous beat counter (integer part = beat index).</summary>
        public double BeatPosition { get; private set; }
        public float BeatPhase => (float)(BeatPosition - System.Math.Floor(BeatPosition));
        public event System.Action<int> OnBeat;
        int lastBeat = -1;

        void Awake()
        {
            Instance = this;
            if (source == null) source = GetComponent<AudioSource>();
            // Log-ish band edges over FFT bins 1..~256 (about 43 Hz .. 11 kHz at 48 kHz).
            edges = new int[BandCount + 1];
            for (int i = 0; i <= BandCount; i++)
                edges[i] = Mathf.Max(i == 0 ? 1 : edges[i - 1] + 1, Mathf.RoundToInt(Mathf.Pow(256f, i / (float)BandCount)));
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (source != null && source.isPlaying)
            {
                source.GetSpectrumData(fft, 0, FFTWindow.BlackmanHarris);
                BeatPosition = source.time * (bpm / 60f);
            }
            else
            {
                BeatPosition = Time.timeAsDouble * (bpm / 60f);
                System.Array.Clear(fft, 0, fft.Length);
            }

            float dt = Time.deltaTime;
            for (int b = 0; b < BandCount; b++)
            {
                float sum = 0; int n = edges[b + 1] - edges[b];
                for (int k = edges[b]; k < edges[b + 1] && k < FftSize; k++) sum += fft[k];
                // sqrt compresses dynamic range so quiet bands are still visible.
                float target = Mathf.Clamp01(Mathf.Sqrt(sum / Mathf.Max(1, n)) * gain * 0.5f);
                float rate = target > bands[b] ? attack : release;
                bands[b] = Mathf.Lerp(bands[b], target, 1f - Mathf.Exp(-rate * dt));
                packed[b >> 2][b & 3] = bands[b];
            }

            Bass = (bands[0] + bands[1] + bands[2] + bands[3]) * 0.25f;
            Mid = (bands[4] + bands[5] + bands[6] + bands[7] + bands[8] + bands[9]) / 6f;
            High = (bands[10] + bands[11] + bands[12] + bands[13] + bands[14] + bands[15]) / 6f;

            // Treble speeds the ceiling spin. Integrating on the CPU keeps motion continuous.
            latticeAngle += (0.1f + High * 4f) * dt;

            Shader.SetGlobalVectorArray(SpectrumId, packed);
            Shader.SetGlobalVector(MetricsId, new Vector4(Bass, Mid, High, BeatPhase));
            Shader.SetGlobalFloat(LatticeId, latticeAngle);

            int beat = (int)System.Math.Floor(BeatPosition);
            if (beat != lastBeat) { lastBeat = beat; OnBeat?.Invoke(beat); }
        }
    }
}
