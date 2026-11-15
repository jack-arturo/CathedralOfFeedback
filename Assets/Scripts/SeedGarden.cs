using System.Collections.Generic;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The room as a step sequencer. The building has 16 bays and a bar has 16 steps: a light
    /// runs round the bays once per bar, and every note the player has left hanging in the air
    /// rings (a glass bell) when the light reaches the bay behind it.
    ///
    /// Hold an orb still to bud a note where it is; height is its pitch. Each note stays tied to
    /// the orb that made it by a thread of light, and the orb answers when the note rings. Poke a
    /// note to pop it (pop several quickly for a climbing combo), throw a comet through notes to
    /// strum them, and clap to harvest: every note flies up into the lantern in a rising run and
    /// the room drops. Notes are cleared when the headset comes off, so the next player starts clean.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class SeedGarden : MonoBehaviour
    {
        public static SeedGarden Instance { get; private set; }

        [SerializeField] Material seedMaterial, threadMaterial, sparkMaterial;
        [SerializeField] int capacity = 16;
        [SerializeField] float size = 0.045f;
        [SerializeField] float bellGain = 0.16f;

        const int ThreadPoints = 10;
        const float RiseSeconds = 0.8f;
        static readonly int[] Climb = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22 };

        struct Seed
        {
            public bool alive, armed, rising, rung, fused;
            public Vector3 pos, from; public int note, step; public float freq, hue, flash, age, rise, fuse;
            public Transform t; public Renderer r; public LineRenderer thread; public GrabbableOrb parent;
        }
        struct Later { public float at; public int kind; public float freq, gain, pan; }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int PlayheadId = Shader.PropertyToID("_CathedralPlayhead");
        static readonly int HitId = Shader.PropertyToID("_CathedralHit");

        Seed[] seeds;
        int next, lastStep = -1, combo, harvestTotal, harvestLeft;
        float amount, rebuildTimer, clapCooldown, lastHandGap = 1f, lastPop = -10f;
        Vector3 hitPos; float hitAmount;
        bool wasWorn = true;
        MaterialPropertyBlock mpb;
        CathedralDsp boundDsp;
        ParticleSystem sparks;
        readonly List<Later> later = new List<Later>();
        readonly Vector3[] pts = new Vector3[ThreadPoints];

        /// <summary>Notes currently hanging in the room.</summary>
        public int Count { get; private set; }
        public bool Harvesting => harvestLeft > 0;

        static Vector3 LanternPoint => new Vector3(0f, Nave.LanternHeight, 0f);       // the ring at the crown of the canopy

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

                var tg = new GameObject("Thread");
                tg.transform.SetParent(transform, false);
                var lr = tg.AddComponent<LineRenderer>();
                lr.sharedMaterial = threadMaterial != null ? threadMaterial : seedMaterial;
                lr.useWorldSpace = true; lr.positionCount = ThreadPoints; lr.widthMultiplier = 0.004f;
                lr.numCapVertices = 0; lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.enabled = false;

                g.SetActive(false);
                seeds[i].t = g.transform; seeds[i].r = r; seeds[i].thread = lr;
            }
            MakeSparks();
            Shader.SetGlobalVector(PlayheadId, Vector4.zero);
            Shader.SetGlobalVector(HitId, Vector4.zero);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static CathedralDsp Dsp => CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;

        // ------------------------------------------------------------------ sparks

        void MakeSparks()
        {
            if (sparkMaterial == null) return;
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = 1400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.8f; main.startSpeed = 0f; main.startSize = 0.02f;
            main.gravityModifier = 0.3f;
            var em = sparks.emission; em.enabled = false;
            var sh = sparks.shape; sh.enabled = false;
            var lim = sparks.limitVelocityOverLifetime;
            lim.enabled = true; lim.limit = 1000f; lim.dampen = 0f; lim.drag = 2.2f;
            lim.multiplyDragByParticleSize = false; lim.multiplyDragByParticleVelocity = false;
            var col = sparks.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sparkMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            sparks.Play();
        }

        /// <summary>A burst of sparks at a point: pops, strikes, harvest.</summary>
        public void Burst(Vector3 pos, Color color, int count, float speed)
        {
            if (sparks == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                ep.position = pos + dir * 0.012f;
                ep.velocity = dir * (speed * Random.Range(0.3f, 1f));
                ep.startLifetime = Random.Range(0.45f, 1.05f);
                ep.startSize = Random.Range(0.012f, 0.03f);
                ep.startColor = Color.Lerp(color, Color.white, Random.value * 0.45f);
                sparks.Emit(ep, 1);
            }
        }

        /// <summary>A flat ring of sparks racing outward: a shockwave.</summary>
        public void Ring(Vector3 pos, Color color, int count, float speed)
        {
            if (sparks == null) return;
            var ep = new ParticleSystem.EmitParams();
            float phase = Random.value * 6.2832f;
            for (int i = 0; i < count; i++)
            {
                float a = phase + i * (2f * Mathf.PI / count);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ep.position = pos + dir * 0.05f;
                ep.velocity = dir * speed;
                ep.startLifetime = 0.95f; ep.startSize = 0.03f; ep.startColor = color;
                sparks.Emit(ep, 1);
            }
        }

        /// <summary>Play a synth one-shot after a delay (for runs and chords).</summary>
        public void PlayLater(float delay, int kind, float freq, float gain, float pan) =>
            later.Add(new Later { at = Time.time + delay, kind = kind, freq = freq, gain = gain, pan = pan });

        // ------------------------------------------------------------------ notes

        /// <summary>Leave a note hanging at a point in the room. It sounds once a bar, when the light reaches its bay.</summary>
        public void Plant(Vector3 pos, int note, float freq, float hue, GrabbableOrb parent = null)
        {
            if (Harvesting) return;
            var field = FeedbackField.Instance;
            Vector3 origin = field != null && field.viewer != null ? field.viewer.position : new Vector3(0f, Nave.EyeHeight, 0f);
            Vector3 d = pos - origin;
            int step = Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(d.z, d.x) / (2f * Mathf.PI) * Nave.Wedges, Nave.Wedges)) % Nave.Wedges;

            int slot = next; next = (next + 1) % capacity;
            seeds[slot].alive = true; seeds[slot].armed = false; seeds[slot].rising = false; seeds[slot].fused = false;
            seeds[slot].pos = pos; seeds[slot].note = note; seeds[slot].step = step;
            seeds[slot].freq = freq; seeds[slot].hue = hue; seeds[slot].flash = 1f; seeds[slot].age = 0f;
            seeds[slot].parent = parent;
            seeds[slot].t.position = pos; seeds[slot].t.gameObject.SetActive(true);
            Rebuild();
            var dsp = Dsp;
            if (dsp != null) dsp.OneShot(0, freq, bellGain * 1.2f, GrabbableOrb.PanFor(pos));
            Burst(pos, GrabbableOrb.ColorForNote(note, hue), 10, 0.9f);
            CathedralDirector.Stir(0.04f);
            Debug.Log($"[Cathedral] note planted, {Count} hanging");
        }

        void Kill(int i)
        {
            seeds[i].alive = false; seeds[i].rising = false; seeds[i].fused = false; seeds[i].parent = null;
            seeds[i].t.gameObject.SetActive(false); seeds[i].thread.enabled = false;
        }

        public void Clear()
        {
            for (int i = 0; i < capacity; i++) Kill(i);
            harvestLeft = 0;
            Rebuild();
        }

        /// <summary>Something struck the building here: light it up in the stone and the glass.</summary>
        public void Strike(Vector3 pos, float strength)
        {
            hitPos = pos; hitAmount = Mathf.Max(hitAmount, Mathf.Clamp01(strength));
            var field = FeedbackField.Instance;
            if (field != null) field.AddSplat(pos, Color.white, 1.5f + 2f * strength, 0.09f);
        }

        /// <summary>
        /// Set off every note tied to an orb, nearest first, like a fuse running out along the
        /// threads: each burns bright, then bursts and rings. Returns how many were lit.
        /// </summary>
        public int Cascade(GrabbableOrb orb, Vector3 from)
        {
            var order = new List<int>();
            for (int i = 0; i < capacity; i++)
                if (seeds[i].alive && !seeds[i].rising && !seeds[i].fused && (orb == null || seeds[i].parent == orb)) order.Add(i);
            order.Sort((x, y) => (seeds[x].pos - from).sqrMagnitude.CompareTo((seeds[y].pos - from).sqrMagnitude));
            for (int k = 0; k < order.Count; k++) { seeds[order[k]].fused = true; seeds[order[k]].fuse = 0.15f + 0.12f * k; }
            if (order.Count >= 4) PlayLater(0.15f + 0.12f * order.Count, 0, 1760f, bellGain * 1.4f, 0f);
            return order.Count;
        }

        void Detonate(int i)
        {
            Vector3 pos = seeds[i].pos;
            Color c = GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue);
            Burst(pos, c, 34, 3f);
            Burst(pos, Color.white, 10, 4.5f);
            Strike(pos, 0.9f);
            var dsp = Dsp;
            if (dsp != null) dsp.OneShot(0, seeds[i].freq * 2f, bellGain * 1.5f, GrabbableOrb.PanFor(pos));
            if (Narrator.Instance != null) Narrator.Instance.Effect(Narrator.Fx.Shatter, 0.4f);
            Kill(i);
        }

        /// <summary>Something lined up just right: a ring of sparks, a quick run of bells, a flash in the vault.</summary>
        public void Flourish(Vector3 pos, Color color)
        {
            Ring(pos, color, 48, 4f);
            Burst(pos, Color.white, 30, 4f);
            for (int k = 0; k < 5; k++) PlayLater(0.06f * (k + 1), 0, 880f * Mathf.Pow(2f, Climb[k + 3] / 12f), bellGain * 1.3f, (k - 2) * 0.4f);
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.4f);
            CathedralDirector.Stir(0.15f);
        }

        /// <summary>A comet passing through hanging notes rings them as it goes. Returns how many it rang.</summary>
        public int Strum(Vector3 p, float radius)
        {
            int rang = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (!seeds[i].alive || seeds[i].rising || seeds[i].flash > 0.4f) continue;
                if ((seeds[i].pos - p).sqrMagnitude > radius * radius) continue;
                seeds[i].flash = 1f;
                var dsp = Dsp;
                if (dsp != null) dsp.OneShot(0, seeds[i].freq, bellGain * 1.3f, GrabbableOrb.PanFor(seeds[i].pos));
                Burst(seeds[i].pos, GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue), 8, 1.4f);
                if (seeds[i].parent != null) seeds[i].parent.Ring();
                rang++;
            }
            return rang;
        }

        void Pop(int i)
        {
            Vector3 pos = seeds[i].pos;
            Color c = GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue);
            float pan = GrabbableOrb.PanFor(pos);
            combo = Time.time - lastPop < 1.6f ? combo + 1 : 1;
            lastPop = Time.time;

            // Sparks, a crack of glass, a flash in the wall behind it, and its own note an octave up.
            Burst(pos, c, 30, 2.4f);
            Burst(pos, Color.white, 10, 3.6f);
            Strike(pos, 0.85f);
            var dsp = Dsp;
            if (dsp != null)
            {
                dsp.OneShot(0, seeds[i].freq * 2f, bellGain * 1.4f, pan);
                // Quick pops climb the scale.
                if (combo > 1) dsp.OneShot(0, 880f * Mathf.Pow(2f, Climb[Mathf.Min(combo - 1, Climb.Length - 1)] / 12f), bellGain * 1.2f, pan);
            }
            if (Narrator.Instance != null) Narrator.Instance.Effect(Narrator.Fx.Shatter, 0.5f + 0.1f * Mathf.Min(combo, 4));
            if (seeds[i].parent != null) seeds[i].parent.Ring();
            if (combo == 4)
            {
                // Four in a row: a flourish and a flash through the whole vault.
                for (int k = 0; k < 5; k++) PlayLater(0.07f * (k + 1), 0, 880f * Mathf.Pow(2f, Climb[k + 4] / 12f), bellGain * 1.3f, (k - 2) * 0.4f);
                if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.45f);
                Burst(pos, Color.white, 40, 5f);
            }
            CathedralDirector.Stir(0.03f);
            Debug.Log($"[Cathedral] pop, combo {combo}");
            Kill(i);
        }

        /// <summary>Clap with notes hanging: they fly up to the lantern, lowest first, and the room drops.</summary>
        void Harvest()
        {
            var order = new List<int>();
            for (int i = 0; i < capacity; i++) if (seeds[i].alive && !seeds[i].rising) order.Add(i);
            order.Sort((a, b) => seeds[a].freq.CompareTo(seeds[b].freq));
            float gap = Mathf.Min(0.11f, 1.3f / order.Count);
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                seeds[i].rising = true; seeds[i].rung = false; seeds[i].rise = -k * gap; seeds[i].from = seeds[i].pos;
                seeds[i].thread.enabled = false;
            }
            harvestTotal = harvestLeft = order.Count;
            Rebuild();
            if (Narrator.Instance != null) { Narrator.Instance.Effect(Narrator.Fx.Rise, 0.6f); Narrator.Instance.OnHarvest(); }
            Debug.Log($"[Cathedral] harvest of {order.Count}");
        }

        void HarvestFinale()
        {
            Vector3 top = LanternPoint;
            float size01 = Mathf.Clamp01(harvestTotal / 10f);
            var dsp = Dsp;
            if (dsp != null) dsp.Drop();
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.55f + 0.45f * size01);
            Strike(top, 1f);
            Burst(top, new Color(1f, 0.85f, 0.6f), 60 + harvestTotal * 8, 5.5f);
            if (Narrator.Instance != null) Narrator.Instance.OnDrop();
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
                if (dsp == null || seeds[i].rising) continue;
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

            for (int k = later.Count - 1; k >= 0; k--)
            {
                if (Time.time < later[k].at) continue;
                var dsp = Dsp;
                if (dsp != null) dsp.OneShot(later[k].kind, later[k].freq, later[k].gain, later[k].pan);
                later.RemoveAt(k);
            }

            // Headset off: the next person starts with an empty room.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            if (!worn && wasWorn && Count > 0) Clear();
            wasWorn = worn;

            // Clap: two empty hands coming together fast.
            clapCooldown -= dt;
            if (hands.Count >= 2 && hands[0].HandMode && hands[1].HandMode && !hands[0].Holding && !hands[1].Holding)
            {
                float gap = (hands[0].PalmPoint - hands[1].PalmPoint).magnitude;
                float closing = (lastHandGap - gap) / dt;
                if (gap < 0.11f && closing > 0.9f && clapCooldown <= 0f && !Harvesting)
                {
                    clapCooldown = 1f;
                    Vector3 mid = (hands[0].PalmPoint + hands[1].PalmPoint) * 0.5f;
                    var dsp = Dsp;
                    if (dsp != null) for (int k = 0; k < 3; k++) dsp.OneShot(1, 150f * Mathf.Pow(1.26f, k), 0.4f, k - 1f);
                    Strike(mid, 1f);
                    Burst(mid, Color.white, 24, 3f);
                    if (Count >= 3) Harvest();
                    else if (Count > 0)
                    {
                        for (int i = 0; i < capacity; i++)
                            if (seeds[i].alive) Burst(seeds[i].pos, GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue), 16, 2f);
                        Clear();
                    }
                }
                lastHandGap = gap;
            }
            else lastHandGap = 1f;

            // Playhead: one bay per sixteenth, once round the room per bar.
            float stepPos = an != null ? (float)((an.BeatPosition * 4.0) % 16.0) : 0f;
            int step = Mathf.FloorToInt(stepPos);
            bool tick = step != lastStep; lastStep = step;
            amount = Mathf.MoveTowards(amount, Count > 0 && !Harvesting ? 1f : 0f, dt * 1.5f);
            Shader.SetGlobalVector(PlayheadId, new Vector4(amount, stepPos, 0f, 0f));

            hitAmount = Mathf.MoveTowards(hitAmount, 0f, dt * 2.2f);
            Shader.SetGlobalVector(HitId, new Vector4(hitPos.x, hitPos.y, hitPos.z, hitAmount));

            bool changed = false;
            for (int i = 0; i < capacity; i++)
            {
                if (!seeds[i].alive) continue;
                Color c = GrabbableOrb.ColorForNote(seeds[i].note, seeds[i].hue);

                if (seeds[i].rising)
                {
                    // Harvest: spiral up the axis of the building into the lantern.
                    seeds[i].rise += dt;
                    if (seeds[i].rise < 0f) continue;
                    if (!seeds[i].rung)
                    {
                        seeds[i].rung = true;
                        var dsp = Dsp;
                        if (dsp != null) dsp.OneShot(0, seeds[i].freq, bellGain * 1.5f, GrabbableOrb.PanFor(seeds[i].pos));
                        Burst(seeds[i].pos, c, 12, 1.6f);
                    }
                    float u = Mathf.Clamp01(seeds[i].rise / RiseSeconds), e = u * u;
                    Vector3 top = LanternPoint, from = seeds[i].from;
                    Vector3 flat = Quaternion.AngleAxis(e * 240f, Vector3.up) * new Vector3(from.x, 0f, from.z) * (1f - e);
                    Vector3 p = new Vector3(flat.x, Mathf.Lerp(from.y, top.y, e), flat.z);
                    seeds[i].pos = p; seeds[i].t.position = p;
                    seeds[i].t.localScale = Vector3.one * size * (1.6f + 2f * u);
                    c.a = 2.5f; mpb.SetColor(ColorId, c); seeds[i].r.SetPropertyBlock(mpb);
                    if (field != null) field.AddSplat(p, c, 1.6f, 0.05f);
                    if (u >= 1f)
                    {
                        Burst(p, c, 14, 2.5f);
                        Kill(i); changed = true;
                        if (--harvestLeft <= 0) { harvestLeft = 0; HarvestFinale(); }
                    }
                    continue;
                }

                seeds[i].age += dt;
                if (seeds[i].fused)
                {
                    // The fuse is running: the note and its thread burn bright, then it goes off.
                    seeds[i].fuse -= dt; seeds[i].flash = 1f;
                    if (seeds[i].fuse <= 0f) { Detonate(i); changed = true; continue; }
                }
                if (tick && seeds[i].step == step)
                {
                    seeds[i].flash = 1f;
                    if (seeds[i].parent != null) seeds[i].parent.Ring();
                }
                seeds[i].flash = Mathf.MoveTowards(seeds[i].flash, 0f, dt * 3f);

                // Poke to pop, but only once the hand that planted it has moved away.
                bool handNear = false, touched = false;
                for (int h = 0; h < hands.Count; h++)
                {
                    float d = (hands[h].TouchPoint - seeds[i].pos).magnitude;
                    if (d < 0.15f) handNear = true;
                    if (d < 0.065f && !hands[h].Holding) touched = true;
                }
                if (!handNear) seeds[i].armed = true;
                if (touched && seeds[i].armed && seeds[i].age > 0.8f) { Pop(i); changed = true; continue; }

                float f = seeds[i].flash;
                seeds[i].t.localScale = Vector3.one * size * (1f + f * 1.4f);
                c.a = 0.7f + f * 2.4f;
                mpb.SetColor(ColorId, c); seeds[i].r.SetPropertyBlock(mpb);
                if (f > 0.55f && field != null) field.AddSplat(seeds[i].pos, c, 1.1f + f, 0.05f);

                // The thread back to the orb that made this note. It hangs slack, brightens and
                // shivers when the note rings, and is gone when the note is.
                var lr = seeds[i].thread;
                var orb = seeds[i].parent;
                if (orb == null) { if (lr.enabled) lr.enabled = false; continue; }
                Vector3 a = seeds[i].pos, b = orb.transform.position;
                float len = (b - a).magnitude;
                if (len < 0.03f) { if (lr.enabled) lr.enabled = false; continue; }
                Vector3 axis = (b - a) / len;
                Vector3 side = Vector3.Cross(axis, Vector3.up);
                side = side.sqrMagnitude < 1e-4f ? Vector3.right : side.normalized;
                float sag = Mathf.Min(0.12f, len * 0.08f);
                for (int k = 0; k < ThreadPoints; k++)
                {
                    float u = k / (float)(ThreadPoints - 1), env = Mathf.Sin(u * Mathf.PI);
                    pts[k] = Vector3.Lerp(a, b, u) + Vector3.down * (sag * env)
                             + side * (Mathf.Sin(u * 18f - Time.time * 22f) * 0.012f * f * env);
                }
                lr.SetPositions(pts);
                float bright = 0.32f + 1.1f * f;
                Color ca = c; ca.a = bright;                                    // the glow shader reads alpha as intensity
                Color cb = Color.HSVToRGB(orb.baseHue, 0.75f, 1f); cb.a = bright * 0.75f;
                lr.startColor = ca; lr.endColor = cb;
                lr.widthMultiplier = 0.0035f + 0.006f * f;
                if (!lr.enabled) lr.enabled = true;
            }
            if (changed) Rebuild();
        }
    }
}
