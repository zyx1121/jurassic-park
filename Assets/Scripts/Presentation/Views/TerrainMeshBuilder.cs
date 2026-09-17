using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Turns the map into one mesh: a flat quad per walkable cell and a raised block per cliff cell, coloured per vertex.
    /// One mesh and one material means one draw call for the whole terrain, which is what a MacBook Air wants.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public static Mesh Build(MapDefinition map, Color ground, Color noBuild, Color cliff, float cliffHeight)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            float s = map.CellSize;
            // Vertex colours are not converted by the pipeline. The project renders in linear space, so authored (sRGB) colours are converted here once.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                ground = ground.linear;
                noBuild = noBuild.linear;
                cliff = cliff.linear;
            }

            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var cell = new Cell(x, y);
                    bool walkable = map.IsStaticWalkable(cell);
                    float top = walkable ? 0f : cliffHeight;
                    Color color = walkable ? (map.IsStaticBuildable(cell) ? ground : noBuild) : cliff;
                    // Two tones in a checker so distances read at a glance from the fixed camera.
                    if (((x + y) & 1) == 0) color *= 0.94f;
                    float x0 = x * s, x1 = x0 + s, z0 = y * s, z1 = z0 + s;
                    Quad(vertices, colors, triangles, color,
                        new Vector3(x0, top, z0), new Vector3(x0, top, z1), new Vector3(x1, top, z1), new Vector3(x1, top, z0));
                    if (walkable) continue;

                    // Cliff sides only where they face walkable ground; hidden faces are wasted triangles.
                    Color side = color * 0.7f;
                    if (map.IsStaticWalkable(new Cell(x, y - 1)))
                        Quad(vertices, colors, triangles, side, new Vector3(x0, 0, z0), new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, 0, z0));
                    if (map.IsStaticWalkable(new Cell(x, y + 1)))
                        Quad(vertices, colors, triangles, side, new Vector3(x1, 0, z1), new Vector3(x1, top, z1), new Vector3(x0, top, z1), new Vector3(x0, 0, z1));
                    if (map.IsStaticWalkable(new Cell(x - 1, y)))
                        Quad(vertices, colors, triangles, side, new Vector3(x0, 0, z1), new Vector3(x0, top, z1), new Vector3(x0, top, z0), new Vector3(x0, 0, z0));
                    if (map.IsStaticWalkable(new Cell(x + 1, y)))
                        Quad(vertices, colors, triangles, side, new Vector3(x1, 0, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x1, 0, z1));
                }
            }

            var mesh = new Mesh { name = "Terrain" };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Quad(List<Vector3> vertices, List<Color> colors, List<int> triangles, Color color, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
    }
}
