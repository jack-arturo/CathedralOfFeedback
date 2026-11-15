using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The feedback you can play with. Everything the orb does is recorded against the beat
    /// clock and replayed by ghost orbs one, two, three and four bars later: same path, same
    /// notes, each pass quieter and dimmer, like a tape delay for gestures. Play a phrase and
    /// you are in a canon with yourself. Fixed-size ring buffer, no allocations after Start.
    /// </summary>
    [RequireComponent(typeof(GrabbableOrb))]
    [DefaultExecutionOrder(10)]
    public class TrailEcho : MonoBehaviour
    {
        [SerializeField] Material ghostMaterial;
        [SerializeField, Range(1, 4)] int echoCount = 4;
        [Tooltip("Delay between echoes, in beats.")]
        [SerializeField] float echoBeats = 4f;
        [SerializeField] float ghostSize = 0.085f;
        [SerializeField, Range(0, 1)] float feedback = 0.64f;

        struct Frame
        {
            public double beat; public Vector3 pos; public float freq, bright;
            public int note; public bool gate, fast;
        }

        const int Capacity = 2048;                    // ~17 s at 120 Hz, echoes need 8 s
        static readonly int ColorId = Shader.PropertyToID("_Color");
        readonly Frame[] frames = new Frame[Capacity];
        long written;
        long[] cursor;
        Transform[] ghosts;
        Renderer[] ghostRenderers;
        MaterialPropertyBlock mpb;
        GrabbableOrb orb;

        void Start()
        {
            orb = GetComponent<GrabbableOrb>();
            mpb = new MaterialPropertyBlock();
            ghosts = new Transform[echoCount]; ghostRenderers = new Renderer[echoCount]; cursor = new long[echoCount];
            for (int i = 0; i < echoCount; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = $"{name}Echo{i + 1}";
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = ghostMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                g.SetActive(false);
                ghosts[i] = g.transform; ghostRenderers[i] = r;
            }
        }

        void Update()
        {
            var an = SpectrumAnalyzer.Instance;
            if (an == null) return;
            double now = an.BeatPosition;

            frames[written % Capacity] = new Frame
            {
                beat = now, pos = transform.position, freq = orb.Frequency, bright = orb.Brightness,
                note = orb.NoteIndex, gate = orb.IsHeld, fast = orb.Fast,
            };
            written++;

            var audioHost = CathedralAudio.Instance;
            var dsp = audioHost != null ? audioHost.Dsp : null;
            var field = FeedbackField.Instance;
            float pulse = an.BeatPulse;

            for (int i = 0; i < echoCount; i++)
            {
                double target = now - echoBeats * (i + 1);
                long oldest = System.Math.Max(0, written - Capacity);
                if (cursor[i] < oldest) cursor[i] = oldest;
                while (cursor[i] + 1 < written && frames[(cursor[i] + 1) % Capacity].beat <= target) cursor[i]++;
                Frame f = frames[cursor[i] % Capacity];
                bool live = f.gate && f.beat <= target && target - f.beat < 0.5;
                float level = Mathf.Pow(feedback, i + 1);

                if (dsp != null)
                {
                    dsp.Voices[orb.orbIndex * GrabbableOrb.VoicesPerOrb + 1 + i] = new CathedralDsp.VoiceParams
                    {
                        Freq = f.freq, Gain = live ? orb.liveGain * level : 0f, Bright = f.bright * level,
                        Pan = GrabbableOrb.PanFor(f.pos), Gate = live, Fast = f.fast,
                    };
                }

                var g = ghosts[i];
                if (g.gameObject.activeSelf != live) g.gameObject.SetActive(live);
                if (!live) continue;
                g.position = f.pos;
                g.localScale = Vector3.one * ghostSize * (0.75f + pulse * 0.5f) * (1f - i * 0.12f);
                Color c = GrabbableOrb.ColorForNote(f.note, orb.baseHue);
                c.a = 0.35f + level;
                mpb.SetColor(ColorId, c);
                ghostRenderers[i].SetPropertyBlock(mpb);
                if (field != null) field.AddSplat(f.pos, c, 0.5f + level, 0.04f);
            }
        }

        void OnDestroy()
        {
            if (ghosts == null) return;
            foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
        }
    }
}
