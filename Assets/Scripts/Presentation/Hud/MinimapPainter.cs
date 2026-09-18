using System;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The minimap's pixels: the map's walkable and blocked cells under the local team's fog, and the colour of a dot per
    /// entity. It paints into a buffer and only when the fog's revision moves, so the texture is uploaded rarely and a still
    /// army costs nothing. Plain C#, so an EditMode test can check a pixel without a scene, and it reads nothing but the read
    /// model: what the team has not seen is not in the buffer at all.
    /// </summary>
    public sealed class MinimapPainter
    {
        /// <summary>Fog classes as the snapshot stores them, the same bytes <see cref="FogView"/> reads.</summary>
        public const byte Unexplored = 0, Explored = 1, Visible = 2;

        public static readonly Color32 Blocked = new Color32(52, 56, 62, 255);
        public static readonly Color32 Ground = new Color32(74, 97, 68, 255);
        public static readonly Color32 Dark = new Color32(0, 0, 0, 255);

        /// <summary>How much of the terrain colour is left where the team has been but cannot see right now.</summary>
        public const float ExploredDim = 0.45f;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Row major, the map's own order: index 0 is the cell at x 0, y 0, which a texture draws at the bottom left.</summary>
        public Color32[] Pixels { get; private set; } = Array.Empty<Color32>();

        /// <summary>The fog revision the buffer was painted for. <see cref="long.MinValue"/> until the first paint.</summary>
        public long PaintedRevision { get; private set; } = long.MinValue;

        public bool NeedsPaint(MapDefinition map, FogSnapshot fog) =>
            map != null && (Pixels.Length != map.Width * map.Height || Width != map.Width || PaintedRevision != fog.Revision);

        /// <summary>
        /// Repaints the whole buffer. A fog that does not cover this map is treated as unexplored rather than as clear, so a
        /// client that has not been sent its mask yet shows black instead of the whole layout.
        /// </summary>
        public void Paint(MapDefinition map, FogSnapshot fog)
        {
            if (map == null) return;
            if (Pixels.Length != map.Width * map.Height) Pixels = new Color32[map.Width * map.Height];
            Width = map.Width;
            Height = map.Height;
            bool fogFits = fog != null && fog.Width == map.Width && fog.Height == map.Height && fog.Cells.Length == Pixels.Length;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    Pixels[y * map.Width + x] = Compose(map.IsStaticWalkable(new Cell(x, y)), fogFits ? fog.At(x, y) : Unexplored);
            PaintedRevision = fog?.Revision ?? 0;
        }

        /// <summary>Terrain under fog: unexplored is black, explored keeps a dim memory of the ground, visible is the ground itself.</summary>
        public static Color32 Compose(bool walkable, byte fogCell)
        {
            if (fogCell == Unexplored) return Dark;
            Color32 terrain = walkable ? Ground : Blocked;
            return fogCell == Visible ? terrain : Dim(terrain, ExploredDim);
        }

        /// <summary>
        /// The dot for one thing in the read model: yours green, a team mate's blue, another team's red, an unowned resource
        /// dark green, anything else unowned grey. Something only remembered is drawn faintly, as the picture may be stale.
        /// </summary>
        public static Color32 DotOf(MatchReadModel model, in EntitySnapshot entity)
        {
            Color32 colour;
            if (!entity.Owner.IsNone && entity.Owner == model.LocalSeat) colour = new Color32(90, 225, 105, 255);
            else if (HudModel.IsTeamMate(model, entity.Owner)) colour = new Color32(90, 150, 240, 255);
            else if (!entity.Owner.IsNone) colour = new Color32(220, 70, 60, 255);
            else if (entity.Kind == EntityKind.ResourceNode) colour = new Color32(40, 92, 70, 255);
            else colour = new Color32(170, 160, 140, 255);
            return entity.Remembered ? Dim(colour, 0.6f) : colour;
        }

        private static Color32 Dim(Color32 colour, float factor) =>
            new Color32((byte)(colour.r * factor), (byte)(colour.g * factor), (byte)(colour.b * factor), colour.a);
    }
}
