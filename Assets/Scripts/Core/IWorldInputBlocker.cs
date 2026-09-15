using UnityEngine;

namespace JurassicPark.Core
{
    public interface IWorldInputBlocker
    {
        bool BlocksWorldInput { get; }
        bool BlocksPointer(Vector2 screenPosition);
    }
}
