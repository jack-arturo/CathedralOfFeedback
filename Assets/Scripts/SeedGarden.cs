using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The room as a step sequencer. The building has 16 bays and a bar has 16 steps: a light
    /// runs round the bays once per bar, and every note the player has left hanging in the air
    /// rings (a glass bell) when the light reaches the bay behind it. Let go of an orb gently to
    /// plant a note where it is; height is its pitch. Poke a note to pop it, clap to clear them
    /// all. Notes are also cleared when the headset comes off, so the next player starts clean.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class SeedGarden : MonoBehaviour
    {
        public static SeedGarden Instance { get; private set; }

        [SerializeField] Material seedMaterial;
        [SerializeField] int capacity = 16;
        [SerializeField] float size = 0.045f;
        [SerializeField] float bellGain = 0.16f;

        struct Seed
        {
            public bool alive, armed;
            public Vector3 pos; public int note, step; public float freq, hue, flash, age;
            public Transform t; public Renderer r;
        }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int PlayheadId = Shader.PropertyToID("_CathedralPlayhead");
        static readonly int HitId = Shader.PropertyToID("_CathedralHit");

        Seed[] seeds;
        int next, lastStep = -1;
        float amount, rebuildTimer, clapCooldown, lastHandGap = 1f;
        Vector3 hitPos; float hitAmount;
        bool wasWorn = true;
        MaterialPropertyBlock mpb;
        CathedralDsp boundDsp;

        public int Count { get; private set; }

        void Awake()
        {
            Instance = this;
            mpb = new MaterialPropertyBlock();
            seeds = new Seed[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = "PlantedNote" + i;
                g.transform.SetParent(transform, false);
                g.transform.localScale = Vector3.one * size;
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = seedMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                g.SetActive(false);
                seeds[i].t = g.transform; seeds[i].r = r;
            }
            Shader.SetGlobalVector(PlayheadId, Vector4.zero);
            Shader.SetGlobalVector(HitId, Vector4.zero);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static CathedralDsp Dsp => CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;

        /// <summary>Leave a note hanging at a point in the room. It sounds once a bar, when the light reaches its bay.</summary>
        public void Plant(Vector3 pos, int note, float freq, float hue)
        {
            var field = FeedbackField.Instance;
            Vector3 origin = field != null && field.viewer != null ? field.viewer.position : new Vector3(0f, Nave.EyeHeight, 0f);
            Vector3 d = pos - origin;
            int step = Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) * Nave.Wedges, Nave.Wedges)) % Nave.Wedges;

            int slot = next; next = (next + 1) % capacity;
            seeds[slot].alive = true; seeds[slot].armed = false;
            seeds[slot].pos = pos; seeds[slot].note = note; seeds[slot].step = step;
            seeds[slot].freq = freq; seeds[slot].hue = hue; seeds[slot].flash = 1f; seeds[slot].age = 0f;
            seeds[slot].t.position = pos; seeds[slot].t.gameObject.SetActive(true);
            Rebuild();
            var dsp = Dsp;
            if (dsp != null) dsp.OneShot(0, freq, bellGain * 1.2f, GrabbableOrb.PanFor(pos));
        }

        public void Clear()
        {
            for (int i = 0; i < capacity; i++) { seeds[i].alive = false; seeds[i].t.gameObject.SetActive(false); }
            Rebuild();
        }

        /// <summary>A thrown orb struck something: light it up in the stone and the glass.</summary>
        public void Strike(Vector3 pos, float strength)
        {
            hitPos = pos; hitAmount = Mathf.Max(hitAmount, Mathf.Clamp01(strength));
            var field = FeedbackField.Instance;
            if (field != null) field.AddSplat(pos, Color.white, 1.5f + 2f * strength, 0.09f);
        }

        void Rebuild()
        {
            Count = 0;
            var dsp = Dsp; boundDsp = dsp;
            if (dsp != null) System.Array.Clear(dsp.PatternCount, 0, dsp.PatternCount.Length);
            for (int i = 0; i < capacity; i++)
            {
                if (!seeds[i].alive) continue;
                Count++;
                if (dsp == null) continue;
                int st = seeds[i].step, k = dsp.PatternCount[st];
                if (k >= CathedralDsp.NotesPerStep) continue;
                dsp.Pattern[st * CathedralDsp.NotesPerStep + k] = new CathedralDsp.StepNote
                {
                    Freq = seeds[i].freq, Gain = bellGain, Pan = GrabbableOrb.PanFor(seeds[i].pos),
                };
                dsp.PatternCount[st] = k + 1;
            }
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var an = SpectrumAnalyzer.Instance;
            var field = FeedbackField.Instance;
            var hands = OrbGrabber.All;

            // The synth can be rebuilt (audio device change); keep its pattern and pans current.
            rebuildTimer += dt;
            if (Dsp != boundDsp || rebuildTimer > 2f) { rebuildTimer = 0f; Rebuild(); }

            // Headset off: the next person starts with an empty room.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            if (!worn && wasWorn && Count > 0) Clear();
            wasWorn = worn;

            // Clap (two empty hands coming together fast) clears the pattern, with a drum roll.
            clapCooldown -= dt;
            if (hands.Count >= 2 && hands[0].HandMode && hands[1].HandMode && !hands[0].Holding && !hands[1].Holding)
            {
                float gap = (hands[0].PalmPoint - hands[1].PalmPoint).magnitude;
                float closing = (lastHandGap - gap) / dt;
                if (gap < 0.11f && closing > 0.9f && clapCooldown <= 0f)
                {
                    clapCooldown = 1f;
                    var dsp = Dsp;
                    if (dsp != null) for (int k = 0; k < 3; k++) dsp.OneShot(1, 150f * Mathf.Pow(1.26f, k), 0.4f, k - 1f);
                    Strike((hands[0].PalmPoint + hands[1].PalmPoint) * 0.5f, 1f);
                    if (Count > 0) Clear();
                }
                lastHandGap = gap;
            }
            else lastHandGap = 1f;

            // Playhead: one bay per sixteenth, once round the room per bar.
            float stepPos = an != null ? (float)((an.BeatPosition * 4.0) % 16.0) : 0f;
            int step = Mathf.FloorToInt(stepPos);
            bool tick = step != lastStep; lastStep = step;
            amount = Mathf.MoveTowards(amount, Count > 0 ? 1f : 0f, dt * 1.5f);
            Shader.SetGlobalVector(PlayheadId, new Vector4(amount, stepPos, 0f, 0f));

            hitAmount = Mathf.MoveTowards(hitAmount, 0f, dt * 2.2f);
            Shader.SetGlobalVector(HitId, new Vector4(hitPos.x, hitPos.y, hitPos.z, hitAmount));

            bool changed = false;
            for (int i = 0; i < capacity; i++)
            {
                if (!seeds[i].alive) continue;
                seeds[i].age += dt;
                if (tick && seeds[i].step == step) seeds[i].flash = 1f;
                seeds[i].flash = Mathf.MoveTowards(seeds[i].flash, 0f, dt * 3f);

                // Poke to pop, but only once the hand that planted it has moved away.
                bool handNear = false, touched = false;
                for (int h = 0; h < hands.Count; h++)
                {
                    float d = (hands[h].TouchPoint - seeds[i].pos).magnitude;
                    if (d < 0.14f) handNear = true;
                    if (d < 0.055f && !hands[h].Holding) touched = true;
                }
                if (!handNear) seeds[i].armed = true;
                if (touched && seeds[i].armed && seeds[i].age > 0.8f)
                {
                    seeds[i].alive = false; seeds[i].t.gameObject.SetActive(false); changed = true;
                    var dsp = Dsp;
                    if (dsp != null) dsp.OneShot(0, seeds[i].freq * 2f, bellGain, GrabbableOrb.PanFor(seeds[i].pos));
                    continue;
                }

                Color c = GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue);
                float f = seeds[i].flash;
                seeds[i].t.localScale = Vector3.one * size * (1f + f * 1.4f);
                c.a = 0.7f + f * 2.4f;
                mpb.SetColor(ColorId, c); seeds[i].r.SetPropertyBlock(mpb);
                if (f > 0.55f && field != null) field.AddSplat(seeds[i].pos, c, 1.1f + f, 0.05f);
            }
            if (changed) Rebuild();
        }
    }
}
