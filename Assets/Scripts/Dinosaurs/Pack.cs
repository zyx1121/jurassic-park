using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Dinosaurs
{
    /// <summary>A group of dinosaurs that hunt together: the leader picks the target, followers take flank slots.</summary>
    public sealed class Pack : MonoBehaviour
    {
        private readonly List<DinosaurBrain> members = new List<DinosaurBrain>();

        public IReadOnlyList<DinosaurBrain> Members => members;
        public DinosaurBrain Leader => members.Count > 0 ? members[0] : null;
        public Transform SharedTarget { get; private set; }

        public void Add(DinosaurBrain brain)
        {
            if (!members.Contains(brain))
            {
                members.Add(brain);
                brain.Pack = this;
            }
        }

        public void Remove(DinosaurBrain brain)
        {
            members.Remove(brain);
            if (brain.Pack == this) brain.Pack = null;
        }

        public int IndexOf(DinosaurBrain brain) => members.IndexOf(brain);

        /// <summary>Leader reports its target; followers read it. A dead leader hands over to the next member.</summary>
        public void ReportTarget(DinosaurBrain reporter, Transform target)
        {
            Prune();
            if (reporter == Leader)
            {
                SharedTarget = target;
            }
            else if (SharedTarget == null && target != null)
            {
                SharedTarget = target;
            }
        }

        public void Prune()
        {
            members.RemoveAll(m => m == null || m.State == DinosaurState.Dead);
            if (members.Count == 0) SharedTarget = null;
        }
    }
}
