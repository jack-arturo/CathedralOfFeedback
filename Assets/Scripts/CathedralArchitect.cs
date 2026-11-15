using UnityEngine;

namespace CathedralOfFeedback
{
    /// <summary>Generates the building's meshes on start and publishes its proportions to the shaders.</summary>
    [DefaultExecutionOrder(-300)]
    public class CathedralArchitect : MonoBehaviour
    {
        public MeshFilter shell, stone, floor, shafts;
        static readonly int ZonesId = Shader.PropertyToID("_CathedralZones");

        void Awake() { Build(); }

        public void Build()
        {
            if (shell != null) shell.sharedMesh = Nave.BuildShell();
            if (stone != null) stone.sharedMesh = Nave.BuildStone();
            if (floor != null) floor.sharedMesh = Nave.BuildFloor();
            if (shafts != null) shafts.sharedMesh = Nave.BuildShafts();
            Shader.SetGlobalVector(ZonesId, new Vector4(Nave.ZoneSpring, Nave.ZoneMid, Nave.ZoneTop, Nave.ThetaMax));
        }
    }
}
