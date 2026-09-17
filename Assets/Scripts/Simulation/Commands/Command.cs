using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// An intent: which seat asks which of its entities to do what. A command changes nothing by itself.
    /// Human input, the GUI and computer allies all produce the same value, and it is the only thing a client sends to the host.
    /// </summary>
    public sealed class Command
    {
        /// <summary>Sequence chosen by the sender, increasing per seat. With the seat it identifies the command for de-duplication.</summary>
        public long CommandId { get; }

        /// <summary>The seat this command speaks for. On a networked host this is set from the connection's seat binding, never trusted from the payload.</summary>
        public SeatId Seat { get; }

        public CommandKind Kind { get; }
        public CommandMode Mode { get; }
        public IReadOnlyList<EntityId> Actors { get; }
        public SimVector2 TargetPosition { get; }
        public EntityId TargetEntity { get; }

        public Command(long commandId, SeatId seat, CommandKind kind, IReadOnlyList<EntityId> actors,
            SimVector2 targetPosition = default, EntityId targetEntity = default, CommandMode mode = CommandMode.Replace)
        {
            CommandId = commandId;
            Seat = seat;
            Kind = kind;
            Mode = mode;
            Actors = actors ?? Array.Empty<EntityId>();
            TargetPosition = targetPosition;
            TargetEntity = targetEntity;
        }

        public override string ToString() => $"{Seat} #{CommandId} {Kind} x{Actors.Count}";
    }
}
