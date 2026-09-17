using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    [Serializable]
    public sealed class InfraRules
    {
        public float MoveSpeed = 5f;
        public float GatherSeconds = .3f;
        public float TransferSeconds = .2f;
        public float BuildSeconds = 3f;
        public float AttackInterval = .6f;
        public float AttackDamage = 10f;
        public float WorkerHealth = 100f;
        public float DinosaurHealth = 120f;
        public float WallHealth = 60f;
        public float ReplanSeconds = .5f;
        public int CarryCapacity = 5;
        public int WallCost = 10;
        public int DepotCapacity = 200;
        public float StructureHealth = 100f;
        public float MaxTickSeconds = .1f;
        public float ArrivalTolerance = .001f;
        public int EventLogCapacity = 128;

        internal void Validate(float cellSize)
        {
            float[] positive = { MoveSpeed, AttackInterval, AttackDamage, WorkerHealth,
                DinosaurHealth, WallHealth, ReplanSeconds, StructureHealth, MaxTickSeconds,
                ArrivalTolerance };
            foreach (float value in positive)
                if (!InfraMap.Finite(value) || value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(InfraRules), "Rule values must be finite and positive.");
            float[] nonNegative = { GatherSeconds, TransferSeconds, BuildSeconds };
            foreach (float value in nonNegative)
                if (!InfraMap.Finite(value) || value < 0)
                    throw new ArgumentOutOfRangeException(nameof(InfraRules), "Durations must be finite and nonnegative.");
            if (CarryCapacity <= 0 || WallCost <= 0 || DepotCapacity <= 0 ||
                EventLogCapacity <= 0 || ArrivalTolerance >= cellSize)
                throw new ArgumentOutOfRangeException(nameof(InfraRules), "Capacities and event limit must be positive.");
        }
    }

    public enum InfraEntityKind { Worker, Dinosaur, Source, Depot, Blueprint, Wall, GroundPile }
    public enum InfraTaskStatus { Idle, Queued, Planning, Running, Blocked, Completed, Cancelled, Failed }
    public enum InfraCommandKind { Move, Gather, Build, Attack, Stop, CancelBuild }

    public sealed class InfraEntity
    {
        public int Id { get; internal set; }
        public int Owner { get; internal set; }
        public InfraEntityKind Kind { get; internal set; }
        public Vector2 Position { get; internal set; }
        public Vector2Int Cell { get; internal set; }
        public float Health { get; internal set; }
        public int Stored { get; internal set; }
        public int Reserved => OutgoingReserved + IncomingReserved;
        public int OutgoingReserved { get; internal set; }
        public int IncomingReserved { get; internal set; }
        public int Carried { get; internal set; }
        public int Delivered { get; internal set; }
        public int Capacity { get; internal set; }
        public InfraTaskStatus TaskStatus { get; internal set; }
        public string Action { get; internal set; } = "Idle";
        public string Reason { get; internal set; } = "";
        public int TargetId { get; internal set; }
        public bool IsAlive => Health > 0;
        public int RequiredMaterials { get; internal set; }
        public float BuildProgress { get; internal set; }

        internal bool MaterialsConsumed;
        internal InfraTask Task;
    }

    public readonly struct InfraCommand : IEquatable<InfraCommand>
    {
        public long CommandId { get; }
        public int Owner { get; }
        public int ActorId { get; }
        public InfraCommandKind Kind { get; }
        public Vector2Int Cell { get; }
        public int TargetId { get; }

        public InfraCommand(long commandId, int owner, int actorId, InfraCommandKind kind,
            Vector2Int cell, int targetId = 0)
        {
            CommandId = commandId;
            Owner = owner;
            ActorId = actorId;
            Kind = kind;
            Cell = cell;
            TargetId = targetId;
        }

        public bool Equals(InfraCommand other) => CommandId == other.CommandId &&
            Owner == other.Owner && ActorId == other.ActorId && Kind == other.Kind &&
            Cell == other.Cell && TargetId == other.TargetId;
        public override bool Equals(object obj) => obj is InfraCommand other && Equals(other);
        public override int GetHashCode() => CommandId.GetHashCode() ^ Owner ^ ActorId ^
            (int)Kind ^ Cell.GetHashCode() ^ TargetId;
    }

    public readonly struct InfraCommandResult
    {
        public bool Accepted { get; }
        public string Reason { get; }
        public int TargetId { get; }

        internal InfraCommandResult(bool accepted, string reason, int targetId = 0)
        {
            Accepted = accepted;
            Reason = reason;
            TargetId = targetId;
        }
    }

    public readonly struct InfraEvent
    {
        public float Time { get; }
        public int EntityId { get; }
        public string Message { get; }
        internal InfraEvent(float time, int entityId, string message)
        {
            Time = time;
            EntityId = entityId;
            Message = message;
        }
    }

    internal enum InfraPhase { Move, ChooseHaul, Pickup, Dropoff, Construct, Attack }

    internal sealed class InfraTask
    {
        public InfraCommand Command;
        public int TargetId;
        public InfraPhase Phase;
        public InfraPlan Plan;
        public float Elapsed;
        public float RetryAt;
        public int BlockedRevision;
        public int SourceId;
        public int SourceAmount;
        public int DestinationId;
        public int DestinationAmount;
        public Vector2Int AttackCell;
        public bool HasAttackCell;
        public int AttackVictim;
    }

    internal sealed class InfraPlan
    {
        public List<Vector2Int> Cells;
        public List<Vector2Int> Goals;
        public int Index;
        public int Revision;
        public bool AllowBreach;
    }
}
