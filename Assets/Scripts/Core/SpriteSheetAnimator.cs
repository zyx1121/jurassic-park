using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>
    /// Plays sprite-sheet clips on a lit quad by sliding the material's base map UVs through a
    /// MaterialPropertyBlock, so the sprite keeps receiving 3D lighting and shadows and no
    /// material instances are created.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class SpriteSheetAnimator : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        [SerializeField] private SpriteSheetSet set;
        [SerializeField] private string defaultClip = "Idle";

        public SpriteSheetClip Current { get; private set; }
        public int Row { get; set; }
        public int Frame { get; private set; }
        public bool Finished => Current != null && !Current.loop && time * Current.framesPerSecond >= Current.columns;

        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private float time;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            if (set != null && !string.IsNullOrEmpty(defaultClip))
            {
                Play(defaultClip);
            }
        }

        /// <summary>Switches clip; restarts only when the clip actually changes.</summary>
        public void Play(string clipName)
        {
            SpriteSheetClip clip = set != null ? set.Find(clipName) : null;
            if (clip == null || clip == Current)
            {
                return;
            }

            Current = clip;
            time = 0f;
            Apply();
        }

        private void Update()
        {
            if (Current == null)
            {
                return;
            }

            time += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            Frame = SpriteSheetMath.FrameAt(time, Current.framesPerSecond, Current.columns, Current.loop);
            int row = Mathf.Clamp(Row, 0, Current.rows - 1);
            meshRenderer.GetPropertyBlock(block);
            block.SetTexture(BaseMap, Current.sheet);
            block.SetVector(BaseMapST, SpriteSheetMath.CellScaleOffset(Frame, row, Current.columns, Current.rows));
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
