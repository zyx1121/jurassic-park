using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The drag rectangle, drawn where the box select is happening. The readout it used to carry moved into
    /// <see cref="HudOverlay"/> when the diagnostic HUD arrived, so the two do not write over each other; this draws nothing
    /// but the box and the message that stopped the match.
    /// </summary>
    public sealed class SelectionOverlay : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private SelectionController selection;

        private Texture2D pixel;

        public void Configure(GameSession gameSession, SelectionController controller)
        {
            session = gameSession;
            selection = controller;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || selection == null) return;
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            if (selection.IsDragging)
            {
                Rect r = selection.DragRect;
                var gui = new Rect(r.xMin, Screen.height - r.yMax, r.width, r.height);
                GUI.color = new Color(0.4f, 1f, 0.5f, 0.18f);
                GUI.DrawTexture(gui, pixel);
                GUI.color = Color.white;
            }
            // The HUD has the bar to itself; a match that could not start says so here, in the middle of the screen, where
            // nothing else draws.
            if (session != null && session.Failure != null)
                GUI.Label(new Rect(12f, Screen.height * 0.5f - 40f, Screen.width - 24f, 80f), session.Failure);
        }

        private void OnDestroy()
        {
            if (pixel != null) Destroy(pixel);
        }
    }
}
