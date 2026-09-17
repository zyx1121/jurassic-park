namespace JurassicPark.Simulation
{
    /// <summary>
    /// Why a candidate region exists. The original script draws dinosaur spawns, the rescue zone of the match and
    /// supply drops from separate rectangle lists, so one region list carrying a kind keeps those rules apart without
    /// three parallel map layers.
    /// </summary>
    public enum RegionKind
    {
        DinosaurSpawn = 0,
        Evacuation = 1,
        Supply = 2
    }
}
