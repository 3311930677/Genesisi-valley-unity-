using System;

namespace GenesisValley.Core
{
    /// <summary>Stable entity identifier. All cross-entity relations store ids, never scene objects.</summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);
        public readonly int Value;

        public EntityId(int value) { Value = value; }

        public bool IsValid => Value > 0;
        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
        public override string ToString() => IsValid ? "#" + Value : "#none";
    }

    public sealed class IdAllocator
    {
        private int _next = 1;
        public int NextValue => _next;
        public EntityId Next() => new EntityId(_next++);
        public void Restore(int next)
        {
            if (next < 1) throw new ArgumentOutOfRangeException(nameof(next));
            _next = next;
        }
    }
}
