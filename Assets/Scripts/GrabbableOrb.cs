using System.Collections.Generic;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// A playable orb. While held, height picks a pentatonic note and hand speed opens the
    /// filter; the synth plucks it on the beat grid so anything you do is in time. It paints
    /// the wall behind it into the feedback field, and when thrown it flies, then drifts home
    /// so it can never be lost. Its movement is replayed by <see cref="TrailEcho"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableOrb : MonoBehaviour
    {
        public static readonly List<GrabbableOrb> All = new List<GrabbableOrb>();
        public const int VoicesPerOrb = 5;
        /// <summary>Standing eye height of the current player; the note range hangs off it so the
        /// instrument fits a child, a seated player, or a headset with a bad floor estimate.</summary>
        public static float PlayerHeight = 1.6f;
        public static float MinHeight => PlayerHeight - 1.0f;      // about knee height
        public static float MaxHeight => PlayerHeight + 0.4f;      // a comfortable reach overhead
        static readonly int[] Scale = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22 };   // A minor pentatonic, two octaves
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("0 or 1: which block of five synth voices (one live, four echoes) this orb owns.")]
        public int orbIndex;
        public float rootHz = 220f;
        [Range(0, 1)] public float baseHue = 0.09f;
        public float liveGain = 0.42f;
        public Renderer core, halo;
        public TrailRenderer Trail;
        [SerializeField] float returnDelay = 2.6f;
        [Tooltip("Resting place relative to the player: metres to the right (negative = left).")]
        public float sideOffset = 0.22f;

        public Rigidbody Body { get; private set; }
        public bool IsHeld { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Speed { get; private set; }
        public int NoteIndex { get; private set; }
        public float Height01 { get; private set; }
        public float Brightness { get; private set; }
        public bool Fast { get; private set; }
        public float Frequency => FreqForNote(NoteIndex, rootHz);
        public Color NoteColor => ColorForNote(NoteIndex, baseHue);

        OrbGrabber holder;
        Vector3 home, lastPos, homeVel, baseScale;
        float freeTime = 999f, awayTime, invite, heldTime;
        Vector3 lean;
        MaterialPropertyBlock mpb;

        public static int NoteForHeight(float y) =>
            Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(MinHeight, MaxHeight, y) * (Scale.Length - 1)), 0, Scale.Length - 1);
        public static float FreqForNote(int note, float root) => root * Mathf.Pow(2f, Scale[Mathf.Clamp(note, 0, Scale.Length - 1)] / 12f);
        public static Color ColorForNote(int note, float hue) => Color.HSVToRGB(Mathf.Repeat(hue + note * 0.085f, 1f), 0.85f, 1f);

        /// <summary>Stereo position of a world point relative to the listener, -1..1.</summary>
        public static float PanFor(Vector3 worldPos)
        {
            var field = FeedbackField.Instance;
            Transform head = field != null ? field.viewer : null;
            if (head == null) return 0f;
            Vector3 l = head.InverseTransformPoint(worldPos);
            return Mathf.Clamp(l.x / (l.magnitude + 0.15f), -1f, 1f);
        }

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.isKinematic = true;
            if (Trail == null) Trail = GetComponentInChildren<TrailRenderer>();
            if (Trail != null) Trail.emitting = false;
            home = lastPos = transform.position;
            baseScale = transform.localScale;
            mpb = new MaterialPropertyBlock();
            NoteIndex = NoteForHeight(home.y);
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Grab(OrbGrabber hand)
        {
            holder = hand; IsHeld = true; heldTime = 0f;
            Body.isKinematic = true;
            if (Trail != null) Trail.emitting = true;
        }

        public void Release()
        {
            IsHeld = false; holder = null; freeTime = 0f;
            Body.isKinematic = false;

            // Let go gently and the orb leaves its note hanging in the air; throw it and it flies.
            float speed = Velocity.magnitude;
            if (speed < 0.5f && heldTime > 0.35f && SeedGarden.Instance != null)
            {
                SeedGarden.Instance.Plant(transform.position, NoteIndex, Frequency, baseHue);
                Body.linearVelocity = Vector3.zero;
                freeTime = returnDelay - 0.35f;            // head home almost at once
                return;
            }

            // A throw is boosted a little and, if it is roughly towards a column, steered onto it,
            // so a casual toss reliably rings something.
            Vector3 v = Vector3.ClampMagnitude(Velocity * 1.35f, 9f);
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (flat.magnitude > 1.2f)
            {
                float best = 22f; Vector3 aim = Vector3.zero;
                for (int k = 0; k < 8; k++)
                {
                    float phi = 2f * Mathf.PI * (k + 0.5f) / 8f;
                    Vector3 col = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi)) * Nave.CanopyRadius;
                    Vector3 to = col - transform.position; to.y = 0f;
                    float ang = Vector3.Angle(flat, to);
                    if (ang < best) { best = ang; aim = to.normalized; }
                }
                if (aim != Vector3.zero) { Vector3 steered = aim * flat.magnitude; v = new Vector3(steered.x, v.y, steered.z); }
            }
            Body.linearVelocity = v;
        }

        void OnCollisionEnter(Collision c)
        {
            if (IsHeld || Body.isKinematic) return;
            float hit = Mathf.Clamp01(c.relativeVelocity.magnitude / 4f);
            if (hit < 0.08f) return;
            var column = c.collider.GetComponent<ColumnDrum>();
            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null)
                audioHost.Dsp.OneShot(1, column != null ? column.Frequency : 82f, 0.2f + 0.45f * hit, PanFor(transform.position));
            if (SeedGarden.Instance != null) SeedGarden.Instance.Strike(c.GetContact(0).point, column != null ? 0.5f + 0.5f * hit : 0.3f * hit);
            freeTime = Mathf.Min(freeTime, returnDelay - 0.9f);     // let it ricochet before heading home
        }

        /// <summary>
        /// The orbs rest in front of the player at chest height. If the player walks off or
        /// turns away for a moment, the resting place follows, so there is always an orb in reach.
        /// </summary>
        void FollowPlayer(float dt)
        {
            var field = FeedbackField.Instance;
            Transform head = field != null ? field.viewer : null;
            if (head == null) return;
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.04f) return;                       // looking straight up or down
            f.Normalize();
            Vector3 right = new Vector3(f.z, 0f, -f.x);
            Vector3 want = head.position + f * 0.42f + right * sideOffset;
            want.y = Mathf.Max(0.5f, head.position.y - 0.4f);
            awayTime = (want - home).sqrMagnitude > 0.2f ? awayTime + dt : 0f;
            if (awayTime > 1.2f) { home = want; awayTime = 0f; }
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!IsHeld) FollowPlayer(dt);
            if (IsHeld && holder != null)
            {
                transform.position = holder.GrabPoint;
                heldTime += dt;
            }
            else
            {
                freeTime += dt;
                // The glass rings when a thrown orb reaches it, and sends the orb back.
                Vector3 p0 = transform.position;
                float rad = new Vector2(p0.x, p0.z).magnitude;
                if (!Body.isKinematic && rad > Nave.Radius - 0.35f && p0.y < Nave.WallHeight + 3f)
                {
                    Vector3 n = new Vector3(-p0.x, 0f, -p0.z).normalized;
                    Vector3 vel = Body.linearVelocity;
                    if (Vector3.Dot(vel, n) < 0f)
                    {
                        Body.linearVelocity = Vector3.Reflect(vel, n) * 0.6f;
                        var host = CathedralAudio.Instance;
                        if (host != null && host.Dsp != null) host.Dsp.OneShot(0, Frequency * 2f, 0.3f, PanFor(p0));
                        if (SeedGarden.Instance != null) SeedGarden.Instance.Strike(p0, 0.7f);
                    }
                }
                bool tooFar = (transform.position - home).sqrMagnitude > 144f || transform.position.y < 0.06f;
                if (freeTime > returnDelay || tooFar)
                {
                    if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.isKinematic = true; homeVel = Vector3.zero; }
                    Vector3 rest = home + Vector3.up * (Mathf.Sin(Time.time * 0.9f + orbIndex * 2.1f) * 0.025f);
                    // A free hand coming close makes the orb lean towards it and swell: "take me".
                    invite = 0f;
                    var hands = OrbGrabber.All;
                    for (int i = 0; i < hands.Count; i++)
                    {
                        if (hands[i].Holding) continue;
                        Vector3 to = hands[i].GrabPoint - rest;
                        float k = Mathf.Clamp01(1f - (to.magnitude - 0.1f) / 0.3f);
                        if (k > invite) { invite = k; lean = to.normalized * (0.05f * k); }
                    }
                    rest += lean * (invite > 0f ? 1f : 0f);
                    transform.position = Vector3.SmoothDamp(transform.position, rest, ref homeVel, 0.55f);
                    if (Trail != null && Trail.emitting && (transform.position - rest).sqrMagnitude < 0.01f) Trail.emitting = false;
                }
            }

            Vector3 pos = transform.position;
            Vector3 raw = (pos - lastPos) / dt;
            lastPos = pos;
            Velocity = Vector3.Lerp(Velocity, raw, 0.4f);
            Speed = Mathf.Lerp(Speed, raw.magnitude, 0.25f);
            Brightness = Mathf.Clamp01(0.12f + Speed * 0.35f);
            Fast = Speed > 0.9f;
            if (IsHeld)
            {
                NoteIndex = NoteForHeight(pos.y);
                Height01 = Mathf.InverseLerp(MinHeight, MaxHeight, pos.y);
            }

            var audioHost = CathedralAudio.Instance;
            if (audioHost != null && audioHost.Dsp != null)
            {
                audioHost.Dsp.Voices[orbIndex * VoicesPerOrb] = new CathedralDsp.VoiceParams
                {
                    Freq = Frequency, Gain = IsHeld ? liveGain * (0.7f + 0.3f * Mathf.Clamp01(Speed)) : 0f,
                    Bright = Brightness, Pan = PanFor(pos), Gate = IsHeld, Fast = Fast,
                };
            }

            var an = SpectrumAnalyzer.Instance;
            float pulse = an != null ? an.BeatPulse : 0f;
            bool flying = !IsHeld && !Body.isKinematic;
            Color c = IsHeld || flying ? NoteColor : Color.HSVToRGB(baseHue, 0.75f, 1f);
            if (IsHeld || flying) invite = 0f;
            float glow = IsHeld ? 1.3f + pulse * 0.9f : flying ? 1.1f : 0.55f + pulse * 0.25f + invite * 0.9f;
            transform.localScale = baseScale * (1f + (IsHeld ? 0.22f : 0.08f) * pulse + invite * 0.3f);
            if (core != null) { c.a = glow; mpb.SetColor(ColorId, c); core.SetPropertyBlock(mpb); }
            if (halo != null) { c.a = glow * 0.45f; mpb.SetColor(ColorId, c); halo.SetPropertyBlock(mpb); }
            if (Trail != null) { c.a = 1f; Trail.startColor = c; Trail.endColor = new Color(c.r, c.g, c.b, 0f); }

            var field = FeedbackField.Instance;
            if (field != null && (IsHeld || flying))
                field.AddSplat(pos, NoteColor, IsHeld ? 1.3f + Mathf.Min(Speed, 2f) * 0.9f : 0.9f, IsHeld ? 0.055f : 0.04f);
        }
    }
}
