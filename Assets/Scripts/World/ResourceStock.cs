namespace JurassicPark.World
{
    /// <summary>Pure gathering math for one node: hits accumulate into units until the stock runs out.</summary>
    public sealed class ResourceStock
    {
        public int Remaining { get; private set; }
        public int Capacity { get; }
        public int HitsPerUnit { get; }
        public int Hits { get; private set; }
        public bool Depleted => Remaining <= 0;
        public float Progress => HitsPerUnit <= 1 ? 0f : Hits / (float)HitsPerUnit;

        public ResourceStock(int capacity, int hitsPerUnit)
        {
            Capacity = capacity;
            Remaining = capacity;
            HitsPerUnit = hitsPerUnit < 1 ? 1 : hitsPerUnit;
        }

        /// <summary>One interact press. Returns the units yielded (0 or 1).</summary>
        public int Hit()
        {
            if (Depleted)
            {
                return 0;
            }

            Hits++;
            if (Hits < HitsPerUnit)
            {
                return 0;
            }

            Hits = 0;
            Remaining--;
            return 1;
        }

        public void Refill()
        {
            Remaining = Capacity;
            Hits = 0;
        }
    }
}
