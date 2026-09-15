using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>A renderer that can go translucent while it hides the player. PropPlacer adds one per prop with a fade material.</summary>
    public sealed class Occluder : MonoBehaviour
    {
        [SerializeField] private Renderer target;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material fadeMaterial;

        public bool Faded { get; private set; }
        public int LastSeenFrame { get; set; }

        public void Configure(Renderer r, Material normal, Material fade)
        {
            target = r;
            normalMaterial = normal;
            fadeMaterial = fade;
        }

        public void SetFaded(bool faded)
        {
            if (Faded == faded || target == null || fadeMaterial == null) return;
            Faded = faded;
            target.sharedMaterial = faded ? fadeMaterial : normalMaterial;
        }
    }
}
