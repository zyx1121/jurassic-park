using System;
using System.Collections.Generic;

namespace JurassicPark.Presentation
{
    /// <summary>The local team's fog: one byte per map cell (unexplored, explored, visible) in map index order, and a revision that moves when it changes.</summary>
    public sealed class FogSnapshot
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Cells { get; private set; } = Array.Empty<byte>();
        public long Revision { get; private set; }

        public void Set(int width, int height, IReadOnlyList<byte> cells, long revision)
        {
            if (Cells.Length != width * height) Cells = new byte[width * height];
            Width = width;
            Height = height;
            for (int i = 0; i < Cells.Length && i < cells.Count; i++) Cells[i] = cells[i];
            Revision = revision;
        }

        public byte At(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? (byte)0 : Cells[y * Width + x];
    }
}
