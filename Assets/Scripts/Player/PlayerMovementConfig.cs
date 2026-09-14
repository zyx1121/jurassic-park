using UnityEngine;

namespace JurassicPark.Player
{
    /// <summary>Tunable movement numbers. One asset per character class later on.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Player Movement Config", fileName = "PlayerMovement")]
    public sealed class PlayerMovementConfig : ScriptableObject
    {
        [Min(0f)] public float walkSpeed = 4f;
        [Min(0f)] public float sprintSpeed = 6.5f;
        [Tooltip("How fast horizontal velocity approaches the target, in units per second squared.")]
        [Min(0f)] public float acceleration = 40f;
        [Min(0f)] public float gravity = 25f;
        [Tooltip("Radius around the player in which interactables are searched.")]
        [Min(0f)] public float interactRadius = 1.6f;
    }
}
