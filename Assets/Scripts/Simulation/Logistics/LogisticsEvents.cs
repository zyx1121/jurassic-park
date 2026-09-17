namespace JurassicPark.Simulation
{
    /// <summary>Goods moved between two places. From or To is None when the other side is a resource node being gathered.</summary>
    public sealed class GoodsTransferred : SimEvent
    {
        public EntityId From { get; }
        public EntityId To { get; }
        public string Resource { get; }
        public int Amount { get; }

        public GoodsTransferred(EntityId from, EntityId to, string resource, int amount)
        {
            From = from;
            To = to;
            Resource = resource;
            Amount = amount;
        }
    }

    /// <summary>Goods fell to the ground because whatever held them is gone.</summary>
    public sealed class GoodsDropped : SimEvent
    {
        public EntityId From { get; }
        public EntityId Pile { get; }

        public GoodsDropped(EntityId from, EntityId pile)
        {
            From = from;
            Pile = pile;
        }
    }

    public sealed class ResourceNodeDepleted : SimEvent
    {
        public EntityId Node { get; }
        public ResourceNodeDepleted(EntityId node) => Node = node;
    }
}
