namespace JurassicPark.Core
{
    /// <summary>Primary action may repeat while held; doors and pickups deliberately do not opt in.</summary>
    public interface IRepeatableInteractable : IInteractable
    {
    }
}
