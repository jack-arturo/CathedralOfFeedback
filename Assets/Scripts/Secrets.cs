using System.Collections;
using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// Eight secrets, eight columns. Each column carries a gem at head height: a faint grey
    /// ember until its secret has been found, then lit in its own colour for as long as this
    /// wearer plays. They show that there is more to find without saying what. Light all eight
    /// and the cathedral plays its finale. A new wearer starts with them dark again.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class Secrets : MonoBehaviour
    {
        public const int Count = 8;
        public const int StringDrop = 0, Harvest = 1, CatchDrop = 2, Rally = 3, Bloom = 4, Supernova = 5, OrbClap = 6, Whirl = 7;
        static readonly string[] Names = { "string drop", "harvest", "catch drop", "column rally", "bloom", "supernova", "orb clap", "whirl" };
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Secrets self;

        [SerializeField] Material gemMaterial, haloMaterial;

        readonly bool[] found = new bool[Count];
        readonly float[] flare = new float[Count];
        Transform[] gems; Renderer[] cores, halos;
        MaterialPropertyBlock mpb;
        int foundCount; bool finale; float unworn;

        /// <summary>The player did one of the eight things. The first time, its gem lights.</summary>
        public static void Found(int id) { if (self != null) self.Mark(id); }

        static Vector3 GemPoint(int k)
        {
            Vector3 c = ColumnDrum.PositionOf(k);
            return c - c.normalized * 0.42f + Vector3.up * 1.9f;        // on the face of the column that looks at the middle
        }
        static Color GemColor(int k) => Color.HSVToRGB(k / (float)Count, 0.85f, 1f);

        void Awake()
        {
            self = this;
            mpb = new MaterialPropertyBlock();
            gems = new Transform[Count]; cores = new Renderer[Count]; halos = new Renderer[Count];
            for (int k = 0; k < Count; k++)
            {
                var g = Sphere("Secret" + k, transform, gemMaterial);
                g.transform.position = GemPoint(k); g.transform.localScale = Vector3.one * 0.1f;
                var h = Sphere("Halo", g.transform, haloMaterial);
                h.transform.localPosition = Vector3.zero; h.transform.localScale = Vector3.one * 3f;
                gems[k] = g.transform; cores[k] = g.GetComponent<Renderer>(); halos[k] = h.GetComponent<Renderer>();
            }
        }

        void OnDestroy() { if (self == this) self = null; }

        static GameObject Sphere(string name, Transform parent, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = name; g.transform.SetParent(parent, false);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g;
        }

        void Mark(int id)
        {
            if (id < 0 || id >= Count || found[id]) return;
            found[id] = true; foundCount++; flare[id] = 1f;
            Vector3 p = GemPoint(id); Color c = GemColor(id);
            var garden = SeedGarden.Instance;
            if (garden != null)
            {
                // A bolt of sparks from the player to the gem, so the eye follows it there.
                Transform head = FeedbackField.Instance != null ? FeedbackField.Instance.viewer : null;
                if (head != null)
                {
                    Vector3 from = head.position + head.forward * 0.4f - Vector3.up * 0.3f;
                    for (int j = 0; j < 12; j++) garden.Burst(Vector3.Lerp(from, p, j / 11f), c, 3, 0.4f);
                }
                garden.Burst(p, c, 50, 3f);
                garden.Ring(p, c, 40, 2.5f);
                float pan = GrabbableOrb.PanFor(p);
                garden.PlayLater(0.15f, 0, 880f, 0.2f, pan);
                garden.PlayLater(0.27f, 0, 1318.5f, 0.2f, pan);
                garden.PlayLater(0.39f, 0, 1760f, 0.2f, pan);
            }
            CathedralDirector.Stir(0.2f);
            Debug.Log($"[Cathedral] secret found: {Names[id]} ({foundCount}/{Count})");
            if (Narrator.Instance != null) Narrator.Instance.OnSecret(foundCount, foundCount == Count);
            if (foundCount == Count && !finale) { finale = true; StartCoroutine(Finale()); }
        }

        /// <summary>All eight: everything the building can do, at once.</summary>
        IEnumerator Finale()
        {
            yield return new WaitForSeconds(2.2f);
            Debug.Log("[Cathedral] finale: all eight secrets found");
            var dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
            if (dsp != null) { dsp.Drop(); dsp.Overdrive(16); }
            CathedralDirector.Stir(1f);
            if (FeedbackField.Instance != null) FeedbackField.Instance.Flash(1f);
            if (CometPool.Instance != null) CometPool.Instance.ForceBloom();
            if (Narrator.Instance != null) Narrator.Instance.OnDrop();
            var wait = new WaitForSeconds(0.25f);
            for (int n = 0; n < 24; n++)
            {
                int k = n % Count;
                Vector3 p = GemPoint(k);
                flare[k] = 1f;
                var garden = SeedGarden.Instance;
                if (garden != null)
                {
                    garden.Burst(p + Vector3.up * Random.Range(0f, 3f), GemColor(k), 30, 3.5f);
                    if ((n & 3) == 0) garden.Burst(new Vector3(0f, Nave.LanternHeight, 0f), Color.white, 60, 6f);
                }
                dsp = CathedralAudio.Instance != null ? CathedralAudio.Instance.Dsp : null;
                if (dsp != null) dsp.OneShot(0, ColumnDrum.FrequencyOf(k) * 4f, 0.2f, GrabbableOrb.PanFor(p));
                yield return wait;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // The next wearer gets to find them all again.
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            unworn = worn ? 0f : unworn + dt;
            if (unworn > 20f && foundCount > 0)
            {
                for (int k = 0; k < Count; k++) found[k] = false;
                foundCount = 0; finale = false;
            }

            var an = SpectrumAnalyzer.Instance;
            float pulse = an != null ? an.BeatPulse : 0f;
            for (int k = 0; k < Count; k++)
            {
                flare[k] = Mathf.MoveTowards(flare[k], 0f, dt);
                Color c; float glow;
                if (found[k])
                {
                    // Once all eight are lit the colours chase each other round the ring.
                    c = finale ? Color.HSVToRGB(Mathf.Repeat(k / (float)Count + Time.time * 0.15f, 1f), 0.85f, 1f) : GemColor(k);
                    glow = 1.1f + 0.6f * pulse + 2f * flare[k];
                }
                else
                {
                    c = new Color(0.6f, 0.7f, 1f);
                    glow = 0.2f + 0.07f * Mathf.Sin(Time.time * 1.5f + k);
                }
                gems[k].localScale = Vector3.one * (0.1f * (1f + 0.8f * flare[k] + (found[k] ? 0.15f * pulse : 0f)));
                c.a = glow; mpb.SetColor(ColorId, c); cores[k].SetPropertyBlock(mpb);
                c.a = glow * 0.4f; mpb.SetColor(ColorId, c); halos[k].SetPropertyBlock(mpb);
            }
        }
    }
}
