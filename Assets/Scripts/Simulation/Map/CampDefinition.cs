using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// One recommended camp site. The original marks 12 of them and assigns none to a player, so a camp is an offer,
    /// not an owner. Entrances are listed explicitly because a camp is defined by where it can be entered, and
    /// therefore where it can be walled or breached, which the bounds rectangle alone cannot express.
    /// </summary>
    public sealed class CampDefinition
    {
        private readonly List<Cell> entrances;

        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>Inclusive cell rectangle of the camp ground.</summary>
        public CellBounds Bounds { get; }

        /// <summary>Walkable gaps leading into the camp, in author order. They sit on the cliff line, usually just outside <see cref="Bounds"/>.</summary>
        public IReadOnlyList<Cell> Entrances { get; }

        public CampDefinition(string id, string displayName, CellBounds bounds, IReadOnlyList<Cell> entrances)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? string.Empty;
            Bounds = bounds;
            if (entrances == null) throw new ArgumentNullException(nameof(entrances));
            this.entrances = new List<Cell>(entrances);
            Entrances = this.entrances.AsReadOnly();
        }

        public override string ToString() => $"Camp('{Id}', {Bounds}, {entrances.Count} entrances)";
    }
}
