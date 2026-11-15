using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Records the orb's path while held and replays it as pooled "echo" ghosts delayed by whole
    /// beats (1..N). Each ghost pulses on the beat, so the trail answers back in time with the music.
    /// Fixed-size ring buffer and ghost pool: no allocations at runtime.
    /// </summary>
    [RequireComponent(typeof(GrabbableOrb))]
    public class TrailEcho : MonoBehaviour
    {
        [SerializeField] Material ghostMaterial;
        [SerializeField, Range(1, 6)] int echoCount = 4;
        [SerializeField] float ghostSize = 0.12f;

        const int Capacity = 512;     // ~8.5 s at 60 Hz
        readonly Vector3[] pos = new Vector3[Capacity];
        readonly float[] time = new float[Capacity];
        int head, count;
        Transform[] ghosts;
        float beatPulse;

        void Start()
        {
            ghosts = new Transform[echoCount];
            for (int i = 0; i < echoCount; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.name = "Echo" + (i + 1);
                g.transform.localScale = Vector3.one * ghostSize;
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = ghostMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                g.SetActive(false);
                ghosts[i] = g.transform;
            }
            if (SpectrumAnalyzer.Instance != null) SpectrumAnalyzer.Instance.OnBeat += OnBeat;
        }

        void OnDestroy() { if (SpectrumAnalyzer.Instance != null) SpectrumAnalyzer.Instance.OnBeat -= OnBeat; }

        void OnBeat(int _) { beatPulse = 1f; }

        void LateUpdate()
        {
            var orb = GetComponent<GrabbableOrb>();
            if (orb.IsHeld)
            {
                pos[head] = transform.position; time[head] = Time.time;
                head = (head + 1) % Capacity; if (count < Capacity) count++;
            }
            if (count == 0 || SpectrumAnalyzer.Instance == null) return;

            beatPulse = Mathf.MoveTowards(beatPulse, 0f, Time.deltaTime * 4f);
            float interval = SpectrumAnalyzer.Instance.BeatInterval;
            for (int i = 0; i < ghosts.Length; i++)
            {
                float t = Time.time - interval * (i + 1);
                bool ok = TrySample(t, out var p);
                var g = ghosts[i];
                if (g.gameObject.activeSelf != ok) g.gameObject.SetActive(ok);
                if (!ok) continue;
                g.position = p;
                g.localScale = Vector3.one * ghostSize * (0.6f + beatPulse * 0.8f) * (1f - i * 0.12f);
            }
        }

        bool TrySample(float t, out Vector3 p)
        {
            p = default;
            // Walk back from newest; buffer is small so linear scan is fine.
            for (int n = 0; n < count - 1; n++)
            {
                int a = (head - 1 - n + Capacity) % Capacity;
                int b = (a - 1 + Capacity) % Capacity;
                if (time[b] <= t && t <= time[a])
                {
                    float k = Mathf.InverseLerp(time[b], time[a], t);
                    p = Vector3.Lerp(pos[b], pos[a], k);
                    return true;
                }
            }
            return false;
        }
    }
}
