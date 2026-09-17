using JurassicPark.Simulation;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>Short code for what an entity is doing, small enough for the wire. The simulation's task kinds are strings; this is their presentation-side index.</summary>
    public enum TaskKindCode : byte { None = 0, Move = 1, Gather = 2, Deliver = 3, Pickup = 4, Other = 255 }

    /// <summary>
    /// Everything the screen needs to know about one entity at one tick. The host fills these from the simulation, a client from
    /// the network, and nothing that draws or handles input can tell which: that is the whole seam between playing alone and
    /// playing together.
    /// </summary>
    public struct EntitySnapshot
    {
        public EntityId Id;
        public ushort DefinitionIndex;
        public EntityKind Kind;
        public SeatId Owner;
        public SimVector2 Position;
        public ushort PackTotal;
        public ushort PackCapacity;
        public int NodeRemaining;
        public TaskKindCode Task;
        public TaskState TaskState;
        public TaskReason TaskReason;

        public static TaskKindCode CodeOf(string taskKind)
        {
            switch (taskKind)
            {
                case null: return TaskKindCode.None;
                case "move": return TaskKindCode.Move;
                case "gather": return TaskKindCode.Gather;
                case "deliver": return TaskKindCode.Deliver;
                case "pickup": return TaskKindCode.Pickup;
                default: return TaskKindCode.Other;
            }
        }
    }

    public struct SeatSnapshot
    {
        public SeatId Id;
        public int Team;
        public SeatController Controller;
    }
}
