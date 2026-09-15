using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.World
{
    public sealed class FacilityMarker : MonoBehaviour
    {
        /// <summary>Live markers, so the minimap does not scan the whole scene every OnGUI call.</summary>
        public static readonly List<FacilityMarker> All = new List<FacilityMarker>();

        public string facilityName;
        public bool isDock;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);
    }
}
