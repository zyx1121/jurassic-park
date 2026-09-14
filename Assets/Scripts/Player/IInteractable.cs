namespace JurassicPark.Player
{
    /// <summary>Anything the player can press Interact on: resource nodes, structures, pickups, the boat.</summary>
    public interface IInteractable
    {
        /// <summary>Short verb shown in the HUD prompt, e.g. "Gather", "Repair".</summary>
        string Prompt { get; }

        bool CanInteract(PlayerController player);

        void Interact(PlayerController player);
    }
}
