namespace JurassicPark.Simulation
{
    /// <summary>Broad category of an entity. Concrete content (which survivor, which dinosaur) is data, not a new kind.</summary>
    public enum EntityKind
    {
        Unit = 1,
        Building = 2,
        ResourceNode = 3,
        GroundPile = 4,
    }
}
