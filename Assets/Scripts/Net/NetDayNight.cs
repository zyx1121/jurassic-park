using JurassicPark.Scene;
using Unity.Netcode;
using UnityEngine;

namespace JurassicPark.Net
{
    /// <summary>Host owns the clock; clients follow it. Sits next to the DayNightCycle.</summary>
    [RequireComponent(typeof(DayNightCycle))]
    public sealed class NetDayNight : NetworkBehaviour
    {
        private readonly NetworkVariable<float> time = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<int> day = new NetworkVariable<int>(1);

        private DayNightCycle cycle;

        private void Awake()
        {
            cycle = GetComponent<DayNightCycle>();
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                time.Value = cycle.NormalizedTime;
                day.Value = cycle.DayNumber;
            }
            else if (Mathf.Abs(Mathf.DeltaAngle(cycle.NormalizedTime * 360f, time.Value * 360f)) > 1f)
            {
                cycle.SetTime(time.Value);
            }
        }
    }
}
