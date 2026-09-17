using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// What one path query is allowed to do. A survivor hauling crates and a dinosaur planning a breach ask the same
    /// pathfinder different questions, so the costs and the permission to cross a destructible blocker are inputs, not
    /// constants inside the algorithm. The move costs live here too, so no gameplay number is buried in the search.
    /// </summary>
    public sealed class PathOptions
    {
        /// <summary>Cost of an orthogonal step. Ten keeps whole numbers exact and leaves room for the diagonal ratio.</summary>
        public const int DefaultStraightCost = 10;

        /// <summary>Cost of a diagonal step: ten times the square root of two, rounded, so diagonals are not free.</summary>
        public const int DefaultDiagonalCost = 14;

        /// <summary>
        /// Budget value that means "as many nodes as the map has cells". A fixed number smaller than the map can never
        /// be exhausted, so an enclosed goal would report BudgetExceeded forever and NoRoute would be unreachable; a
        /// budget of one cell per cell always terminates and always lets the searcher prove there is no route.
        /// </summary>
        public const int BudgetFromMapSize = 0;

        /// <summary>A fixed budget for callers that want one that does not grow with the map. It is not the default: see <see cref="BudgetFromMapSize"/>.</summary>
        public const int DefaultMaxExpandedNodes = 20000;

        /// <summary>The searcher may cross a destructible blocker, paying <see cref="BreachCost"/>. False makes every blocker a wall.</summary>
        public bool AllowBreach { get; }

        /// <summary>Extra cost charged for entering one cell held by a destructible blocker, on top of the move cost. It is what makes going around the cheaper answer until it is not.</summary>
        public int BreachCost { get; }

        /// <summary>Upper bound on expanded nodes, or <see cref="BudgetFromMapSize"/> to take one per cell of the map being searched. Reaching it returns BudgetExceeded, which means "not answered yet", never "no route".</summary>
        public int MaxExpandedNodes { get; }

        public int StraightCost { get; }
        public int DiagonalCost { get; }

        public PathOptions(
            bool allowBreach = false,
            int breachCost = 0,
            int maxExpandedNodes = BudgetFromMapSize,
            int straightCost = DefaultStraightCost,
            int diagonalCost = DefaultDiagonalCost)
        {
            if (breachCost < 0) throw new ArgumentOutOfRangeException(nameof(breachCost), "A breach cannot refund movement.");
            if (maxExpandedNodes < BudgetFromMapSize) throw new ArgumentOutOfRangeException(nameof(maxExpandedNodes), "A budget is a count of nodes, or zero to take it from the map.");
            if (straightCost < 1) throw new ArgumentOutOfRangeException(nameof(straightCost), "Moves cost something, or the search has no order.");
            if (diagonalCost < straightCost) throw new ArgumentOutOfRangeException(nameof(diagonalCost), "A diagonal step covers more ground than a straight one.");
            if (diagonalCost > 2 * straightCost) throw new ArgumentOutOfRangeException(nameof(diagonalCost), "A diagonal step must not cost more than the two straight steps it replaces.");
            AllowBreach = allowBreach;
            BreachCost = breachCost;
            MaxExpandedNodes = maxExpandedNodes;
            StraightCost = straightCost;
            DiagonalCost = diagonalCost;
        }
    }
}
