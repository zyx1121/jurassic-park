using System.Collections.Generic;
using System.Text;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// What the HUD says, worked out from the read model alone: the seat's stores, the clocks and one line per selected thing.
    /// Plain C# with no MonoBehaviour and no drawing, so an EditMode test checks the numbers without a scene.
    /// </summary>
    public static class HudModel
    {
        /// <summary>Everything the local seat can spend, split by where it sits. Only the read model is counted, so the fog never leaks into a total.</summary>
        public readonly struct Stores
        {
            /// <summary>In depots the local seat owns.</summary>
            public int Own { get; }

            /// <summary>In depots a team mate owns: usable by the local seat, which is why it is shown, but not its own.</summary>
            public int Allied { get; }

            /// <summary>In the packs of the local seat's units, on its way somewhere.</summary>
            public int Carried { get; }

            public Stores(int own, int allied, int carried)
            {
                Own = own;
                Allied = allied;
                Carried = carried;
            }

            public int Total => Own + Allied + Carried;
        }

        /// <summary>
        /// Counts the stores. A depot is whatever the catalog marks as one, so a new building that stores goods is counted
        /// without touching this. Wood is the only resource in the slice, so a pack total is a wood total.
        /// </summary>
        public static Stores StoresOf(MatchReadModel model)
        {
            if (model == null) return default;
            int own = 0, allied = 0, carried = 0;
            IReadOnlyList<EntitySnapshot> entities = model.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                EntitySnapshot entity = entities[i];
                if (entity.PackTotal == 0) continue;
                EntityCatalogAsset.Entry entry = model.EntryOf(entity);
                if (entry != null && entry.isDepot)
                {
                    if (entity.Owner == model.LocalSeat) own += entity.PackTotal;
                    else if (IsTeamMate(model, entity.Owner)) allied += entity.PackTotal;
                    continue;
                }
                if (entity.Kind == EntityKind.Unit && entity.Owner == model.LocalSeat) carried += entity.PackTotal;
            }
            return new Stores(own, allied, carried);
        }

        /// <summary>Another seat on the local seat's team. The local seat itself is not its own team mate.</summary>
        public static bool IsTeamMate(MatchReadModel model, SeatId seat)
        {
            if (seat.IsNone || seat == model.LocalSeat || model.LocalSeat.IsNone) return false;
            int team = model.TeamOf(seat);
            return team != 0 && team == model.TeamOf(model.LocalSeat);
        }

        /// <summary>Seconds as mm:ss, the countdown the match clock shows.</summary>
        public static void AppendClock(StringBuilder into, float seconds)
        {
            int whole = seconds > 0f ? (int)seconds : 0;
            into.Append(whole / 60).Append(':').Append((whole % 60).ToString("00"));
        }

        /// <summary>The hour of the day as hh:mm.</summary>
        public static void AppendTimeOfDay(StringBuilder into, float timeOfDay)
        {
            int hours = (int)timeOfDay;
            into.Append(hours.ToString("00")).Append(':').Append(((int)((timeOfDay - hours) * 60f)).ToString("00"));
        }

        /// <summary>The clock's own rule, kept in step with <see cref="Clock.IsNight"/>: a night that crosses midnight wraps.</summary>
        public static bool IsNight(float timeOfDay, float nightStartsAt, float nightEndsAt) =>
            nightStartsAt > nightEndsAt
                ? timeOfDay >= nightStartsAt || timeOfDay < nightEndsAt
                : timeOfDay >= nightStartsAt && timeOfDay < nightEndsAt;

        /// <summary>How many of each kind are selected, in the order they first appear, for a selection too long to list.</summary>
        public readonly struct DefinitionCount
        {
            public string Id { get; }
            public int Count { get; }

            public DefinitionCount(string id, int count)
            {
                Id = id;
                Count = count;
            }
        }

        public static void CountByDefinition(MatchReadModel model, IReadOnlyList<EntityId> selection, List<DefinitionCount> into)
        {
            into.Clear();
            if (model == null || selection == null) return;
            for (int i = 0; i < selection.Count; i++)
            {
                if (!model.TryGet(selection[i], out EntitySnapshot entity)) continue;
                string id = model.DefinitionIdOf(entity);
                int at = -1;
                for (int k = 0; k < into.Count; k++)
                {
                    if (into[k].Id != id) continue;
                    at = k;
                    break;
                }
                if (at < 0) into.Add(new DefinitionCount(id, 1));
                else into[at] = new DefinitionCount(id, into[at].Count + 1);
            }
        }

        /// <summary>One line about a selected thing: who owns it, how it is, what it carries, what it is doing and why it is stuck.</summary>
        public static void AppendSelected(StringBuilder into, MatchReadModel model, in EntitySnapshot entity)
        {
            into.Append(model.DefinitionIdOf(entity)).Append(' ').Append(entity.Id.Value);
            if (entity.Owner.IsNone) into.Append("  unowned");
            else if (entity.Owner == model.LocalSeat) into.Append("  yours");
            else into.Append(IsTeamMate(model, entity.Owner) ? "  ally seat " : "  seat ").Append(entity.Owner.Value);
            if (entity.HealthFraction < 255) into.Append("  hp ").Append(entity.HealthFraction * 100 / 255).Append('%');
            if (entity.PackCapacity > 0) into.Append("  pack ").Append(entity.PackTotal).Append('/').Append(entity.PackCapacity);
            else if (entity.PackTotal > 0) into.Append("  holds ").Append(entity.PackTotal);
            if (entity.IsSite) into.Append("  built ").Append(entity.BuildProgress * 100 / 255).Append('%');
            if (entity.Kind == EntityKind.ResourceNode) into.Append("  left ").Append(entity.NodeRemaining);
            EntityCatalogAsset.Entry entry = model.EntryOf(entity);
            if (entry != null && entry.isGate && !entity.IsSite) into.Append(entity.GateOpen ? "  open" : "  closed");
            if (entity.Task == TaskKindCode.None) into.Append("  idle");
            else
            {
                into.Append("  ").Append(entity.Task).Append(' ').Append(entity.TaskState);
                if (entity.TaskReason != TaskReason.None) into.Append(" (").Append(entity.TaskReason).Append(')');
            }
            if (entity.Remembered) into.Append("  [remembered]");
        }
    }
}
