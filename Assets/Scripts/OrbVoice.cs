using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Makes the orb a playable instrument. While held: height picks a pentatonic note,
    /// hand speed opens the brightness and volume, and the sound comes from the orb itself
    /// (3D). Height also sweeps a low-pass filter on the beat so the whole room responds.
    /// A dotted-eighth echo delay ties the synth to the ghost trails.
    /// </summary>
    [RequireComponent(typeof(AudioSource), typeof(GrabbableOrb))]
    public class OrbVoice : MonoBehaviour
    {
        [SerializeField] float minHeight = 0.4f, maxHeight = 2.2f;
        [SerializeField] AudioLowPassFilter beatFilter;

        static readonly int[] Scale = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22 }; // A minor pentatonic, 2 oct
        const float RootHz = 110f;
        const int Rate = 48000;

        GrabbableOrb orb;
        Vector3 lastPos;
        float speed, smoothSpeed;
        volatile float targetFreq = 220f, targetGain, targetBright = 0.1f;
        float freq = 220f, gain, bright = 0.1f, phase, lp;
        float[] delay; int dpos;

        void Awake()
        {
            orb = GetComponent<GrabbableOrb>();
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("OrbCarrier", 1024, 1, Rate, false);
            src.clip.SetData(new float[1024], 0);
            src.loop = true; src.spatialBlend = 1f; src.minDistance = 1f; src.maxDistance = 15f;
            src.playOnAwake = false;
            delay = new float[Mathf.RoundToInt(Rate * 0.375f) * 2]; // stereo-interleaved headroom
            src.Play();
            lastPos = transform.position;
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            speed = (transform.position - lastPos).magnitude / dt;
            lastPos = transform.position;
            smoothSpeed = Mathf.Lerp(smoothSpeed, speed, 0.2f);

            if (orb.IsHeld)
            {
                float h = Mathf.InverseLerp(minHeight, maxHeight, transform.position.y);
                int idx = Mathf.Clamp(Mathf.RoundToInt(h * (Scale.Length - 1)), 0, Scale.Length - 1);
                targetFreq = RootHz * Mathf.Pow(2f, Scale[idx] / 12f);
                targetGain = Mathf.Clamp01(0.25f + smoothSpeed * 0.3f) * 0.35f;
                targetBright = Mathf.Clamp01(0.08f + smoothSpeed * 0.25f);
                if (beatFilter != null) beatFilter.cutoffFrequency = Mathf.Lerp(500f, 16000f, h);
            }
            else
            {
                targetGain = 0f;
                if (beatFilter != null)
                    beatFilter.cutoffFrequency = Mathf.Lerp(beatFilter.cutoffFrequency, 22000f, 4f * dt);
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float tf = targetFreq, tg = targetGain, tb = targetBright;
            for (int i = 0; i < data.Length; i += channels)
            {
                freq += (tf - freq) * 0.0015f;      // glide between notes
                gain += (tg - gain) * 0.0008f;      // soft attack/release
                bright += (tb - bright) * 0.001f;
                phase += freq / Rate; if (phase > 1f) phase -= 1f;
                float saw = phase * 2f - 1f;
                float tri = Mathf.Abs(phase * 4f - 2f) - 1f;
                float raw = Mathf.Lerp(tri, saw, bright);
                lp += (raw - lp) * Mathf.Lerp(0.02f, 0.5f, bright);
                float dry = lp * gain;
                float wet = delay[dpos];
                delay[dpos] = dry + wet * 0.45f;
                if (++dpos >= delay.Length / 2) dpos = 0;
                float s = dry + wet * 0.6f;
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
