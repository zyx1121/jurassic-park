using System.Collections.Generic;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using Unity.Collections;
using Unity.Netcode;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Net
{
    /// <summary>
    /// The whole wire protocol: commands in, answers and snapshots out. Named messages rather than NetworkObjects, because
    /// nothing in this game is replicated per object; the host owns one simulation and everyone else draws pictures of it.
    /// Every reader is for data from another machine: it bounds what it allocates and returns false instead of throwing.
    /// </summary>
    public static class NetMessages
    {
        public const string Command = "jp.command";
        public const string Answer = "jp.answer";
        public const string Snapshot = "jp.snapshot";
        public const string Seats = "jp.seats";
        public const string Welcome = "jp.welcome";
        public const string MatchFull = "jp.full";

        /// <summary>Bumped whenever the byte layout changes, so mismatched builds refuse each other instead of misreading.</summary>
        public const ushort ProtocolVersion = 2;

        public const int MaxActorsPerCommand = 128;
        public const int MaxEntitiesPerSnapshot = 4096;
        private const int EntityBytes = 8 + 2 + 1 + 4 + 4 + 4 + 2 + 2 + 4 + 1 + 1 + 1 + 1 + 1 + 1 + 1;

        // ---- command: client to host. The seat is deliberately absent. ----

        public static FastBufferWriter WriteCommand(Command command)
        {
            var writer = new FastBufferWriter(44 + command.Actors.Count * 8, Allocator.Temp);
            writer.WriteValueSafe(command.CommandId);
            writer.WriteValueSafe(command.Epoch);
            writer.WriteValueSafe((byte)command.Kind);
            writer.WriteValueSafe((byte)command.Mode);
            writer.WriteValueSafe(command.TargetPosition.X);
            writer.WriteValueSafe(command.TargetPosition.Y);
            writer.WriteValueSafe(command.TargetEntity.Value);
            writer.WriteValueSafe(command.Argument);
            writer.WriteValueSafe((ushort)command.Actors.Count);
            for (int i = 0; i < command.Actors.Count; i++) writer.WriteValueSafe(command.Actors[i].Value);
            return writer;
        }

        public static bool TryReadCommand(ref FastBufferReader reader, SeatId boundSeat, out Command command)
        {
            command = null;
            if (!reader.TryBeginRead(8 + 4 + 1 + 1 + 4 + 4 + 8 + 4 + 2)) return false;
            reader.ReadValue(out long id);
            reader.ReadValue(out int epoch);
            reader.ReadValue(out byte kind);
            reader.ReadValue(out byte mode);
            reader.ReadValue(out float x);
            reader.ReadValue(out float y);
            reader.ReadValue(out long target);
            reader.ReadValue(out int argument);
            reader.ReadValue(out ushort count);
            if (count > MaxActorsPerCommand || !reader.TryBeginRead(count * 8)) return false;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return false;
            var actors = new EntityId[count];
            for (int i = 0; i < count; i++)
            {
                reader.ReadValue(out long actor);
                actors[i] = new EntityId(actor);
            }
            // Kind and mode travel as raw bytes; the router answers Malformed for values outside the enums.
            command = new Command(id, epoch, boundSeat, (CommandKind)kind, actors, new SimVector2(x, y), new EntityId(target), (CommandMode)mode, argument);
            return true;
        }

        // ---- answer: host to the one client that sent the command ----

        public static FastBufferWriter WriteAnswer(CommandResolved answer)
        {
            var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteValueSafe(answer.Seat.Value);
            writer.WriteValueSafe(answer.Epoch);
            writer.WriteValueSafe(answer.CommandId);
            writer.WriteValueSafe((byte)answer.Rejection);
            writer.WriteValueSafe(answer.IsRepeat);
            writer.WriteValueSafe(answer.CurrentEpoch);
            writer.WriteValueSafe(answer.NextCommandId);
            return writer;
        }

        public static bool TryReadAnswer(ref FastBufferReader reader, out CommandResolved answer)
        {
            answer = null;
            if (!reader.TryBeginRead(4 + 4 + 8 + 1 + 1 + 4 + 8)) return false;
            reader.ReadValue(out int seat);
            reader.ReadValue(out int epoch);
            reader.ReadValue(out long id);
            reader.ReadValue(out byte rejection);
            reader.ReadValue(out bool isRepeat);
            reader.ReadValue(out int currentEpoch);
            reader.ReadValue(out long next);
            if (seat < 0) return false;
            answer = new CommandResolved(new SeatId(seat), epoch, id, (CommandRejection)rejection, isRepeat, currentEpoch, next);
            return true;
        }

        // ---- welcome: host to a client that just got a seat ----

        public static FastBufferWriter WriteWelcome(SeatId seat, int epoch, long nextCommandId)
        {
            var writer = new FastBufferWriter(24, Allocator.Temp);
            writer.WriteValueSafe(ProtocolVersion);
            writer.WriteValueSafe(seat.Value);
            writer.WriteValueSafe(epoch);
            writer.WriteValueSafe(nextCommandId);
            return writer;
        }

        public static bool TryReadWelcome(ref FastBufferReader reader, out ushort version, out SeatId seat, out int epoch, out long nextCommandId)
        {
            version = 0; seat = SeatId.None; epoch = 0; nextCommandId = 0;
            if (!reader.TryBeginRead(2 + 4 + 4 + 8)) return false;
            reader.ReadValue(out version);
            reader.ReadValue(out int seatValue);
            reader.ReadValue(out epoch);
            reader.ReadValue(out nextCommandId);
            if (seatValue < 1) return false;
            seat = new SeatId(seatValue);
            return true;
        }

        // ---- seat table ----

        public static FastBufferWriter WriteSeats(IReadOnlyList<SeatSnapshot> seats)
        {
            var writer = new FastBufferWriter(4 + seats.Count * 9, Allocator.Temp);
            writer.WriteValueSafe((byte)seats.Count);
            for (int i = 0; i < seats.Count; i++)
            {
                writer.WriteValueSafe(seats[i].Id.Value);
                writer.WriteValueSafe(seats[i].Team);
                writer.WriteValueSafe((byte)seats[i].Controller);
            }
            return writer;
        }

        public static bool TryReadSeats(ref FastBufferReader reader, List<SeatSnapshot> into)
        {
            into.Clear();
            if (!reader.TryBeginRead(1)) return false;
            reader.ReadValue(out byte count);
            if (!reader.TryBeginRead(count * 9)) return false;
            for (int i = 0; i < count; i++)
            {
                reader.ReadValue(out int id);
                reader.ReadValue(out int team);
                reader.ReadValue(out byte controller);
                if (id < 0) return false;
                into.Add(new SeatSnapshot { Id = new SeatId(id), Team = team, Controller = (SeatController)controller });
            }
            return true;
        }

        // ---- snapshot: host to every client, the whole visible state ----

        public static FastBufferWriter WriteSnapshot(long tick, IReadOnlyList<EntitySnapshot> entities)
        {
            // The reader refuses more than the cap, so the writer must never produce it: clients would freeze with no message.
            int count = System.Math.Min(entities.Count, MaxEntitiesPerSnapshot);
            var writer = new FastBufferWriter(16 + count * EntityBytes, Allocator.Temp);
            writer.WriteValueSafe(tick);
            writer.WriteValueSafe(count);
            for (int i = 0; i < count; i++)
            {
                EntitySnapshot e = entities[i];
                writer.WriteValueSafe(e.Id.Value);
                writer.WriteValueSafe(e.DefinitionIndex);
                writer.WriteValueSafe((byte)e.Kind);
                writer.WriteValueSafe(e.Owner.Value);
                writer.WriteValueSafe(e.Position.X);
                writer.WriteValueSafe(e.Position.Y);
                writer.WriteValueSafe(e.PackTotal);
                writer.WriteValueSafe(e.PackCapacity);
                writer.WriteValueSafe(e.NodeRemaining);
                writer.WriteValueSafe((byte)e.Task);
                writer.WriteValueSafe((byte)e.TaskState);
                writer.WriteValueSafe((byte)e.TaskReason);
                writer.WriteValueSafe(e.BuildProgress);
                writer.WriteValueSafe(e.IsSite);
                writer.WriteValueSafe(e.HealthFraction);
                writer.WriteValueSafe(e.GateOpen);
            }
            return writer;
        }

        public static bool TryReadSnapshot(ref FastBufferReader reader, out long tick, List<EntitySnapshot> into)
        {
            into.Clear();
            tick = 0;
            if (!reader.TryBeginRead(8 + 4)) return false;
            reader.ReadValue(out tick);
            reader.ReadValue(out int count);
            if (count < 0 || count > MaxEntitiesPerSnapshot || !reader.TryBeginRead(count * EntityBytes)) return false;
            for (int i = 0; i < count; i++)
            {
                var e = new EntitySnapshot();
                reader.ReadValue(out long id);
                reader.ReadValue(out e.DefinitionIndex);
                reader.ReadValue(out byte kind);
                reader.ReadValue(out int owner);
                reader.ReadValue(out float x);
                reader.ReadValue(out float y);
                reader.ReadValue(out e.PackTotal);
                reader.ReadValue(out e.PackCapacity);
                reader.ReadValue(out e.NodeRemaining);
                reader.ReadValue(out byte task);
                reader.ReadValue(out byte state);
                reader.ReadValue(out byte reason);
                reader.ReadValue(out e.BuildProgress);
                reader.ReadValue(out e.IsSite);
                reader.ReadValue(out e.HealthFraction);
                reader.ReadValue(out e.GateOpen);
                if (owner < 0 || float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return false;
                e.Id = new EntityId(id);
                e.Kind = (EntityKind)kind;
                e.Owner = new SeatId(owner);
                e.Position = new SimVector2(x, y);
                e.Task = (TaskKindCode)task;
                e.TaskState = (TaskState)state;
                e.TaskReason = (TaskReason)reason;
                into.Add(e);
            }
            return true;
        }
    }
}
