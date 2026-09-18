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
        private int builtForVersion = -1;
        private string cached = string.Empty;

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
            GUI.Label(new Rect(12, 10, 640, 400), Readout());
        }

        /// <summary>Rebuilt once per tick at most, not once per repaint, so the overlay does not churn strings every frame.</summary>
        private string Readout()
        {
            if (session.Failure != null) return session.Failure;
            MatchReadModel model = session.Model;
            if (model == null) return string.Empty;
            if (session.Commands == null) return "connected, waiting for a seat";
            if (model.Tick == builtAtTick && selection.Version == builtForVersion && selection.PlacingIndex < 0 && session.Role != MatchRole.Client) return cached;
            builtAtTick = model.Tick;
            builtForVersion = selection.Version;
            text.Clear();
            text.Append(session.Role).Append("  seat ").Append(model.LocalSeat.Value).Append("  tick ").Append(model.Tick).Append('\n');
            MatchSnapshot match = model.Match;
            int hours = (int)match.TimeOfDay, minutes = (int)((match.TimeOfDay - hours) * 60f);
            text.Append(match.Phase).Append("  ").Append((int)match.SecondsLeft / 60).Append(':').Append(((int)match.SecondsLeft % 60).ToString("00"))
                .Append("  ").Append(hours.ToString("00")).Append(':').Append(minutes.ToString("00"))
                .Append("  mode ").Append(match.ModeIndex).Append(" difficulty ").Append(match.Difficulty);
            if (match.BoardedByLocal > 0) text.Append("  boarded ").Append(match.BoardedByLocal);
            if (match.LocalOutcome != SeatOutcome.Undecided) text.Append("  ").Append(match.LocalOutcome.ToString().ToUpperInvariant());
            text.Append('\n');
            text.Append("LMB select  drag box  RMB order  Shift queue  X stop  B wall  G gate  Del demolish  WASD pan  wheel zoom\n");
            if (selection.PlacingIndex >= 0)
                text.Append("placing ").Append(model.Catalog.entries[selection.PlacingIndex].id).Append(" at ").Append(selection.PlacingCell).Append("  (LMB place, Shift keeps placing, RMB or Esc cancel)\n");
            if (selection.LastRejection != CommandRejection.None) text.Append("last order refused: ").Append(selection.LastRejection).Append('\n');
            for (int i = 0; i < selection.Selection.Count && i < 8; i++)
            {
                if (!model.TryGet(selection.Selection[i], out EntitySnapshot unit)) continue;
                text.Append(model.DefinitionIdOf(unit)).Append(' ').Append(unit.Id.Value).Append(": ");
                if (unit.Task == TaskKindCode.None) text.Append("idle");
                else
                {
                    text.Append(unit.Task).Append(' ').Append(unit.TaskState);
                    if (unit.TaskReason != TaskReason.None) text.Append(" (").Append(unit.TaskReason).Append(')');
                }
                if (unit.PackCapacity > 0) text.Append("  pack ").Append(unit.PackTotal).Append('/').Append(unit.PackCapacity);
                if (unit.HealthFraction < 255) text.Append("  hp ").Append(unit.HealthFraction * 100 / 255).Append('%');
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
