using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The fog drawn over the ground: one quad per cell in a single mesh, its vertex alpha set from the team's mask (unexplored
    /// dark, explored dim, visible clear). Rebuilt only when the mask's revision moves, so a still army costs nothing.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FogView : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [Range(0f, 1f)] [SerializeField] private float unexploredAlpha = 0.92f;
        [Range(0f, 1f)] [SerializeField] private float exploredAlpha = 0.5f;
        [Tooltip("Height above the ground, so the fog covers cliff tops too.")]
        [SerializeField] private float height = 3.6f;   // above the tallest stand-in (trees reach 3.4 m), so nothing pokes through the dark

        private Mesh mesh;
        private Color[] colors;
        private long shownRevision = -1;
        private int shownWidth = -1, shownHeight = -1;

        public void Configure(GameSession gameSession) => session = gameSession;

        private void LateUpdate()
        {
            MatchReadModel model = session.Model;
            if (model == null) return;
            FogSnapshot fog = model.Fog;
            if (fog.Cells.Length == 0) return;
            if (mesh == null || shownWidth != fog.Width || shownHeight != fog.Height) Build(fog);
            if (fog.Revision == shownRevision) return;
            shownRevision = fog.Revision;
            for (int i = 0; i < fog.Cells.Length; i++)
            {
                float alpha = fog.Cells[i] == 2 ? 0f : fog.Cells[i] == 1 ? exploredAlpha : unexploredAlpha;
                int v = i * 4;
                colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = new Color(0f, 0f, 0f, alpha);
            }
            mesh.SetColors(colors);
        }

        private void Build(FogSnapshot fog)
        {
            if (mesh != null) Destroy(mesh);
            float s = session.Model.Map.CellSize;
            var vertices = new Vector3[fog.Cells.Length * 4];
            var triangles = new int[fog.Cells.Length * 6];
            colors = new Color[vertices.Length];
            for (int y = 0; y < fog.Height; y++)
                for (int x = 0; x < fog.Width; x++)
                {
                    int i = y * fog.Width + x, v = i * 4, t = i * 6;
                    float x0 = x * s, x1 = x0 + s, z0 = y * s, z1 = z0 + s;
                    vertices[v] = new Vector3(x0, height, z0);
                    vertices[v + 1] = new Vector3(x0, height, z1);
                    vertices[v + 2] = new Vector3(x1, height, z1);
                    vertices[v + 3] = new Vector3(x1, height, z0);
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
            mesh = new Mesh { name = "Fog" };
            if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            shownWidth = fog.Width;
            shownHeight = fog.Height;
            shownRevision = -1;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
