namespace JurassicPark.Simulation
{
    /// <summary>One step of the fixed-order simulation pipeline. Systems run on the authority only and in registration order.</summary>
    public interface ISimSystem
    {
        void Tick(World world);
    }
}
