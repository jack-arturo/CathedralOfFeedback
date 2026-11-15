using System.Collections.Generic;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// A playable orb. While held, height picks a pentatonic note and hand speed opens the
    /// filter; the synth plucks it on the beat grid so anything you do is in time.
    ///
    /// The orb never leaves you. Hold it still for half a second and it buds a note that stays
    /// hanging in the air, tied back to the orb by a thread (see <see cref="SeedGarden"/>). Throw
    /// it and what flies is a comet of its light (see <see cref="CometPool"/>) while the orb
    /// itself re-forms by your hand, so you can keep playing, or keep throwing. Its movement is
    /// replayed by <see cref="TrailEcho"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableOrb : MonoBehaviour
    {
        public static readonly List<GrabbableOrb> All = new List<GrabbableOrb>();
        public const int VoicesPerOrb = 5;
        const float ThrowSpeed = 0.9f, DwellSeconds = 0.55f, DwellRadius = 0.03f, PlantSpacing = 0.16f;
        static readonly int[] Scale = { 0, 3, 5, 7, 10, 12, 15, 17, 19, 22 };   // A minor pentatonic, two octaves
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Standing eye height of the current player; the note range hangs off it so the
        /// instrument fits a child, a seated player, or a headset with a bad floor estimate.</summary>
        public static float PlayerHeight = 1.6f;
        public static float MinHeight => PlayerHeight - 1.0f;      // about knee height
        public static float MaxHeight => PlayerHeight + 0.4f;      // a comfortable reach overhead

        [Tooltip("0 or 1: which block of five synth voices (one live, four echoes) this orb owns.")]
        public int orbIndex;
        public float rootHz = 220f;
        [Range(0, 1)] public float baseHue = 0.09f;
        public float liveGain = 0.42f;
        public Renderer core, halo;
        public TrailRenderer Trail;
        [Tooltip("Resting place relative to the player: metres to the right (negative = left).")]
        public float sideOffset = 0.22f;

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
        Vector3 home, lastPos, homeVel, baseScale, haloScale = Vector3.one, lean, lastPlant = Vector3.one * 999f, dwellAnchor;
        float awayTime, invite, heldTime, dwell, ring, reform = 1f;
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
            // The orb is moved by hand, never by physics: what gets thrown is a comet.
            GetComponent<Rigidbody>().isKinematic = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (Trail == null) Trail = GetComponentInChildren<TrailRenderer>();
            if (Trail != null) Trail.emitting = false;
            home = lastPos = transform.position;
            baseScale = transform.localScale;
            if (halo != null) haloScale = halo.transform.localScale;
            mpb = new MaterialPropertyBlock();
            NoteIndex = NoteForHeight(home.y);
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Grab(OrbGrabber hand)
        {
            holder = hand; IsHeld = true; heldTime = 0f; dwell = 0f; dwellAnchor = transform.position;
            lastPlant = Vector3.one * 999f;
            if (Trail != null) Trail.emitting = true;
        }

        /// <summary>The orb's light came back and a hand met it: the orb is in that hand now.</summary>
        public void CatchInto(OrbGrabber hand)
        {
            Vector3 p = hand.GrabPoint;
            transform.position = p; lastPos = p; homeVel = Vector3.zero;
            Velocity = Vector3.zero; Speed = 0f;
            if (Trail != null) Trail.Clear();
            Grab(hand);
            lastPlant = p;              // a catch is not a place to leave a note
            ring = 1f; reform = 0.5f;
        }

        public void Release(bool allowPlant = true)
        {
            IsHeld = false; holder = null; dwell = 0f;
            Vector3 pos = transform.position;
            if (Velocity.magnitude >= ThrowSpeed)
            {
                // A throw: a comet of this orb's light flies off, and the orb re-forms at its
                // resting place straight away, so the player's hands are never empty for long.
                CometPool.Launch(pos, Velocity * 1.35f, NoteColor, Frequency, this);
                if (Trail != null) { Trail.emitting = false; Trail.Clear(); }
                transform.position = home; homeVel = Vector3.zero; lastPos = home;
                reform = 0f;
            }
            else if (allowPlant && heldTime > 0.35f && (pos - lastPlant).sqrMagnitude > PlantSpacing * PlantSpacing)
            {
                Plant();            // a gentle let-go leaves the note behind too
            }
        }

        void Plant()
        {
            if (SeedGarden.Instance == null) return;
            SeedGarden.Instance.Plant(transform.position, NoteIndex, Frequency, baseHue, this);
            lastPlant = transform.position; dwell = 0f; ring = 1f;
        }

        /// <summary>One of this orb's planted notes just sounded.</summary>
        public void Ring() { ring = 1f; }

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
            FollowPlayer(dt);
            if (IsHeld && holder != null)
            {
                transform.position = holder.GrabPoint;
                heldTime += dt;
            }
            else
            {
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
                if (invite > 0f) rest += lean;
                transform.position = Vector3.SmoothDamp(transform.position, rest, ref homeVel, 0.3f);
                if (Trail != null && Trail.emitting && (transform.position - rest).sqrMagnitude < 0.01f) Trail.emitting = false;
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

                // Hold still and the orb buds a note where it is; move a hand's width on before the
                // next. Not while both orbs are held: then the hands are busy with the string.
                int heldCount = 0;
                for (int i = 0; i < All.Count; i++) if (All[i].IsHeld) heldCount++;
                // "Still" means staying inside a small sphere, which hand-tracking jitter cannot break.
                bool room = (pos - lastPlant).sqrMagnitude > PlantSpacing * PlantSpacing;
                if ((pos - dwellAnchor).sqrMagnitude > DwellRadius * DwellRadius) { dwellAnchor = pos; dwell = 0f; }
                else if (room && heldTime > 0.3f && heldCount < 2) dwell += dt;
                else dwell = 0f;
                if (dwell >= DwellSeconds) Plant();
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
            ring = Mathf.MoveTowards(ring, 0f, dt * 3f);
            reform = Mathf.MoveTowards(reform, 1f, dt / 0.3f);
            float charge = Mathf.Clamp01(dwell / DwellSeconds);
            if (IsHeld) invite = 0f;

            Color c = IsHeld ? NoteColor : Color.HSVToRGB(baseHue, 0.75f, 1f);
            float glow = (IsHeld ? 1.3f + pulse * 0.9f + charge * 1.2f : 0.55f + pulse * 0.25f + invite * 0.9f) + ring * 1.4f;
            float grow = Mathf.SmoothStep(0.15f, 1f, reform);
            transform.localScale = baseScale * (grow * (1f + (IsHeld ? 0.22f : 0.08f) * pulse + invite * 0.3f + ring * 0.25f));
            if (core != null) { c.a = glow; mpb.SetColor(ColorId, c); core.SetPropertyBlock(mpb); }
            if (halo != null)
            {
                // The halo draws in as a note charges, then snaps back out when it buds.
                halo.transform.localScale = haloScale * (1f - 0.55f * charge);
                c.a = glow * 0.45f * (1f + charge); mpb.SetColor(ColorId, c); halo.SetPropertyBlock(mpb);
            }
            if (Trail != null) { c.a = 1f; Trail.startColor = c; Trail.endColor = new Color(c.r, c.g, c.b, 0f); }

            var field = FeedbackField.Instance;
            if (field != null && IsHeld)
                field.AddSplat(pos, NoteColor, 1.3f + Mathf.Min(Speed, 2f) * 0.9f, 0.055f);
        }
    }
}
