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
    /// </summary>
    public class CometPool : MonoBehaviour
    {
        public static CometPool Instance { get; private set; }
        const int Layer = 30;       // an unnamed user layer, kept for comets alone

        [SerializeField] Material bodyMaterial, trailMaterial;
        [SerializeField] PhysicsMaterial bounce;
        [SerializeField] int size = 8;

        Comet[] comets;
        int next, rally;
        float lastColumnHit = -10f;

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

        /// <summary>Throw a comet. A throw roughly towards a column is steered onto it.</summary>
        public static void Launch(Vector3 pos, Vector3 velocity, Color color, float freq)
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
            c.Fire(pos, v, color, freq);
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
            if (rally >= 3) { rally = 0; StartCoroutine(Carillon(column.index)); }
        }

        /// <summary>The rally payoff: every column in the ring rings in turn, round from the one that was hit.</summary>
        IEnumerator Carillon(int start)
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
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(0.7f);
            if (Narrator.Instance != null) Narrator.Instance.Effect(Narrator.Fx.Choir, 0.45f);
        }
    }

    /// <summary>One thrown comet. Lives for a few seconds or a few bounces.</summary>
    public class Comet : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        Rigidbody body; Renderer rend; TrailRenderer trail; MaterialPropertyBlock mpb;
        Color color; float freq, life; int bounces;

        void Awake()
        {
            body = GetComponent<Rigidbody>(); rend = GetComponent<Renderer>(); trail = GetComponent<TrailRenderer>();
            mpb = new MaterialPropertyBlock();
        }

        public void Fire(Vector3 pos, Vector3 velocity, Color c, float f)
        {
            transform.position = pos;
            gameObject.SetActive(true);
            trail.Clear();
            body.linearVelocity = velocity; body.angularVelocity = Vector3.zero;
            color = c; freq = f; life = 0f; bounces = 0;
            trail.startColor = c; trail.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life += dt;
            Vector3 p = transform.position;

            // Passing through hanging notes strums them.
            if (SeedGarden.Instance != null) SeedGarden.Instance.Strum(p, 0.14f);

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
            transform.localScale = Vector3.one * 0.07f * fade;
            Color c = color; c.a = 1.6f * fade;
            mpb.SetColor(ColorId, c); rend.SetPropertyBlock(mpb);
            var field = FeedbackField.Instance;
            if (field != null) field.AddSplat(p, color, 1.0f * fade, 0.04f);
        }

        void OnCollisionEnter(Collision c)
        {
            float hit = Mathf.Clamp01(c.relativeVelocity.magnitude / 5f);
            bounces++;
            var column = c.collider.GetComponent<ColumnDrum>();
            if (column != null && CometPool.Instance != null) { CometPool.Instance.ColumnHit(column, c.GetContact(0).point, hit); return; }
            if (hit < 0.1f) return;
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            if (dsp != null) dsp.OneShot(1, 98f, 0.12f + 0.2f * hit, GrabbableOrb.PanFor(transform.position));
            if (SeedGarden.Instance != null) SeedGarden.Instance.Strike(c.GetContact(0).point, 0.3f * hit);
        }
    }
}
