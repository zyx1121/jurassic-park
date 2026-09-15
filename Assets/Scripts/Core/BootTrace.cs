using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Startup breadcrumbs in the player log, so a load stall can be located without a debugger.</summary>
    public static class BootTrace
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Subsystems() => Debug.Log("[Boot] subsystem registration");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene() => Debug.Log("[Boot] before scene load");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterScene() => Debug.Log("[Boot] after scene load: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

        public static void Mark(string what) => Debug.Log("[Boot] " + what);
    }
}
