using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>
    /// A renderer that fades while it hides the player. While faded it uses the see-through material
    /// and animates _Fade through a property block (only the handful of active occluders pay for that).
    /// </summary>
    public sealed class Occluder : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] private Renderer target;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material fadeMaterial;
        [Tooltip("Seconds to fade fully in or out.")]
        [SerializeField] private float fadeTime = 0.3f;

        public bool Faded { get; private set; }
        public float Fade { get; private set; }
        public int LastSeenFrame { get; set; }

        private MaterialPropertyBlock block;

        public void Configure(Renderer r, Material normal, Material fade)
        {
            target = r;
            normalMaterial = normal;
            fadeMaterial = fade;
        }

        /// <summary>Called by SeeThrough every frame for occluders it tracks. Returns false when fully restored.</summary>
        public bool Tick(bool wanted)
        {
            if (target == null || fadeMaterial == null) return false;
            float goal = wanted ? 1f : 0f;
            Fade = Mathf.MoveTowards(Fade, goal, Time.deltaTime / Mathf.Max(0.01f, fadeTime));
            if (Fade > 0f && !Faded)
            {
                Faded = true;
                target.sharedMaterial = fadeMaterial;
            }

            if (Faded)
            {
                block ??= new MaterialPropertyBlock();
                target.GetPropertyBlock(block);
                block.SetFloat(FadeId, Fade);
                target.SetPropertyBlock(block);
            }

            if (Fade <= 0f && Faded)
            {
                Faded = false;
                target.SetPropertyBlock(null);
                target.sharedMaterial = normalMaterial;
                return false;
            }

            return true;
        }
    }
}
