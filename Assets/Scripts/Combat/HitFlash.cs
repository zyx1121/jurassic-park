using UnityEngine;

namespace JurassicPark.Combat
{
    /// <summary>Tints every renderer under this object for a moment when its Health takes a hit.</summary>
    [RequireComponent(typeof(Health))]
    public sealed class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color flashColor = new Color(1f, 0.35f, 0.35f);
        [Min(0.01f)] [SerializeField] private float duration = 0.12f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float until;
        private bool flashing;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
            GetComponent<Health>().Damaged += OnDamaged;
        }

        private void OnDamaged(DamageInfo info, float applied)
        {
            until = Time.time + duration;
            if (!flashing)
            {
                flashing = true;
                SetColor(flashColor);
            }
        }

        private void Update()
        {
            if (flashing && Time.time >= until)
            {
                flashing = false;
                SetColor(Color.white);
            }
        }

        private void SetColor(Color c)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer)
                {
                    continue;
                }

                renderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColor, c);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }
}
