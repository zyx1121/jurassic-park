using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Queries HUD geometry directly, before InputAction callbacks can reach the world.</summary>
    public static class WorldInputBlockers
    {
        private static readonly List<IWorldInputBlocker> blockers = new List<IWorldInputBlocker>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => blockers.Clear();

        public static void Register(IWorldInputBlocker blocker)
        {
            if (blocker == null) throw new ArgumentNullException(nameof(blocker));
            if (!blockers.Contains(blocker)) blockers.Add(blocker);
        }

        public static void Unregister(IWorldInputBlocker blocker) => blockers.Remove(blocker);

        public static bool BlocksWorldInput
        {
            get
            {
                for (int i = blockers.Count - 1; i >= 0; i--)
                {
                    if (!IsAlive(blockers[i])) { blockers.RemoveAt(i); continue; }
                    if (blockers[i].BlocksWorldInput) return true;
                }
                return false;
            }
        }

        public static bool BlocksPointer(Vector2 screenPosition)
        {
            for (int i = blockers.Count - 1; i >= 0; i--)
            {
                IWorldInputBlocker blocker = blockers[i];
                if (!IsAlive(blocker)) { blockers.RemoveAt(i); continue; }
                if (blocker.BlocksWorldInput || blocker.BlocksPointer(screenPosition)) return true;
            }
            return false;
        }

        private static bool IsAlive(IWorldInputBlocker blocker) =>
            !(blocker is UnityEngine.Object obj) || obj != null;
    }
}
