using JurassicPark.Core;
using Unity.Netcode;
using UnityEngine;

namespace JurassicPark.Net
{
    /// <summary>Plugs the NetworkManager into Core.Authority so gameplay systems know whether they are the host.</summary>
    public static class NetAuthority
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Authority.Provider = () =>
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            };
            Authority.NetworkedProvider = () =>
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening;
            };
        }
    }
}
