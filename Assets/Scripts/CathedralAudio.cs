using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Hosts <see cref="CathedralDsp"/> on an AudioSource and exposes its sample clock as the
    /// master beat clock, so visuals lock to what is actually being rendered rather than to
    /// frame time.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DefaultExecutionOrder(-200)]
    public class CathedralAudio : MonoBehaviour
    {
        [Tooltip("Seconds between a sample being rendered and reaching the ear (buffer + output latency).")]
        [SerializeField] float outputLatency = 0.06f;

        public CathedralDsp Dsp { get; private set; }
        public static CathedralAudio Instance { get; private set; }

        double blockDspTime; long blockSample; bool running;

        void Awake()
        {
            Instance = this;
            int rate = AudioSettings.outputSampleRate;
            Dsp = new CathedralDsp(rate);
            var src = GetComponent<AudioSource>();
            // A silent looping clip keeps OnAudioFilterRead running on this source.
            src.clip = AudioClip.Create("CathedralCarrier", 1024, 1, rate, false);
            src.clip.SetData(new float[1024], 0);
            src.loop = true; src.spatialBlend = 0f; src.playOnAwake = false; src.volume = 1f;
            src.Play();
        }

        void OnDestroy() { if (Instance == this) Instance = null; AudioSettings.OnAudioConfigurationChanged -= OnAudioConfig; }

        void OnEnable() { AudioSettings.OnAudioConfigurationChanged += OnAudioConfig; }

        // A device or routing change resets Unity's audio system and stops every source.
        void OnAudioConfig(bool deviceChanged)
        {
            Debug.Log($"[Cathedral] audio configuration changed (device {deviceChanged}), restarting synth");
            Restart();
        }

        void Restart()
        {
            var src = GetComponent<AudioSource>();
            int rate = AudioSettings.outputSampleRate;
            if (Dsp == null || Dsp.Rate != rate) Dsp = new CathedralDsp(rate);
            src.clip = AudioClip.Create("CathedralCarrier", 1024, 1, rate, false);
            src.clip.SetData(new float[1024], 0);
            src.loop = true;
            src.Play();
            stalled = 0f;
        }

        float stalled;

        void Update()
        {
            // Watchdog: if nothing has pulled audio from the synth for a while, start it again.
            bool pulled = running && AudioSettings.dspTime - blockDspTime < 0.5;
            stalled = pulled ? 0f : stalled + Time.unscaledDeltaTime;
            if (stalled > 1.5f)
            {
                Debug.Log("[Cathedral] synth not being pulled, restarting audio source");
                Restart();
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var dsp = Dsp;
            if (dsp == null) return;
            blockDspTime = AudioSettings.dspTime;
            blockSample = dsp.SamplePos;
            running = true;
            dsp.Render(data, channels);
        }

        /// <summary>Beat position of the sound the player is hearing right now.</summary>
        public double BeatPosition
        {
            get
            {
                if (Dsp == null) return 0.0;
                if (!running) return 0.0;
                double sample = blockSample + (AudioSettings.dspTime - blockDspTime) * Dsp.Rate - outputLatency * Dsp.Rate;
                return System.Math.Max(0.0, sample / Dsp.SamplesPerBeat);
            }
        }
    }
}
