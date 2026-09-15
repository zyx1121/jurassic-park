using UnityEngine;

namespace JurassicPark.UI
{
    /// <summary>Only restores a pause acquired by this menu, never a pre-existing pause.</summary>
    public sealed class HudPause
    {
        private bool ownsPause;
        private float previousScale;

        public void Open(bool networkSession)
        {
            if (networkSession || ownsPause || Time.timeScale == 0f) return;
            previousScale = Time.timeScale;
            ownsPause = true;
            Time.timeScale = 0f;
        }

        public void Close()
        {
            if (!ownsPause) return;
            if (Time.timeScale == 0f) Time.timeScale = previousScale;
            ownsPause = false;
        }
    }
}
