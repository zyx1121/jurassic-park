using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>All facility kits, looked up by name when the island is built.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Facility Library", fileName = "FacilityLibrary")]
    public sealed class FacilityLibrary : ScriptableObject
    {
        public FacilityKit[] kits = new FacilityKit[0];

        public FacilityKit Find(string facilityName)
        {
            foreach (FacilityKit k in kits)
            {
                if (k != null && k.facilityName == facilityName) return k;
            }

            return null;
        }
    }
}
