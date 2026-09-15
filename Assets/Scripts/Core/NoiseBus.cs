using System;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Gameplay noises (building, chopping, fighting) that dinosaurs can hear. Static so any system can emit without references.</summary>
    public static class NoiseBus
    {
        public struct Noise
        {
            public Vector3 position;
            public float radius;
            public GameObject source;
        }

        public static event Action<Noise> Emitted;

        public static void Emit(Vector3 position, float radius, GameObject source = null)
        {
            Emitted?.Invoke(new Noise { position = position, radius = radius, source = source });
        }

        /// <summary>A noise is heard when the listener is inside the noise radius or the noise inside the listener's hearing radius.</summary>
        public static bool Hears(Vector3 listener, float hearingRadius, Noise noise)
        {
            float d = Vector3.Distance(listener, noise.position);
            return d <= Mathf.Max(hearingRadius, noise.radius);
        }
    }
}
