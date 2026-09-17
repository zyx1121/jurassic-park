using System;

namespace JurassicPark.Simulation
{
    /// <summary>Stable identity of one task, never reused, so a late result can be matched against the work that asked for it.</summary>
    public readonly struct TaskId : IEquatable<TaskId>
    {
        public static readonly TaskId None = new TaskId(0);

        public long Value { get; }

        public TaskId(long value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(TaskId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TaskId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => IsNone ? "Task(none)" : $"Task({Value})";

        public static bool operator ==(TaskId a, TaskId b) => a.Equals(b);
        public static bool operator !=(TaskId a, TaskId b) => !a.Equals(b);
    }
}
