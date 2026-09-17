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

        /// <summary>Default budget in expanded nodes. It bounds one query, so a hopeless search reports instead of stalling a tick.</summary>
        public const int DefaultMaxExpandedNodes = 20000;

        /// <summary>The searcher may cross a destructible blocker, paying <see cref="BreachCost"/>. False makes every blocker a wall.</summary>
        public bool AllowBreach { get; }

        /// <summary>Extra cost charged for entering one cell held by a destructible blocker, on top of the move cost. It is what makes going around the cheaper answer until it is not.</summary>
        public int BreachCost { get; }

        /// <summary>Upper bound on expanded nodes. Reaching it returns BudgetExceeded, which means "not answered yet", never "no route".</summary>
        public int MaxExpandedNodes { get; }

        public int StraightCost { get; }
        public int DiagonalCost { get; }

        public PathOptions(
            bool allowBreach = false,
            int breachCost = 0,
            int maxExpandedNodes = DefaultMaxExpandedNodes,
            int straightCost = DefaultStraightCost,
            int diagonalCost = DefaultDiagonalCost)
        {
            if (breachCost < 0) throw new ArgumentOutOfRangeException(nameof(breachCost), "A breach cannot refund movement.");
            if (maxExpandedNodes < 1) throw new ArgumentOutOfRangeException(nameof(maxExpandedNodes), "A query must be allowed at least one expansion.");
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
