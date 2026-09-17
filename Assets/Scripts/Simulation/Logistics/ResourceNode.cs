namespace JurassicPark.Simulation
{
    /// <summary>Ungathered stock, for example a tree. It is not goods yet: gathering turns node stock into goods in the gatherer's pack.</summary>
    public sealed class ResourceNode
    {
        public EntityId Holder { get; }
        public string Resource { get; }
        public int Remaining { get; internal set; }
        internal int Reserved { get; set; }

        /// <summary>Stock no gatherer has been promised yet.</summary>
        public int Unreserved => Remaining - Reserved;

        internal ResourceNode(EntityId holder, string resource, int remaining)
        {
            Holder = holder;
            Resource = resource;
            Remaining = remaining;
        }
    }
}
