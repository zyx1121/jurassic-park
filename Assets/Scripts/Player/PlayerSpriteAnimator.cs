using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.Player
{
    /// <summary>Maps PlayerController state (moving, sprinting, facing) to sprite-sheet clips and rows.</summary>
    public sealed class PlayerSpriteAnimator : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private SpriteSheetAnimator animator;

        private void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            animator = GetComponentInChildren<SpriteSheetAnimator>();
        }

        private void LateUpdate()
        {
            if (player == null || animator == null)
            {
                return;
            }

            animator.Row = (int)player.Facing;
            animator.Play(player.IsMoving ? (player.IsSprinting ? "Run" : "Walk") : "Idle");
        }
    }
}
