using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>
    /// The floor answers your feet. The tile you are standing on is lit, and each step onto a
    /// new tile plays a soft note and sends a ring out across the floor, so walking the nave is
    /// playing it too. There is no foot tracking on the headset: the feet are taken to be under
    /// the head, which is right to within a tile.
    /// </summary>
    [DefaultExecutionOrder(25)]
    public class FloorLights : MonoBehaviour
    {
        /// <summary>Tile size in metres. The floor material is built with the same value.</summary>
        public const float Tile = 0.8f;

        static readonly int StepId = Shader.PropertyToID("_CathedralStep");
        static readonly int FeetId = Shader.PropertyToID("_CathedralFeet");
        static readonly int StepColorId = Shader.PropertyToID("_CathedralStepColor");
        static readonly int[] Scale = { 0, 3, 5, 7, 10 };

        Vector2Int tile; bool hasTile;
        Vector3 stepPos; float stepAge = 10f, sinceNote = 1f, lit;
        Color stepColor = Color.white;

        void Update()
        {
            float dt = Time.deltaTime;
            var field = FeedbackField.Instance;
            Transform head = field != null ? field.viewer : null;
            if (head == null) return;
            stepAge += dt; sinceNote += dt;
            bool worn = Application.isEditor || OVRPlugin.userPresent;
            lit = Mathf.MoveTowards(lit, worn ? 1f : 0f, dt * 2f);

            // The floor is a diamond checkerboard: tiles are squares in (x + z, x - z).
            Vector3 p = head.position;
            Vector2 tg = new Vector2(p.x + p.z, p.x - p.z) / Tile;
            var now = new Vector2Int(Mathf.FloorToInt(tg.x), Mathf.FloorToInt(tg.y));
            // Swaying on a tile edge should not chatter: change tile only once well onto the next one.
            Vector2 off = tg - new Vector2(tile.x + 0.5f, tile.y + 0.5f);
            bool moved = !hasTile || Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) > 0.62f;
            if (moved && now != tile || !hasTile)
            {
                bool first = !hasTile;
                tile = now; hasTile = true;
                float a = (tile.x + 0.5f) * Tile, b = (tile.y + 0.5f) * Tile;
                stepPos = new Vector3((a + b) * 0.5f, 0f, (a - b) * 0.5f);
                int degree = ((tile.x + tile.y * 2) % Scale.Length + Scale.Length) % Scale.Length;
                stepColor = GrabbableOrb.ColorForNote(degree * 2, 0.55f);
                if (!first && worn)
                {
                    stepAge = 0f;
                    var host = CathedralAudio.Instance;
                    if (sinceNote > 0.16f && host != null && host.Dsp != null)
                    {
                        sinceNote = 0f;
                        host.Dsp.OneShot(0, 220f * Mathf.Pow(2f, Scale[degree] / 12f), 0.085f, 0f);
                    }
                }
            }

            Shader.SetGlobalVector(StepId, new Vector4(stepPos.x, stepPos.z, stepAge, lit));
            Shader.SetGlobalVector(FeetId, new Vector4(p.x, p.z, 0f, 0f));
            Shader.SetGlobalColor(StepColorId, stepColor);
        }
    }
}
