using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>Marks a collider as one of the eight canopy columns. A comet that strikes it plays a drum tuned by <see cref="index"/>.</summary>
    public class ColumnDrum : MonoBehaviour
    {
        public int index;
        static readonly int[] Tuning = { 0, 3, 5, 7, 10, 12, 15, 17 };     // A minor pentatonic, like the orbs

        public float Frequency => FrequencyOf(index);
        public static float FrequencyOf(int i) => 110f * Mathf.Pow(2f, Tuning[Mathf.Abs(i) % Tuning.Length] / 12f);

        /// <summary>Foot of column i on the floor.</summary>
        public static Vector3 PositionOf(int i)
        {
            float phi = 2f * Mathf.PI * (i + 0.5f) / 8f;
            return new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi)) * Nave.CanopyRadius;
        }
    }
}
