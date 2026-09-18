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
        /// <summary>Sequence chosen by the sender, starting at 1 and increasing within one controller epoch. Seat, epoch and id identify the command for de-duplication.</summary>
        public long CommandId { get; }

        /// <summary>The seat's controller epoch this command was issued under. See <see cref="Seat.ControllerEpoch"/>.</summary>
        public int Epoch { get; }

        /// <summary>The seat this command speaks for. On a networked host this is set from the connection's seat binding, never trusted from the payload.</summary>
        public SeatId Seat { get; }

        public CommandKind Kind { get; }
        public CommandMode Mode { get; }
        public IReadOnlyList<EntityId> Actors { get; }
        public SimVector2 TargetPosition { get; }
        public EntityId TargetEntity { get; }

        /// <summary>Kind-specific number: for Build, the catalog index of what to build.</summary>
        public int Argument { get; }

        public Command(long commandId, int epoch, SeatId seat, CommandKind kind, IReadOnlyList<EntityId> actors,
            SimVector2 targetPosition = default, EntityId targetEntity = default, CommandMode mode = CommandMode.Replace, int argument = 0)
        {
            CommandId = commandId;
            Epoch = epoch;
            Seat = seat;
            Kind = kind;
            Mode = mode;
            // Copied: the command waits a tick in the queue, and the caller's selection buffer must not be able to change it meanwhile.
            Actors = actors == null || actors.Count == 0 ? Array.Empty<EntityId>() : Array.AsReadOnly(Copy(actors));
            TargetPosition = targetPosition;
            TargetEntity = targetEntity;
            Argument = argument;
        }

        private static EntityId[] Copy(IReadOnlyList<EntityId> source)
        {
            var copy = new EntityId[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }

        public override string ToString() => $"{Seat} e{Epoch}#{CommandId} {Kind} x{Actors.Count}";
    }
}
