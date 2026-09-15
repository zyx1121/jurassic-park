using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Anything a player can press Interact on: resource nodes, structures, pickups, the boat.</summary>
    public interface IInteractable
    {
        /// <summary>Short verb shown in the HUD prompt, e.g. "Gather", "Repair".</summary>
        string Prompt { get; }

        bool CanInteract(GameObject actor);

        void Interact(GameObject actor);
    }
}
