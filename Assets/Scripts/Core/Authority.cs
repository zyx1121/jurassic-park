using System;

namespace JurassicPark.Core
{
    /// <summary>
    /// Who runs gameplay simulation. Offline this is always true. When a NetworkManager exists the
    /// Net assembly plugs in a provider so only the host advances dinosaurs, spawns, time and damage.
    /// </summary>
    public static class Authority
    {
        public static Func<bool> Provider;

        public static bool IsAuthority => Provider == null || Provider();

        /// <summary>True when a networked session is active (host or client).</summary>
        public static Func<bool> NetworkedProvider;

        public static bool IsNetworked => NetworkedProvider != null && NetworkedProvider();
    }
}
