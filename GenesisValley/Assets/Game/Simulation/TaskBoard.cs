using System;
using System.Collections.Generic;
using GenesisValley.Core;

namespace GenesisValley.Simulation
{
    public enum TaskType
    {
        ChopTree = 1,
        Supply = 2,
        Build = 3,
        Sow = 4,
        Harvest = 5,
        Fish = 6,
        FetchWater = 7,
        HaulPile = 8,
    }

    public enum TaskState
    {
        Open = 0,
        Reserved = 1,
        Done = 2,
        Failed = 3,
    }

    public sealed class WorkTask
    {
        public EntityId Id;
        public TaskType Type;
        /// <summary>Tree / building / pile the work happens at.</summary>
        public EntityId TargetId;
        /// <summary>Optional pick-up inventory (storage for Supply).</summary>
        public EntityId SourceId;
        /// <summary>Where produced/carried items should go (storage or construction site).</summary>
        public EntityId DestId;
        /// <summary>Items this task promises to bring to <see cref="DestId"/> (material reservation).</summary>
        public int Amount;
        public TaskState State;
        public readonly List<EntityId> Assignees = new List<EntityId>();
        public int MaxAssignees = 1;
        public int FailCount;
        public long CooldownUntilTick;
        public long CreatedTick;
        public float Priority;

        public bool IsActive => State == TaskState.Open || State == TaskState.Reserved;
        public bool HasSlot => IsActive && Assignees.Count < MaxAssignees;
        public EntityId Assignee => Assignees.Count > 0 ? Assignees[0] : EntityId.None;
    }

    /// <summary>
    /// Task publication, claiming and reservation (GDD §6). The planner publishes; villagers claim; every exit path
    /// (complete, fail, abandon, owner gone) releases the reservation.
    /// </summary>
    public sealed class TaskBoard
    {
        public readonly List<WorkTask> Tasks = new List<WorkTask>();

        public WorkTask Find(EntityId id)
        {
            if (!id.IsValid) return null;
            for (int i = 0; i < Tasks.Count; i++) if (Tasks[i].Id == id) return Tasks[i];
            return null;
        }

        public bool HasActiveTaskFor(EntityId target, TaskType type)
        {
            foreach (var t in Tasks) if (t.TargetId == target && t.Type == type && t.IsActive) return true;
            return false;
        }

        public bool HasActiveTaskFor(EntityId target)
        {
            foreach (var t in Tasks) if (t.TargetId == target && t.IsActive) return true;
            return false;
        }

        public int CountActive(TaskType type)
        {
            int n = 0;
            foreach (var t in Tasks) if (t.Type == type && t.IsActive) n++;
            return n;
        }

        public int CountOpen(TaskType type)
        {
            int n = 0;
            foreach (var t in Tasks) if (t.Type == type && t.State == TaskState.Open) n++;
            return n;
        }

        /// <summary>Items promised to a destination by active tasks (material reservations in transit or planned).</summary>
        public int IncomingTo(EntityId dest)
        {
            int n = 0;
            foreach (var t in Tasks) if (t.IsActive && t.DestId == dest && (t.Type == TaskType.Supply || t.Type == TaskType.ChopTree)) n += t.Amount;
            return n;
        }

        public WorkTask Publish(WorldState w, TaskType type, EntityId target, EntityId dest, int amount = 0, int maxAssignees = 1, float priority = 0f)
        {
            var t = new WorkTask
            {
                Id = w.Ids.Next(), Type = type, TargetId = target, DestId = dest, Amount = amount, State = TaskState.Open,
                CreatedTick = w.Tick, MaxAssignees = Math.Max(1, maxAssignees), Priority = priority,
            };
            Tasks.Add(t);
            return t;
        }

        public void Assign(WorldState w, WorkTask t, Villager v)
        {
            if (!t.Assignees.Contains(v.Id)) t.Assignees.Add(v.Id);
            t.State = TaskState.Reserved;
            v.TaskId = t.Id;
            v.TaskStartedTick = w.Tick;
            if (t.Type == TaskType.ChopTree)
            {
                var tree = w.FindTree(t.TargetId);
                if (tree != null) tree.ReservedBy = v.Id;
            }
            else if (t.Type == TaskType.HaulPile)
            {
                var p = w.FindPile(t.TargetId);
                if (p != null) p.ReservedBy = v.Id;
            }
        }

        /// <summary>Finishes the task for everybody; releases every reservation.</summary>
        public void Complete(WorldState w, WorkTask t)
        {
            if (t == null) return;
            foreach (var id in t.Assignees.ToArray()) Detach(w, t, id);
            t.Assignees.Clear();
            ReleaseTarget(w, t);
            t.State = TaskState.Done;
        }

        /// <summary>Removes one worker from a task (others keep working); the task reopens when nobody is left.</summary>
        public void Leave(WorldState w, WorkTask t, Villager v)
        {
            if (t == null) return;
            Detach(w, t, v.Id);
            if (t.Assignees.Count == 0 && t.IsActive)
            {
                ReleaseTarget(w, t);
                t.State = TaskState.Open;
            }
        }

        /// <summary>Releases a task back to Open (or Failed after too many failures) and frees the target reservation.</summary>
        public void Release(WorldState w, WorkTask t, Villager v, bool countAsFailure, string reason)
        {
            if (t == null) return;
            if (v != null) Detach(w, t, v.Id);
            if (countAsFailure)
            {
                t.FailCount++;
                t.CooldownUntilTick = w.Tick + w.Config.SecondsToTicks(w.Config.TaskFailCooldownSeconds);
            }
            if (t.Assignees.Count > 0 && !countAsFailure) return;
            foreach (var id in t.Assignees.ToArray()) Detach(w, t, id);
            t.Assignees.Clear();
            ReleaseTarget(w, t);
            if (t.FailCount >= w.Config.MaxTaskFailures)
            {
                t.State = TaskState.Failed;
                var tree = w.FindTree(t.TargetId);
                if (tree != null)
                {
                    tree.PathFailures++;
                    tree.PlanCooldownUntilTick = w.Tick + w.Config.SecondsToTicks(w.Config.TaskFailCooldownSeconds * 4);
                }
                w.TryGetPosition(t.TargetId, out float x, out float z);
                w.Emit(SimEventType.TaskAbandoned, v != null ? v.Id : EntityId.None, t.TargetId, x, z, "任务 " + t.Id + " 多次失败后放弃：" + reason);
            }
            else
            {
                t.State = TaskState.Open;
            }
        }

        /// <summary>Cancels every active task that references an entity (target removed, site cancelled, river undone...).</summary>
        public void CancelFor(WorldState w, EntityId entity)
        {
            foreach (var t in Tasks)
            {
                if (!t.IsActive) continue;
                if (t.TargetId != entity && t.DestId != entity && t.SourceId != entity) continue;
                foreach (var id in t.Assignees.ToArray())
                {
                    var v = w.FindVillager(id);
                    Detach(w, t, id);
                    if (v != null) VillagerBrain.Interrupt(w, v, "任务目标已失效");
                }
                t.Assignees.Clear();
                ReleaseTarget(w, t);
                t.State = TaskState.Done;
            }
        }

        private static void Detach(WorldState w, WorkTask t, EntityId villagerId)
        {
            t.Assignees.Remove(villagerId);
            var v = w.FindVillager(villagerId);
            if (v != null && v.TaskId == t.Id) v.TaskId = EntityId.None;
            if (t.Type == TaskType.ChopTree)
            {
                var tree = w.FindTree(t.TargetId);
                if (tree != null && tree.ReservedBy == villagerId) tree.ReservedBy = EntityId.None;
            }
            else if (t.Type == TaskType.HaulPile)
            {
                var p = w.FindPile(t.TargetId);
                if (p != null && p.ReservedBy == villagerId) p.ReservedBy = EntityId.None;
            }
        }

        private static void ReleaseTarget(WorldState w, WorkTask t)
        {
            if (t.Type == TaskType.ChopTree)
            {
                var tree = w.FindTree(t.TargetId);
                if (tree != null && !t.Assignees.Contains(tree.ReservedBy)) tree.ReservedBy = EntityId.None;
            }
        }

        /// <summary>Drops finished tasks so the board does not grow forever.</summary>
        public void Prune()
        {
            Tasks.RemoveAll(t => t.State == TaskState.Done || t.State == TaskState.Failed);
        }

        /// <summary>Consistency check used by tests and after load: every reservation belongs to a live assignee.</summary>
        public int ValidateReservations(WorldState w)
        {
            int fixedCount = 0;
            foreach (var tree in w.Trees)
            {
                if (!tree.ReservedBy.IsValid) continue;
                var v = w.FindVillager(tree.ReservedBy);
                var t = v != null ? Find(v.TaskId) : null;
                if (t == null || t.Type != TaskType.ChopTree || t.TargetId != tree.Id) { tree.ReservedBy = EntityId.None; fixedCount++; }
            }
            foreach (var t in Tasks)
            {
                if (!t.IsActive) continue;
                for (int i = t.Assignees.Count - 1; i >= 0; i--)
                {
                    var v = w.FindVillager(t.Assignees[i]);
                    if (v == null || v.TaskId != t.Id) { t.Assignees.RemoveAt(i); fixedCount++; }
                }
                if (t.Assignees.Count == 0 && t.State == TaskState.Reserved) { t.State = TaskState.Open; fixedCount++; }
            }
            foreach (var v in w.Villagers)
            {
                if (!v.TaskId.IsValid) continue;
                var t = Find(v.TaskId);
                if (t == null || !t.IsActive || !t.Assignees.Contains(v.Id)) { v.TaskId = EntityId.None; fixedCount++; }
            }
            return fixedCount;
        }
    }
}
