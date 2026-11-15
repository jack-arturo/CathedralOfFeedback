using System.Collections;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Thrown light. When the player throws an orb, the orb stays with them and one of these
    /// comets carries the throw: it is steered onto a column if it is roughly aimed at one,
    /// rings what it hits, ricochets and fades. Because the orb is back in reach at once,
    /// throwing never stops the player playing, and they can throw as fast as they like.
    ///
    /// Three column hits in quick succession is a rally: the whole ring of columns answers.
    /// A struck column stays lit in its own colour; light all eight and the vault blooms: gold
    /// light floods down from the crown, the choir comes in, and the columns start again dark.
    ///
    /// And the hidden one: meet a returning comet with an orb that is already in your hand and
    /// the two collide. That is a supernova: an explosion of sparks, a run of bells into a drop,
    /// and eight bars with the arrangement flat out and the arpeggio an octave up.
    ///
    /// A comet that strikes a column comes back: it flies to a point within reach of the player,
    /// slowing as it arrives, and any hand (or a held orb) that meets it catches it. Caught in an
    /// empty hand, the orb itself is in that hand, ready to be thrown again. Each catch in a run
    /// winds the room up a third of the way, like stretching the string, and the third drops it.
    /// </summary>
    public class CometPool : MonoBehaviour
    {
        public static CometPool Instance { get; private set; }
        const int Layer = 30;       // an unnamed user layer, kept for comets alone

        [SerializeField] Material bodyMaterial, trailMaterial;
        [SerializeField] PhysicsMaterial bounce;
        [SerializeField] int size = 8;

        Comet[] comets;
        int next, rally, streak;
        float lastColumnHit = -10f, lastCatch = -100f, charge;
        const float StreakWindow = 12f;
        static readonly int[] Climb = { 0, 3, 5, 7, 10, 12 };

        /// <summary>0..1: how far a run of catches has wound the room up. Drives the same build-up as the string.</summary>
        public static float Charge => Instance != null ? Instance.charge : 0f;

        static readonly int ColsAId = Shader.PropertyToID("_CathedralColsA"), ColsBId = Shader.PropertyToID("_CathedralColsB"),
            BloomId = Shader.PropertyToID("_CathedralBloom");
        readonly bool[] lit = new bool[8];
        readonly float[] glow = new float[8];
        float bloom, rain, nova, novaReady;
        bool wasWorn = true;
        const float NovaBars = 8f, NovaSeconds = NovaBars * 2f;       // 120 BPM: a bar is two seconds
        static readonly int[] Run = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22, 24 };

        /// <summary>1 just after a supernova, falling to 0 as its eight bars play out.</summary>
        public static float Nova => Instance != null ? Instance.nova : 0f;

        /// <summary>A returning comet met an orb that was already in a hand. True if it set off a supernova.</summary>
        public bool Collide(Vector3 pos, Color cometColor)
        {
            if (Time.time < novaReady) return false;            // one at a time: otherwise it is an ordinary catch
            novaReady = Time.time + NovaSeconds + 4f;
            StartCoroutine(Supernova(pos, cometColor));
            return true;
        }

        IEnumerator Supernova(Vector3 pos, Color cometColor)
        {
            Debug.Log("[Cathedral] supernova");
            nova = 1f;
            Color orbColor = Color.white;
            float best = 1f;
            var orbs = GrabbableOrb.All;
            for (int i = 0; i < orbs.Count; i++)
            {
                float d = (orbs[i].transform.position - pos).magnitude;
                if (orbs[i].IsHeld && d < best) { best = d; orbColor = orbs[i].NoteColor; }
                orbs[i].Ring();
            }
            var garden = SeedGarden.Instance;
            var field = FeedbackField.Instance;
            float pan = GrabbableOrb.PanFor(pos);

            // The collision: a white-hot burst, the two colours thrown outward, a shockwave ring.
            if (garden != null)
            {
                garden.Burst(pos, Color.white, 160, 7.5f);
                garden.Burst(pos, orbColor, 110, 5f);
                garden.Ring(pos, cometColor, 72, 6.5f);
                garden.Strike(pos, 1f);
                // A run of bells racing up two octaves into the drop.
                for (int k = 0; k < Run.Length; k++)
                    garden.PlayLater(0.032f * k, 0, 440f * Mathf.Pow(2f, Run[k] / 12f), 0.2f, (k & 1) == 0 ? -0.5f : 0.5f);
            }
            if (field != null)
            {
                field.Flash(1f);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    field.AddSplat(pos + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * 3f, Color.Lerp(orbColor, cometColor, (k & 1)), 3f, 0.12f);
                }
            }
            if (FloorLights.Instance != null) FloorLights.Instance.Pulse(pos, cometColor);
            if (Narrator.Instance != null) Narrator.Instance.Effect(Narrator.Fx.Catch, 0.8f);

            yield return new WaitForSeconds(0.36f);

            // The drop lands (and moves the key), the choir comes in, and the next eight bars run flat out.
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            if (dsp != null) { dsp.Drop(); dsp.Overdrive((int)NovaBars); }
            if (Narrator.Instance != null) Narrator.Instance.OnDrop();
            if (field != null) field.Flash(1f);
            if (garden != null)
            {
                garden.Burst(pos, new Color(1f, 0.85f, 0.55f), 140, 3.2f);
                garden.Ring(pos + Vector3.up * 0.15f, orbColor, 96, 4f);
                garden.Burst(new Vector3(0f, Nave.LanternHeight, 0f), Color.white, 120, 6f);
            }
            if (FloorLights.Instance != null) FloorLights.Instance.Pulse(pos, orbColor);
        }

        void Awake()
        {
            Instance = this;
            // Comets pass through each other (the orbs have no collider); they only strike the building.
            Physics.IgnoreLayerCollision(Layer, Layer, true);
            comets = new Comet[size];
            for (int i = 0; i < size; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.name = "Comet" + i; g.layer = Layer;
                g.transform.SetParent(transform, false);
                g.transform.localScale = Vector3.one * 0.07f;
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = bodyMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                g.GetComponent<Collider>().sharedMaterial = bounce;
                var rb = g.AddComponent<Rigidbody>();
                rb.useGravity = false; rb.linearDamping = 0.25f; rb.angularDamping = 0f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var tr = g.AddComponent<TrailRenderer>();
                tr.sharedMaterial = trailMaterial; tr.time = 0.55f; tr.widthMultiplier = 0.045f; tr.minVertexDistance = 0.02f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tr.receiveShadows = false;
                comets[i] = g.AddComponent<Comet>();
                g.SetActive(false);
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (streak > 0 && Time.time - lastCatch > StreakWindow) streak = 0;
            float want = streak / 3f;
            charge = Mathf.MoveTowards(charge, want, dt * (want > charge ? 2.5f : 0.5f));

            nova = Mathf.MoveTowards(nova, 0f, dt / NovaSeconds);

            // The next wearer starts with dark columns.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            if (!worn && wasWorn) { for (int k = 0; k < 8; k++) lit[k] = false; streak = 0; }
            wasWorn = worn;

            if (bloom > 0f)
            {
                bloom = Mathf.MoveTowards(bloom, 0f, dt / 9f);
                if (bloom < 0.4f) for (int k = 0; k < 8; k++) lit[k] = false;       // the columns go dark again as it fades
                // Light pours down around the player.
                rain -= dt;
                Transform head = FeedbackField.Instance != null ? FeedbackField.Instance.viewer : null;
                if (rain <= 0f && bloom > 0.3f && head != null && SeedGarden.Instance != null)
                {
                    rain = 0.1f;
                    Vector2 r = Random.insideUnitCircle * 2.2f;
                    Vector3 p = head.position + new Vector3(r.x, Random.Range(0.5f, 2.4f), r.y);
                    SeedGarden.Instance.Burst(p, new Color(1f, 0.8f, 0.45f), 7, 1.1f);
                }
            }
            for (int k = 0; k < 8; k++) glow[k] = Mathf.MoveTowards(glow[k], lit[k] ? 1f : 0f, dt * (lit[k] ? 3f : 0.6f));
            Shader.SetGlobalVector(ColsAId, new Vector4(glow[0], glow[1], glow[2], glow[3]));
            Shader.SetGlobalVector(ColsBId, new Vector4(glow[4], glow[5], glow[6], glow[7]));
            Shader.SetGlobalFloat(BloomId, Mathf.SmoothStep(0f, 1f, bloom));
        }

        /// <summary>All eight columns are lit.</summary>
        IEnumerator Bloom()
        {
            bloom = 1f;
            Debug.Log("[Cathedral] bloom: all eight columns lit");
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(1f);
            if (Narrator.Instance != null) { Narrator.Instance.Effect(Narrator.Fx.Choir, 0.75f); Narrator.Instance.OnBloom(); }
            var wait = new WaitForSeconds(0.1f);
            for (int k = 0; k < 16; k++)
            {
                // Twice round the ring, the second time an octave up and higher on the columns.
                int idx = k % 8; bool second = k >= 8;
                Vector3 p = ColumnDrum.PositionOf(idx) + Vector3.up * (second ? 4.6f : 2.2f);
                var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
                if (dsp != null) dsp.OneShot(0, ColumnDrum.FrequencyOf(idx) * (second ? 4f : 2f), 0.3f, GrabbableOrb.PanFor(p));
                if (SeedGarden.Instance != null)
                {
                    SeedGarden.Instance.Strike(p, 1f);
                    SeedGarden.Instance.Burst(p, Color.HSVToRGB(idx / 8f, 0.7f, 1f), 22, 3f);
                }
                yield return wait;
            }
        }

        /// <summary>A returning comet met a hand.</summary>
        public void Caught(Vector3 pos, Color color, float freq, OrbGrabber hand, GrabbableOrb source)
        {
            // The light is the orb's: caught in an empty hand, that orb (or whichever is free) is in the hand.
            if (hand != null && !hand.Holding)
            {
                GrabbableOrb give = source != null && !source.IsHeld ? source : null;
                var free = GrabbableOrb.All;
                for (int i = 0; give == null && i < free.Count; i++) if (!free[i].IsHeld) give = free[i];
                if (give != null) hand.Catch(give);
            }

            streak = Time.time - lastCatch < StreakWindow ? streak + 1 : 1;
            lastCatch = Time.time;
            float pan = GrabbableOrb.PanFor(pos);
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            var garden = SeedGarden.Instance;
            // A rising chord, a step higher with each catch in the run.
            float root = 440f * Mathf.Pow(2f, Climb[Mathf.Min(streak - 1, Climb.Length - 1)] / 12f);
            if (dsp != null) dsp.OneShot(0, root, 0.24f, pan);
            if (garden != null)
            {
                garden.PlayLater(0.07f, 0, root * 1.5f, 0.2f, pan);
                garden.PlayLater(0.14f, 0, root * 2f, 0.2f, pan);
                garden.Burst(pos, color, 36, 2.6f);
                garden.Burst(pos, Color.white, 12, 4f);
                garden.Strike(pos, 0.9f);
            }
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.18f + 0.12f * streak);
            var orbs = GrabbableOrb.All;
            for (int i = 0; i < orbs.Count; i++) orbs[i].Ring();
            if (Narrator.Instance != null) { Narrator.Instance.Effect(Narrator.Fx.Catch, 0.8f); Narrator.Instance.OnCatch(); }
            Debug.Log($"[Cathedral] catch, streak {streak}");

            if (streak < 3) return;
            // Three in a row: the room drops and every column answers.
            streak = 0; lastCatch = -100f; charge = 0f;
            if (dsp != null) dsp.Drop();
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(1f);
            if (garden != null) garden.Burst(new Vector3(0f, Nave.LanternHeight, 0f), new Color(1f, 0.85f, 0.6f), 120, 6f);
            if (Narrator.Instance != null) Narrator.Instance.OnDrop();
            StartCoroutine(Carillon(0, false));
            Debug.Log("[Cathedral] catch drop");
        }

        /// <summary>Throw a comet. A throw roughly towards a column is steered onto it.</summary>
        public static void Launch(Vector3 pos, Vector3 velocity, Color color, float freq, GrabbableOrb source = null)
        {
            var pool = Instance;
            if (pool == null) return;
            Vector3 v = Vector3.ClampMagnitude(velocity, 10f);
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (flat.magnitude > 1.0f)
            {
                float best = 24f; Vector3 aim = Vector3.zero;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 to = ColumnDrum.PositionOf(k) - pos; to.y = 0f;
                    float ang = Vector3.Angle(flat, to);
                    if (ang < best) { best = ang; aim = to.normalized; }
                }
                if (aim != Vector3.zero) { Vector3 s = aim * flat.magnitude; v = new Vector3(s.x, v.y * 0.5f, s.z); }
            }
            // Keep it fast enough to arrive: a soft lob should still reach the columns.
            if (v.magnitude < 4.5f) v = v.normalized * 4.5f;
            var c = pool.comets[pool.next]; pool.next = (pool.next + 1) % pool.size;
            c.Fire(pos, v, color, freq, source);
        }

        /// <summary>A comet struck a column.</summary>
        public void ColumnHit(ColumnDrum column, Vector3 point, float hit)
        {
            float now = Time.time;
            rally = now - lastColumnHit < 2.5f ? rally + 1 : 1;
            lastColumnHit = now;
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            // Each hit in a rally is a step higher.
            float pitch = Mathf.Pow(2f, Mathf.Min(rally - 1, 6) * 2f / 12f);
            if (dsp != null) dsp.OneShot(1, column.Frequency * pitch, 0.28f + 0.4f * hit, GrabbableOrb.PanFor(point));
            if (SeedGarden.Instance != null)
            {
                SeedGarden.Instance.Strike(point, 0.5f + 0.5f * hit);
                SeedGarden.Instance.Burst(point, new Color(1f, 0.8f, 0.5f), 10 + rally * 6, 2.5f);
            }
            Debug.Log($"[Cathedral] column {column.index} struck, rally {rally}");
            if (bloom <= 0f)
            {
                lit[column.index] = true;
                bool all = true;
                for (int k = 0; k < 8; k++) all &= lit[k];
                if (all) { rally = 0; StartCoroutine(Bloom()); return; }
            }
            if (rally >= 3) { rally = 0; StartCoroutine(Carillon(column.index, true)); }
        }

        /// <summary>The rally payoff: every column in the ring rings in turn, round from the one that was hit.</summary>
        IEnumerator Carillon(int start, bool choir)
        {
            Debug.Log("[Cathedral] carillon");
            var wait = new WaitForSeconds(0.085f);
            for (int k = 0; k < 8; k++)
            {
                int idx = (start + k) % 8;
                Vector3 p = ColumnDrum.PositionOf(idx) + Vector3.up * 2.4f;
                var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
                if (dsp != null) dsp.OneShot(1, ColumnDrum.FrequencyOf(idx) * 2f, 0.45f, GrabbableOrb.PanFor(p));
                if (SeedGarden.Instance != null)
                {
                    SeedGarden.Instance.Strike(p, 1f);
                    SeedGarden.Instance.Burst(p, Color.HSVToRGB(idx / 8f, 0.7f, 1f), 26, 3f);
                }
                yield return wait;
            }
            if (!choir) yield break;
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.7f);
            if (Narrator.Instance != null) Narrator.Instance.Effect(Narrator.Fx.Choir, 0.45f);
        }
    }

    /// <summary>One thrown comet. Lives for a few seconds or a few bounces, unless it is on its way back to be caught.</summary>
    public class Comet : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        Rigidbody body; Renderer rend; TrailRenderer trail; MaterialPropertyBlock mpb;
        Color color; float freq, life; int bounces; GrabbableOrb source;
        // The way back after a column strike.
        bool ripe; float ripeTime, ripeEnd, side, lift, returnSpeed; Vector3 approach;

        void Awake()
        {
            body = GetComponent<Rigidbody>(); rend = GetComponent<Renderer>(); trail = GetComponent<TrailRenderer>();
            mpb = new MaterialPropertyBlock();
        }

        public void Fire(Vector3 pos, Vector3 velocity, Color c, float f, GrabbableOrb from)
        {
            source = from;
            transform.position = pos;
            gameObject.SetActive(true);
            trail.Clear();
            body.linearVelocity = velocity; body.angularVelocity = Vector3.zero;
            color = c; freq = f; life = 0f; bounces = 0; ripe = false; trail.widthMultiplier = 0.045f;
            trail.startColor = c; trail.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            Vector3 p = transform.position;

            // Passing through hanging notes strums them.
            if (SeedGarden.Instance != null) SeedGarden.Instance.Strum(p, 0.14f);

            // Once it has bounced it can be caught: by a hand, or by an orb in a hand.
            Transform head = FeedbackField.Instance != null ? FeedbackField.Instance.viewer : null;
            bool magnet = false; Vector3 pull = p;
            if (bounces >= 1)
            {
                var hands = OrbGrabber.All;
                float nearest = 0.32f;
                for (int h = 0; h < hands.Count; h++)
                {
                    float d = Mathf.Min((hands[h].PalmPoint - p).magnitude, (hands[h].GrabPoint - p).magnitude);
                    if (d < (ripe ? 0.15f : 0.12f))
                    {
                        // Met by an orb already in a hand, the two collide; otherwise it is a catch.
                        var pool = CometPool.Instance;
                        if (pool != null && !(hands[h].Holding && pool.Collide(p, color))) pool.Caught(p, color, freq, hands[h], source);
                        gameObject.SetActive(false);
                        return;
                    }
                    if (d < nearest) { nearest = d; pull = hands[h].PalmPoint; magnet = true; }     // a hand reaching for it draws it in
                }
            }
            if (ripe)
            {
                ripeTime += dt; life = 0f; bounces = 1;
                Vector3 tgt = magnet || head == null ? pull : CatchPoint(head);
                Vector3 to = tgt - p; float dist = to.magnitude;
                if (ripeEnd < 0f || magnet)
                {
                    // Fast across the nave, slow for the last metre so there is time to meet it.
                    float speed = Mathf.Lerp(1f, returnSpeed, Mathf.InverseLerp(0.3f, 1.5f, dist));
                    Vector3 was = body.linearVelocity.sqrMagnitude > 1e-4f ? body.linearVelocity.normalized : to.normalized;
                    body.linearVelocity = Vector3.RotateTowards(was, to / Mathf.Max(dist, 1e-3f), 6f * dt, 0f) * speed;
                    if (ripeEnd < 0f && (dist < 0.12f || ripeTime > 6f)) ripeEnd = ripeTime + 0.8f;     // drifting past: a last moment to snatch it
                }
                else if (ripeTime > ripeEnd) { ripe = false; life = 3.6f; trail.widthMultiplier = 0.045f; }
            }

            // The glass rings when a comet reaches it, and sends it back. Below the springing
            // line the shell is a drum; above it, each side is an arc about a centre across the axis.
            float rad = new Vector2(p.x, p.z).magnitude;
            Vector3 outward = rad > 1e-3f ? new Vector3(p.x, 0f, p.z) / rad : Vector3.right;
            bool hitShell; Vector3 n;
            if (p.y <= Nave.WallHeight) { hitShell = rad > Nave.Radius - 0.35f; n = -outward; }
            else
            {
                Vector2 q = new Vector2(rad + Nave.ArchRadius - Nave.Radius, p.y - Nave.WallHeight);
                hitShell = q.magnitude > Nave.ArchRadius - 0.35f;
                Vector2 inw = -q.normalized;
                n = outward * inw.x + Vector3.up * inw.y;
            }
            if (hitShell)
            {
                Vector3 vel = body.linearVelocity;
                if (Vector3.Dot(vel, n) < 0f)
                {
                    body.linearVelocity = Vector3.Reflect(vel, n) * 0.7f;
                    bounces++;
                    var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
                    if (dsp != null) dsp.OneShot(0, freq * 2f, 0.28f, GrabbableOrb.PanFor(p));
                    if (SeedGarden.Instance != null) { SeedGarden.Instance.Strike(p, 0.7f); SeedGarden.Instance.Burst(p, color, 14, 2f); }
                }
            }

            float fade = Mathf.Clamp01((4f - life) / 0.4f) * (bounces >= 5 ? 0f : 1f);
            if (fade <= 0f || p.y < -1f || p.y > 25f) { gameObject.SetActive(false); return; }
            // On the way back it is bigger, whiter and throbbing: "catch me".
            float throb = ripe ? 1.5f + 0.25f * Mathf.Sin(Time.time * 18f) : 1f;
            transform.localScale = Vector3.one * (0.07f * fade * throb);
            Color c = ripe ? Color.Lerp(color, Color.white, 0.35f) : color; c.a = (ripe ? 2.4f : 1.6f) * fade;
            mpb.SetColor(ColorId, c); rend.SetPropertyBlock(mpb);
            var field = FeedbackField.Instance;
            if (field != null) field.AddSplat(p, color, 1.0f * fade, 0.04f);
        }

        /// <summary>Where a returning comet is headed: just in front of the player on the side it comes from,
        /// off to one side at about shoulder height, so catching it takes a reach.</summary>
        Vector3 CatchPoint(Transform head)
        {
            Vector3 right = new Vector3(approach.z, 0f, -approach.x);
            return head.position + approach * 0.35f + right * (side * 0.42f) + Vector3.up * lift;
        }

        void BeginReturn()
        {
            Transform head = FeedbackField.Instance != null ? FeedbackField.Instance.viewer : null;
            if (head == null) return;
            approach = transform.position - head.position; approach.y = 0f;
            approach = approach.sqrMagnitude > 1e-4f ? approach.normalized : Vector3.forward;
            side = Random.value < 0.5f ? -1f : 1f; lift = Random.Range(-0.2f, 0.15f);
            ripe = true; ripeTime = 0f; ripeEnd = -1f; life = 0f; bounces = 1;
            Vector3 to = CatchPoint(head) - transform.position;
            returnSpeed = Mathf.Clamp(to.magnitude / 1.25f, 1.8f, 3.6f);
            body.linearVelocity = to.normalized * returnSpeed;
            trail.widthMultiplier = 0.07f;
            if (Narrator.Instance != null) Narrator.Instance.OnRebound();
        }

        void OnCollisionEnter(Collision c)
        {
            float hit = Mathf.Clamp01(c.relativeVelocity.magnitude / 5f);
            bounces++;
            var column = c.collider.GetComponent<ColumnDrum>();
            if (column != null && CometPool.Instance != null)
            {
                CometPool.Instance.ColumnHit(column, c.GetContact(0).point, hit);
                BeginReturn();
                return;
            }
            if (hit < 0.1f) return;
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            if (dsp != null) dsp.OneShot(1, 98f, 0.12f + 0.2f * hit, GrabbableOrb.PanFor(transform.position));
            if (SeedGarden.Instance != null) SeedGarden.Instance.Strike(c.GetContact(0).point, 0.3f * hit);
        }
    }
}
