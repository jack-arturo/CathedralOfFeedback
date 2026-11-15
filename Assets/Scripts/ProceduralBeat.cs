using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Synthesises a 4-beat loop (kick, hat, snare-ish noise, arpeggio) so the scene reacts to
    /// audio without any imported assets. Assign a real clip to AudioSource to override.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralBeat : MonoBehaviour
    {
        [SerializeField] float bpm = 120f;

        void Start()
        {
            var src = GetComponent<AudioSource>();
            if (src.clip == null)
            {
                src.clip = Build(bpm);
                src.loop = true;
            }
            src.Play();
        }

        static AudioClip Build(float bpm)
        {
            const int rate = 24000; // low rate = small clip, plenty for visuals
            int beatSamples = Mathf.RoundToInt(rate * 60f / bpm);
            int total = beatSamples * 4;
            var data = new float[total];
            var rng = new System.Random(7);
            float[] arp = { 220f, 277.18f, 329.63f, 440f };
            for (int i = 0; i < total; i++)
            {
                float t = (i % beatSamples) / (float)rate;
                int beat = i / beatSamples;
                float s = 0;
                // kick every beat: decaying pitch-swept sine
                s += Mathf.Sin(2 * Mathf.PI * (50f * t + 40f * (1f - Mathf.Exp(-t * 30f)) / 30f * 30f * 0.1f)) * Mathf.Exp(-t * 9f) * 0.8f;
                // hat on off-beats (noise burst)
                float th = ((i + beatSamples / 2) % beatSamples) / (float)rate;
                s += ((float)rng.NextDouble() * 2 - 1) * Mathf.Exp(-th * 60f) * 0.18f;
                // snare on 2 and 4
                if (beat % 2 == 1) s += ((float)rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 18f) * 0.3f;
                // arpeggio
                s += Mathf.Sin(2 * Mathf.PI * arp[beat] * 2f * t) * Mathf.Exp(-t * 4f) * 0.12f;
                data[i] = Mathf.Clamp(s, -1f, 1f);
            }
            var clip = AudioClip.Create("ProceduralBeat", total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
