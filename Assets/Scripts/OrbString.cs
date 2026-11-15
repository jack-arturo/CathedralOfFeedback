using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Hold both orbs and a string of light joins them. It is drawn into the feedback field, so
    /// it sweeps kaleidoscope lines across the vault. Stretch it for a build-up (the kick drops
    /// out, a snare roll and a riser come in, the light climbs faster); bring the orbs back
    /// together for the drop.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    [DefaultExecutionOrder(30)]
    public class OrbString : MonoBehaviour
    {
        const int Points = 24;
        LineRenderer line;
        float build, armedTime = -10f, dropCooldown;
        readonly Vector3[] pts = new Vector3[Points];

        /// <summary>0..1: how far the string is stretched.</summary>
        public float Build => build;
        public static OrbString Instance { get; private set; }

        void Awake() { Instance = this; line = GetComponent<LineRenderer>(); line.positionCount = Points; line.enabled = false; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var orbs = GrabbableOrb.All;
            GrabbableOrb a = null, b = null;
            for (int i = 0; i < orbs.Count; i++) if (orbs[i].IsHeld) { if (a == null) a = orbs[i]; else b = orbs[i]; }
            var audioHost = CathedralAudio.Instance;
            var dsp = audioHost != null ? audioHost.Dsp : null;
            var field = FeedbackField.Instance;
            var an = SpectrumAnalyzer.Instance;
            dropCooldown -= dt;

            float target = 0f;
            if (a != null && b != null)
            {
                Vector3 pa = a.transform.position, pb = b.transform.position;
                float d = (pa - pb).magnitude;
                target = Mathf.Clamp01((d - 0.35f) / 0.85f);
                if (target > 0.6f) armedTime = Time.time;

                // The drop: a stretched string brought back together.
                if (d < 0.3f && Time.time - armedTime < 2.5f && dropCooldown <= 0f)
                {
                    armedTime = -10f; dropCooldown = 1.5f;
                    if (dsp != null) dsp.Drop();
                    if (field != null) field.Flash(1f);
                    if (SeedGarden.Instance != null) SeedGarden.Instance.Strike((pa + pb) * 0.5f, 1f);
                    Debug.Log("[Cathedral] drop");
                }

                // The string itself: a standing wave that tightens as it stretches.
                float pulse = an != null ? an.BeatPulse : 0f;
                Vector3 axis = (pb - pa).normalized;
                Vector3 side = Vector3.Cross(axis, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
                Vector3 up = Vector3.Cross(side, axis);
                float amp = (0.012f + 0.03f * pulse) * (1f - 0.7f * build) * Mathf.Min(1f, d * 2f);
                for (int i = 0; i < Points; i++)
                {
                    float u = i / (float)(Points - 1);
                    float env = Mathf.Sin(u * Mathf.PI);
                    float w = Mathf.Sin(u * Mathf.PI * 3f + Time.time * 14f) * amp * env;
                    float w2 = Mathf.Cos(u * Mathf.PI * 2f - Time.time * 9f) * amp * env * 0.6f;
                    pts[i] = Vector3.Lerp(pa, pb, u) + side * w + up * w2;
                }
                line.SetPositions(pts);
                Color ca = a.NoteColor, cb = b.NoteColor;
                line.startColor = ca; line.endColor = cb;
                line.widthMultiplier = 0.008f + 0.012f * pulse + 0.014f * build;
                line.enabled = true;

                if (field != null)
                    for (int i = 1; i <= 4; i++)
                    {
                        float u = i / 5f;
                        field.AddSplat(Vector3.Lerp(pa, pb, u), Color.Lerp(ca, cb, u), 0.8f + build, 0.035f);
                    }
            }
            else if (line.enabled) line.enabled = false;

            build = Mathf.MoveTowards(build, target, dt * (target > build ? 3f : 1.5f));
            if (dsp != null) dsp.Build = build;
            if (field != null) field.Surge = build;
        }
    }
}
