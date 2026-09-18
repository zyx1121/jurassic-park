namespace JurassicPark.Simulation
{
    /// <summary>One hit landed. Views play the bite and the flinch from this; the damage itself is in HealthChanged.</summary>
    public sealed class Attacked : SimEvent
    {
        public EntityId Attacker { get; }
        public EntityId Target { get; }
        public int Damage { get; }

        public Attacked(EntityId attacker, EntityId target, int damage)
        {
            Attacker = attacker;
            Target = target;
            Damage = damage;
        }
    }
}
