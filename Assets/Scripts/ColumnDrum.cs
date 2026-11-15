using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>Marks a collider as one of the canopy columns. A thrown orb that strikes it plays a drum tuned by <see cref="index"/>.</summary>
    public class ColumnDrum : MonoBehaviour
    {
        public int index;
        static readonly int[] Tuning = { 0, 3, 5, 7, 10, 12, 15, 17 };     // A minor pentatonic, like the orbs
        public float Frequency => 110f * Mathf.Pow(2f, Tuning[Mathf.Abs(index) % Tuning.Length] / 12f);
    }
}
