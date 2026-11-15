using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The feedback loop that lights the building. Two small render targets ping-pong through
    /// Hidden/Cathedral/FeedbackStep once per frame; every surface shader then reads the result
    /// as a global texture. Orbs, echoes and fingertips call <see cref="AddSplat"/> to write
    /// into it. One 256x1024 blit per frame, independent of eye resolution and of stereo.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FeedbackField : MonoBehaviour
    {
        public static FeedbackField Instance { get; private set; }

        [SerializeField] Material stepMaterial;
        [SerializeField] int width = 256, height = 1024;
        [Tooltip("Seconds for light to climb the wall from floor to springing line.")]
        public float riseSeconds = 4.5f;
        [Tooltip("Wedges per second of spiral on the vault.")]
        public float twist = 0.22f;
        public float decaySeconds = 5f;
        public float warp = 0.0012f;
        [Range(0, 1)] public float echoGain = 0.72f;
        public float sourceGain = 1.5f;
        [Tooltip("Hue drift of travelling light, radians per second.")]
        public float hueRate = 0.16f;
        public Transform viewer;
        /// <summary>0..1: speeds the climb of the light (build-up).</summary>
        public float Surge { get; set; }
        float flash;
        /// <summary>Flood the foot of every wall with white for a moment (the drop).</summary>
        public void Flash(float amount) { flash = Mathf.Max(flash, amount); }

        const int MaxSplats = 16;
        static readonly int FieldId = Shader.PropertyToID("_CathedralField");
        static readonly int FlowId = Shader.PropertyToID("_Flow");
        static readonly int SourceId = Shader.PropertyToID("_Source");
        static readonly int SplatPosId = Shader.PropertyToID("_SplatPos");
        static readonly int SplatColId = Shader.PropertyToID("_SplatCol");

        RenderTexture front, back;
        readonly Vector4[] splatPos = new Vector4[MaxSplats], splatCol = new Vector4[MaxSplats];
        int splats;

        public RenderTexture Texture => front;

        void Awake() { Instance = this; Init(); }
        void OnDestroy() { if (Instance == this) Instance = null; Release(); }

        public void Init()
        {
            if (front != null) return;
            Instance = this;
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float
                    : SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf
                    : RenderTextureFormat.ARGB32;
            front = Make(fmt); back = Make(fmt);
            Shader.SetGlobalTexture(FieldId, front);
        }

        RenderTexture Make(RenderTextureFormat fmt)
        {
            var rt = new RenderTexture(width, height, 0, fmt, RenderTextureReadWrite.Linear)
            {
                useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear,
                wrapModeU = TextureWrapMode.Mirror, wrapModeV = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave, name = "CathedralField"
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt; GL.Clear(false, true, Color.black); RenderTexture.active = prev;
            return rt;
        }

        void Release()
        {
            if (front != null) { front.Release(); DestroyImmediate(front); front = null; }
            if (back != null) { back.Release(); DestroyImmediate(back); back = null; }
        }

        /// <summary>Paint light onto the building in the direction of a world position, as seen by the viewer.</summary>
        public void AddSplat(Vector3 worldPos, Color color, float intensity, float radiusRad)
        {
            if (splats >= MaxSplats || intensity <= 0.001f) return;
            Vector3 origin = viewer != null ? viewer.position : new Vector3(0f, Nave.EyeHeight, 0f);
            Vector3 d = worldPos - origin;
            if (d.sqrMagnitude < 1e-6f) return;
            Vector2 f = Nave.DirToField(d.normalized);
            splatPos[splats] = new Vector4(f.x, f.y, radiusRad, 0f);
            splatCol[splats] = new Vector4(color.r * intensity, color.g * intensity, color.b * intensity, 0f);
            splats++;
        }

        void LateUpdate() { Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (front == null || stepMaterial == null) return;
            dt = Mathf.Clamp(dt, 0.001f, 0.05f);
            for (int i = splats; i < MaxSplats; i++) { splatPos[i] = new Vector4(0, 0, 0.01f, 0); splatCol[i] = Vector4.zero; }

            float wallSpan = 1f - Nave.ZoneSpring;
            var an = SpectrumAnalyzer.Instance;
            float bass = an != null ? an.Bass : 0f, pulse = an != null ? an.BeatPulse : 0f;
            stepMaterial.SetVector(FlowId, new Vector4(
                wallSpan / riseSeconds * dt * (1f + bass * 0.5f + Surge * 2.2f),
                twist * dt,
                warp,
                Mathf.Exp(-dt / decaySeconds)));
            flash = Mathf.MoveTowards(flash, 0f, dt * 1.6f);
            stepMaterial.SetVector(SourceId, new Vector4(sourceGain, hueRate * dt, echoGain, pulse * pulse * 0.35f + flash * 1.6f));
            stepMaterial.SetVectorArray(SplatPosId, splatPos);
            stepMaterial.SetVectorArray(SplatColId, splatCol);

            Graphics.Blit(front, back, stepMaterial);
            var t = front; front = back; back = t;
            Shader.SetGlobalTexture(FieldId, front);
            splats = 0;
        }
    }
}
