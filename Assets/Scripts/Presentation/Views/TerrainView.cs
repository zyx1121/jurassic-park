using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>Builds the terrain mesh from the running match's map at start, so what is drawn can never drift from what is simulated.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TerrainView : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private Color ground = new Color(0.27f, 0.42f, 0.30f);
        [SerializeField] private Color noBuild = new Color(0.64f, 0.67f, 0.42f);
        [SerializeField] private Color cliff = new Color(0.13f, 0.20f, 0.23f);
        [SerializeField] private float cliffHeight = 2.4f;

        private Mesh mesh;

        public void Configure(GameSession gameSession) => session = gameSession;

        private void OnEnable()
        {
            session.MatchBegan += Build;
            if (session.Model != null) Build();
        }

        private void OnDisable() => session.MatchBegan -= Build;

        private void Build()
        {
            if (mesh != null) return;
            mesh = TerrainMeshBuilder.Build(session.Model.Map, ground, noBuild, cliff, cliffHeight);
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
