using System.Collections.Generic;
using System.Text;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>What a command card button asks for. The HUD turns each one into the call a key press would make.</summary>
    public enum HudAction
    {
        /// <summary>Drop what the selection is doing.</summary>
        Stop = 1,

        /// <summary>Enter placement for <see cref="HudButton.DefinitionId"/>.</summary>
        Place = 2,

        /// <summary>Leave placement.</summary>
        CancelPlacement = 3,

        /// <summary>A reminder that Del demolishes the building under the cursor. There is no cursor on a button, so it never acts.</summary>
        Demolish = 4,

        /// <summary>Open or close <see cref="HudButton.Target"/>, through the same resolver a right click uses.</summary>
        ToggleGate = 5,
    }

    /// <summary>One button: what it asks for, what it says, and, when it is greyed, why it would be refused.</summary>
    public readonly struct HudButton
    {
        public HudAction Action { get; }
        public string Label { get; }
        public string Hotkey { get; }

        /// <summary>Catalog id for <see cref="HudAction.Place"/>, otherwise null.</summary>
        public string DefinitionId { get; }

        /// <summary>What the action acts on, for <see cref="HudAction.ToggleGate"/>.</summary>
        public EntityId Target { get; }

        public bool Enabled { get; }

        /// <summary>Why the button is greyed, in words a player can act on. Empty while it is enabled.</summary>
        public string Reason { get; }

        public HudButton(HudAction action, string label, string hotkey, bool enabled, string reason = "", string definitionId = null, EntityId target = default)
        {
            Action = action;
            Label = label;
            Hotkey = hotkey;
            Enabled = enabled;
            Reason = reason ?? string.Empty;
            DefinitionId = definitionId;
            Target = target;
        }
    }

    /// <summary>
    /// The command card, derived from what is selected and from the catalog: every buildable entry becomes a Build button and
    /// every gate the local seat may use becomes a Toggle Gate button, so a new definition needs no change here. Pure, so an
    /// EditMode test can ask what a selection offers without a scene.
    /// </summary>
    public static class CommandCard
    {
        private const string NoActors = "select one of your units";
        private const string DemolishHint = "press Del over one of your buildings";

        /// <summary>The keys <see cref="SelectionController"/> already binds for placement. Anything else is mouse only.</summary>
        private static string HotkeyOf(string definitionId) => definitionId == "wall" ? "B" : definitionId == "gate" ? "G" : "";

        /// <summary>
        /// Fills <paramref name="into"/> with the buttons this selection offers. A greyed button is one that belongs to the
        /// selection but would be refused right now, and it carries the reason; a button that does not belong is absent.
        /// </summary>
        public static void Build(MatchReadModel model, IReadOnlyList<EntityId> selection, int placingIndex, List<HudButton> into, StringBuilder scratch)
        {
            into.Clear();
            if (model == null || selection == null) return;
            bool ownUnit = false;
            for (int i = 0; i < selection.Count; i++)
                if (model.TryGet(selection[i], out EntitySnapshot entity) && entity.Kind == EntityKind.Unit && entity.Owner == model.LocalSeat) ownUnit = true;

            if (selection.Count > 0)
            {
                into.Add(new HudButton(HudAction.Stop, "Stop", "X", ownUnit, ownUnit ? "" : NoActors));
                EntityCatalogAsset.Entry[] entries = model.Catalog.entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    EntityCatalogAsset.Entry entry = entries[i];
                    if (entry.buildCost.Length == 0) continue;
                    into.Add(new HudButton(HudAction.Place, LabelOf(scratch, entry), HotkeyOf(entry.id), ownUnit, ownUnit ? "" : NoActors, entry.id));
                }
                into.Add(new HudButton(HudAction.Demolish, "Demolish", "Del", false, DemolishHint));
            }
            if (placingIndex >= 0) into.Add(new HudButton(HudAction.CancelPlacement, "Cancel placement", "Esc", true));

            for (int i = 0; i < selection.Count; i++)
            {
                if (!model.TryGet(selection[i], out EntitySnapshot entity)) continue;
                EntityCatalogAsset.Entry entry = model.EntryOf(entity);
                if (entry == null || !entry.isGate || !model.LocalMayUsePropertyOf(entity.Owner)) continue;
                // A site is not a gate yet, and something only remembered may be long gone: the authority refuses both.
                bool enabled = !entity.IsSite && !entity.Remembered;
                string reason = entity.IsSite ? "not built yet" : entity.Remembered ? "not in sight" : "";
                into.Add(new HudButton(HudAction.ToggleGate, entity.GateOpen ? "Close gate" : "Open gate", "", enabled, reason, entry.id, entity.Id));
            }
        }

        /// <summary>"Build wall 6 wood": the name and the price come from the catalog entry, never from a table here.</summary>
        private static string LabelOf(StringBuilder scratch, EntityCatalogAsset.Entry entry)
        {
            scratch.Clear();
            scratch.Append("Build ").Append(entry.id);
            for (int c = 0; c < entry.buildCost.Length; c++)
                scratch.Append(' ').Append(entry.buildCost[c].amount).Append(' ').Append(entry.buildCost[c].resource);
            return scratch.ToString();
        }
    }
}
