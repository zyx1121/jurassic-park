using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>Flickers a point light with layered noise so the campfire reads as alive.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class CampfireLight : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float baseIntensity = 30f;
        [Range(0f, 1f)] [SerializeField] private float flicker = 0.25f;
        [Min(0f)] [SerializeField] private float speed = 7f;
        [Min(0f)] [SerializeField] private float wobble = 0.08f;

        /// <summary>Live campfires, for dinosaur firelight checks without a scene scan.</summary>
        public static readonly List<CampfireLight> All = new List<CampfireLight>();

        public Light Light => fireLight != null ? fireLight : fireLight = GetComponent<Light>();

        private Light fireLight;
        private Vector3 basePosition;
        private float seed;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            fireLight = GetComponent<Light>();
            basePosition = transform.localPosition;
            seed = Random.value * 100f;
        }

        private void Update()
        {
            float t = Time.time * speed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, seed + 7f) * 0.3f;
            fireLight.intensity = baseIntensity * (1f - flicker + flicker * 2f * n);
            transform.localPosition = basePosition + new Vector3(
                (Mathf.PerlinNoise(t, 3f) - 0.5f) * wobble,
                (Mathf.PerlinNoise(t, 5f) - 0.5f) * wobble,
                (Mathf.PerlinNoise(t, 9f) - 0.5f) * wobble);
        }
    }
}
