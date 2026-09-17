using System.Text;
using JurassicPark.Simulation;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>Drag rectangle and a plain readout of the selection. A stand-in for the real HUD of milestone M2; it reads state and changes nothing.</summary>
    public sealed class SelectionOverlay : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private SelectionController selection;

        private readonly StringBuilder text = new StringBuilder(256);
        private Texture2D pixel;
        private long builtAtTick = -1;
        private int builtForCount = -1;
        private string cached = string.Empty;

        public void Configure(GameSession gameSession, SelectionController controller)
        {
            session = gameSession;
            selection = controller;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
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
            GUI.Label(new Rect(12, 10, 640, 400), Readout());
        }

        /// <summary>Rebuilt once per tick at most, not once per repaint, so the overlay does not churn strings every frame.</summary>
        private string Readout()
        {
            World world = session.Runtime.World;
            if (world.Tick == builtAtTick && selection.Selection.Count == builtForCount) return cached;
            builtAtTick = world.Tick;
            builtForCount = selection.Selection.Count;
            text.Clear();
            text.Append("LMB select  drag box  RMB order  Shift queue  X stop  WASD pan  wheel zoom\n");
            if (selection.LastRejection != CommandRejection.None) text.Append("last order refused: ").Append(selection.LastRejection).Append('\n');
            for (int i = 0; i < selection.Selection.Count && i < 8; i++)
            {
                EntityId id = selection.Selection[i];
                if (!world.TryGet(id, out Entity entity)) continue;
                SimTask task = session.Runtime.Tasks.CurrentOf(id);
                text.Append(entity.DefinitionId).Append(' ').Append(id.Value).Append(": ");
                if (task == null) text.Append("idle");
                else text.Append(task.Kind).Append(' ').Append(task.State).Append(task.Reason != TaskReason.None ? " (" + task.Reason + ")" : string.Empty);
                if (session.Runtime.Logistics.TryGetContainer(id, out Container pack)) text.Append("  pack ").Append(pack.Total).Append('/').Append(pack.Capacity);
                text.Append('\n');
            }
            cached = text.ToString();
            return cached;
        }

        private void OnDestroy()
        {
            if (pixel != null) Destroy(pixel);
        }
    }
}
