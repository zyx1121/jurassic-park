namespace JurassicPark.Simulation
{
    /// <summary>
    /// The eight grid steps in one fixed order. Map validation, pathfinding and approach positions all walk neighbours
    /// through this table, so two cells are "adjacent" in exactly one sense everywhere, and a search that ties on cost
    /// still expands in the same order on every machine.
    /// </summary>
    public static class GridDirections
    {
        /// <summary>X offsets: the four orthogonal steps first, then the four diagonals.</summary>
        public static readonly int[] X = { 1, -1, 0, 0, 1, 1, -1, -1 };

        /// <summary>Y offsets matching <see cref="X"/> by index.</summary>
        public static readonly int[] Y = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public static int Count => X.Length;

        /// <summary>True when the step at this index moves on both axes and therefore costs the diagonal price.</summary>
        public static bool IsDiagonal(int index) => X[index] != 0 && Y[index] != 0;
    }
}
