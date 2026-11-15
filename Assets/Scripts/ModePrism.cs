using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// A small glowing prism that floats at the player's left hip. Poke it with a fingertip (or
    /// touch it with a controller) to step to the next room mode. Its colour shows the current
    /// mode. Touching a thing is the one hand interaction that needs no explaining.
    /// </summary>
    [DefaultExecutionOrder(45)]
    public class ModePrism : MonoBehaviour
    {
        public CathedralDirector director;
        public Renderer body;
        [Tooltip("Resting place relative to the player: metres right (negative = left), forward, and below the eyes.")]
        public Vector3 offset = new Vector3(-0.44f, 0.34f, 0.52f);
        public float touchRadius = 0.075f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color[] ModeColors =
        {
            new Color(0.55f, 0.75f, 1f),     // Virtual
            new Color(0.2f, 1f, 0.6f),       // Windows
            new Color(1f, 0.6f, 0.15f),      // Open
            new Color(1f, 0.25f, 0.8f),      // RoomInGlass
        };

        Vector3 home, vel;
        float awayTime, cooldown, flash;
        bool armed = true;
        MaterialPropertyBlock mpb;

        void Awake() { home = transform.position; mpb = new MaterialPropertyBlock(); }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var field = FeedbackField.Instance;
            Transform head = field != null ? field.viewer : null;
            if (head != null)
            {
                Vector3 f = head.forward; f.y = 0f;
                if (f.sqrMagnitude > 0.04f)
                {
                    f.Normalize();
                    Vector3 right = new Vector3(f.z, 0f, -f.x);
                    Vector3 want = head.position + right * offset.x + f * offset.y;
                    want.y = Mathf.Max(0.4f, head.position.y - offset.z);
                    awayTime = (want - home).sqrMagnitude > 0.2f ? awayTime + dt : 0f;
                    if (awayTime > 1.2f) { home = want; awayTime = 0f; }
                }
            }
            Vector3 rest = home + Vector3.up * (Mathf.Sin(Time.time * 1.3f) * 0.015f);
            transform.position = Vector3.SmoothDamp(transform.position, rest, ref vel, 0.5f);
            transform.Rotate(new Vector3(20f, 45f, 10f) * dt, Space.World);

            cooldown -= dt;
            bool touching = false, near = false;
            var hands = OrbGrabber.All;
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i].Holding) continue;
                float d = (hands[i].TouchPoint - transform.position).magnitude;
                if (d < touchRadius) touching = true;
                if (d < touchRadius * 2f) near = true;
            }
            if (touching && armed && cooldown <= 0f && director != null)
            {
                director.CycleMode();
                armed = false; cooldown = 0.7f; flash = 1f;
            }
            if (!near) armed = true;

            flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);
            if (body != null)
            {
                Color c = ModeColors[director != null ? Mathf.Clamp(director.ModeIndex, 0, ModeColors.Length - 1) : 0];
                c.a = 0.9f + flash * 2.5f + (near ? 0.5f : 0f);
                mpb.SetColor(ColorId, c);
                body.SetPropertyBlock(mpb);
            }
            transform.localScale = Vector3.one * (0.05f * (1f + flash * 0.6f + (near ? 0.15f : 0f)));
        }
    }
}
