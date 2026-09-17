using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// One candidate rectangle for a match event. The map ships every candidate and the per match seed picks one, so
    /// the same map version can host a different rescue point without the terrain changing.
    /// </summary>
    public sealed class RegionDefinition
    {
        public string Id { get; }
        public RegionKind Kind { get; }

        /// <summary>Inclusive cell rectangle the event may place inside.</summary>
        public CellBounds Bounds { get; }

        public RegionDefinition(string id, RegionKind kind, CellBounds bounds)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind;
            Bounds = bounds;
        }

        public override string ToString() => $"Region('{Id}', {Kind}, {Bounds})";
    }
}
