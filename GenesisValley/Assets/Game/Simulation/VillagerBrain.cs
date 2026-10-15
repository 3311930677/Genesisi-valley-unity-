using System;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>
    /// Per-villager needs, utility decisions and action execution (GDD §6).
    /// Decisions are evaluated staggered (every few ticks) and only while idle or at action boundaries;
    /// carried items are always delivered before anything else; unreachable targets interrupt immediately.
    /// </summary>
    public sealed class VillagerBrain
    {
        // ================================================================ tick

        public void Tick(WorldState w, Villager v)
        {
            var c = w.Config;
            UpdateNeeds(w, v);

            switch (v.State)
            {
                case VillagerState.Walking:
                    if (v.NeedsRepath)
                    {
                        if (w.PathBudget <= 0) break; // wait for next tick's budget (GDD §12: per-frame path limit)
                        w.PathBudget--;
                        if (!PlanPath(w, v)) { OnPathFailed(w, v); break; }
                    }
                    if (StepAlongPath(w, v)) OnArrived(w, v);
                    break;
                case VillagerState.Working:
                    if (--v.ActionTicksLeft <= 0) FinishAction(w, v);
                    break;
                case VillagerState.Sleeping:
                    TickSleep(w, v);
                    break;
                default:
                    if (v.IdleTicksLeft > 0)
                    {
                        v.IdleTicksLeft--;
                        // urgent needs cut idle/social time short
                        if (v.Action == VillagerAction.Sit && !w.IsEvening && !w.IsNight) v.IdleTicksLeft = 0;
                        break;
                    }
                    if ((w.Tick + v.Id.Value) % c.AiEvaluateEveryTicks == 0) Decide(w, v);
                    break;
            }
        }

        private static void UpdateNeeds(WorldState w, Villager v)
        {
            var c = w.Config;
            float dayFrac = c.TickSeconds / c.SecondsPerGameDay;
            float sleepMul = v.State == VillagerState.Sleeping ? 0.5f : 1f;
            v.Hunger = Math.Min(3f, v.Hunger + c.FoodPerAdultPerDay * dayFrac * sleepMul);
            v.Thirst = Math.Min(3f, v.Thirst + c.WaterPerAdultPerDay * dayFrac * sleepMul);
            if (v.State != VillagerState.Sleeping)
            {
                float drain = c.EnergyDrainPerDay * dayFrac * (v.State == VillagerState.Working ? 1.25f : 1f);
                v.Energy = Math.Max(0f, v.Energy - drain);
            }

            bool starving = v.Hunger >= c.StarvingThreshold || v.Thirst >= c.StarvingThreshold;
            if (starving) v.Health -= c.HealthLossPerDay * dayFrac;
            else if (v.Hunger < 1.2f && v.Thirst < 1.2f) v.Health += c.HealthRegenPerDay * dayFrac;
            float floor = c.Difficulty == Difficulty.Easy ? c.EasyModeMinHealth : 0.05f;
            v.Health = v.Health < floor ? floor : (v.Health > 1f ? 1f : v.Health);

            if (!v.Weak && v.Health < 0.5f)
            {
                v.Weak = true;
                string why = v.Thirst >= v.Hunger ? "缺水" : "缺粮";
                w.Emit(SimEventType.VillagerWeak, v.Id, EntityId.None, v.X, v.Z, v.Name + " 因长时间" + why + "而虚弱，只能休息和进食");
            }
            else if (v.Weak && v.Health > 0.62f)
            {
                v.Weak = false;
                w.Emit(SimEventType.VillagerRecovered, v.Id, EntityId.None, v.X, v.Z, v.Name + " 恢复了体力");
            }
        }

        // ================================================================ decisions

        private void Decide(WorldState w, Villager v)
        {
            var c = w.Config;
            v.Action = VillagerAction.None;

            // 1. Never drop what we carry: finish delivery first (GDD §6).
            if (!v.Carry.IsEmpty)
            {
                if (!StartDropOff(w, v)) { v.IdleTicksLeft = c.SecondsToTicks(3f); v.Action = VillagerAction.Wait; }
                return;
            }

            // 2. Survival needs.
            if (v.Thirst >= c.ThirstToDrink && TryStartDrink(w, v)) return;
            if (v.Hunger >= c.HungerToEat && TryStartEat(w, v)) return;

            // 3. Sleep.
            if (w.IsNight || v.Energy < 0.12f || (v.Weak && v.Energy < 0.5f)) { StartGoToBed(w, v); return; }
            if (v.Weak) { StartRest(w, v); return; }

            // 4. Evening by the fire (diligent villagers keep working a little longer).
            bool evening = w.IsEvening && !(v.Diligence > 0.72f && w.Hour < w.Config.EveningSocialHour + 1.2f);

            // 5. Continue / choose work.
            var current = w.Tasks.Find(v.TaskId);
            if (current != null && current.IsActive && current.Assignees.Contains(v.Id) && !evening)
            {
                if (ContinueTask(w, v, current)) return;
            }
            else if (current != null) w.Tasks.Leave(w, current, v);

            if (!evening)
            {
                var best = ChooseTask(w, v);
                if (best != null)
                {
                    w.Tasks.Assign(w, best, v);
                    if (ContinueTask(w, v, best)) return;
                    w.Tasks.Leave(w, best, v);
                }
            }

            // 6. Free time.
            if (evening || w.Rng.NextFloat() < 0.25f + 0.4f * v.Sociability) StartSocial(w, v);
            else StartWander(w, v);
        }

        /// <summary>Utility score per open task: type priority from village needs, role match, distance; hysteresis via MinTaskSeconds.</summary>
        private static WorkTask ChooseTask(WorldState w, Villager v)
        {
            int region = w.Nav.RegionOf(w.Nav.WorldToCell(v.X, v.Z));
            if (region == 0) region = w.Nav.RegionNear(v.X, v.Z, 4);
            WorkTask best = null; float bestScore = float.MinValue;
            float foodDays = w.FoodDays, waterDays = w.WaterDays;
            foreach (var t in w.Tasks.Tasks)
            {
                if (!t.HasSlot || t.CooldownUntilTick > w.Tick) continue;
                if (!TargetPosition(w, t, out float tx, out float tz)) continue;
                if (w.Nav.RegionNear(tx, tz, 3) != region) continue;

                float score = BaseScore(t, foodDays, waterDays) + t.Priority;
                if (RoleMatches(v.Role, t.Type)) score += 22f;
                float d = (float)Math.Sqrt((tx - v.X) * (tx - v.X) + (tz - v.Z) * (tz - v.Z));
                score -= d / 5f;
                if (t.Type == TaskType.Build) score += 4f * t.Assignees.Count; // join colleagues
                score += (v.Id.Value * 7 + t.Id.Value * 13) % 5 * 0.1f; // deterministic tie spread

                if (!CanStart(w, v, t, region)) continue;
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        private static float BaseScore(WorkTask t, float foodDays, float waterDays)
        {
            switch (t.Type)
            {
                case TaskType.FetchWater: return waterDays < 0.4f ? 75f : 46f;
                case TaskType.Harvest: return 62f;
                case TaskType.Sow: return 52f;
                case TaskType.Fish: return foodDays < 1.2f ? 58f : 40f;
                case TaskType.Supply: return 45f;
                case TaskType.Build: return 43f;
                case TaskType.ChopTree: return t.DestId.IsValid && t.Priority > 0 ? 40f : 31f;
                case TaskType.HaulPile: return 36f;
                default: return 30f;
            }
        }

        private static bool RoleMatches(JobRole r, TaskType t)
        {
            switch (r)
            {
                case JobRole.Woodcutter: return t == TaskType.ChopTree;
                case JobRole.Builder: return t == TaskType.Build || t == TaskType.Supply;
                case JobRole.Farmer: return t == TaskType.Sow || t == TaskType.Harvest;
                case JobRole.Fisher: return t == TaskType.Fish;
                case JobRole.WaterCarrier: return t == TaskType.FetchWater || t == TaskType.HaulPile;
                default: return t == TaskType.Supply || t == TaskType.HaulPile || t == TaskType.ChopTree;
            }
        }

        private static bool CanStart(WorldState w, Villager v, WorkTask t, int region)
        {
            switch (t.Type)
            {
                case TaskType.ChopTree:
                {
                    var tree = w.FindTree(t.TargetId);
                    return tree != null && tree.Harvestable && (!tree.ReservedBy.IsValid || tree.ReservedBy == v.Id);
                }
                case TaskType.Supply:
                    return FindSupplySource(w, v, region) != null;
                case TaskType.FetchWater:
                    return w.Water.NearestShore(v.X, v.Z, region, out _, out _);
                case TaskType.HaulPile:
                {
                    var p = w.FindPile(t.TargetId);
                    return p != null && !p.Inventory.IsEmpty && (!p.ReservedBy.IsValid || p.ReservedBy == v.Id);
                }
                case TaskType.Build:
                {
                    var b = w.FindBuilding(t.TargetId);
                    return b != null && !b.IsComplete && ConstructionSystem.CanWork(b);
                }
                default:
                    return w.FindBuilding(t.TargetId) != null;
            }
        }

        private static bool TargetPosition(WorldState w, WorkTask t, out float x, out float z)
        {
            if (t.Type == TaskType.Supply || t.Type == TaskType.FetchWater)
                return w.TryGetPosition(t.DestId, out x, out z);
            var b = w.FindBuilding(t.TargetId);
            if (b != null) { WorkSpot(w, b, 0, out x, out z); return true; }
            return w.TryGetPosition(t.TargetId, out x, out z);
        }

        private static StorageEntity FindSupplySource(WorldState w, Villager v, int region)
        {
            StorageEntity best = null; float bestD = float.MaxValue;
            foreach (var s in w.Storages)
            {
                if (s.Inventory.Get(ItemType.Wood) <= 0) continue;
                if (w.Nav.RegionNear(s.X, s.Z, 4) != region) continue;
                float d = (s.X - v.X) * (s.X - v.X) + (s.Z - v.Z) * (s.Z - v.Z);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>Starts (or resumes) the next step of a claimed task. Returns false if it cannot proceed.</summary>
        private bool ContinueTask(WorldState w, Villager v, WorkTask t)
        {
            switch (t.Type)
            {
                case TaskType.ChopTree:
                {
                    var tree = w.FindTree(t.TargetId);
                    if (tree == null || !tree.Harvestable) { w.Tasks.Complete(w, t); return false; }
                    v.DropId = t.DestId;
                    BeginWalk(w, v, WalkPurpose.Task, tree.X, tree.Z, tree.Id);
                    return true;
                }
                case TaskType.Supply:
                {
                    int region = w.Nav.RegionNear(v.X, v.Z, 4);
                    var src = FindSupplySource(w, v, region);
                    var site = w.FindBuilding(t.DestId);
                    if (src == null || site == null || site.IsComplete) { w.Tasks.Complete(w, t); return false; }
                    t.SourceId = src.Id;
                    v.DropId = site.Id;
                    BeginWalk(w, v, WalkPurpose.PickUp, src.X, src.Z, src.Id);
                    return true;
                }
                case TaskType.Build:
                {
                    var b = w.FindBuilding(t.TargetId);
                    if (b == null || b.IsComplete) { w.Tasks.Complete(w, t); return false; }
                    if (!ConstructionSystem.CanWork(b)) { w.Tasks.Leave(w, t, v); return false; }
                    int slot = t.Assignees.IndexOf(v.Id);
                    WorkSpot(w, b, slot < 0 ? 0 : slot, out float x, out float z);
                    BeginWalk(w, v, WalkPurpose.Task, x, z, b.Id);
                    return true;
                }
                case TaskType.Sow:
                case TaskType.Harvest:
                case TaskType.Fish:
                {
                    var b = w.FindBuilding(t.TargetId);
                    if (b == null || !b.IsComplete) { w.Tasks.Complete(w, t); return false; }
                    if (t.Type == TaskType.Sow && b.Crop != CropState.Fallow) { w.Tasks.Complete(w, t); return false; }
                    if (t.Type == TaskType.Harvest && (b.Crop != CropState.Ripe || b.CropFood <= 0)) { w.Tasks.Complete(w, t); return false; }
                    int slot = t.Assignees.IndexOf(v.Id);
                    WorkSpot(w, b, slot < 0 ? 0 : slot + (int)(w.Tick / 300 % 3), out float x, out float z);
                    BeginWalk(w, v, WalkPurpose.Task, x, z, b.Id);
                    return true;
                }
                case TaskType.FetchWater:
                {
                    int region = w.Nav.RegionNear(v.X, v.Z, 4);
                    var dest = w.FindStorage(t.DestId);
                    float sx = dest != null ? dest.X : v.X, sz = dest != null ? dest.Z : v.Z;
                    if (!w.Water.NearestShore(sx, sz, region, out var shore, out _)) { w.Tasks.Release(w, t, v, true, "没有可达的取水点"); return false; }
                    w.Nav.CellCenter(shore, out float x, out float z);
                    v.DropId = t.DestId;
                    BeginWalk(w, v, WalkPurpose.Task, x, z, EntityId.None);
                    return true;
                }
                case TaskType.HaulPile:
                {
                    var p = w.FindPile(t.TargetId);
                    if (p == null || p.Inventory.IsEmpty) { w.Tasks.Complete(w, t); return false; }
                    v.DropId = EntityId.None;
                    BeginWalk(w, v, WalkPurpose.PickUp, p.X, p.Z, p.Id);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Spread workers around a building so they do not stand inside each other.</summary>
        public static void WorkSpot(WorldState w, BuildingEntity b, int slot, out float x, out float z)
        {
            switch (b.Kind)
            {
                case BuildingKind.Field:
                {
                    float lx = ((slot % 3) - 1) * b.SizeX * 0.3f;
                    float lz = (((slot / 3) % 3) - 1) * b.SizeZ * 0.3f;
                    b.ToWorld(lx, lz, out x, out z);
                    return;
                }
                case BuildingKind.FishingHut:
                    if (b.IsComplete) { x = b.SpotX; z = b.SpotZ; return; }
                    break;
                case BuildingKind.Bridge:
                {
                    float ox = (slot % 2 == 0 ? 1 : -1) * 1.6f;
                    float dx = b.BX - b.AX, dz = b.BZ - b.AZ, l = (float)Math.Sqrt(dx * dx + dz * dz);
                    float px = l > 0 ? -dz / l : 0, pz = l > 0 ? dx / l : 0;
                    x = b.AX - dx / Math.Max(l, 1e-3f) * 1.2f + px * ox; z = b.AZ - dz / Math.Max(l, 1e-3f) * 1.2f + pz * ox;
                    return;
                }
            }
            // perimeter slots: front-left, front-right, left, right, back
            float hx = b.SizeX * 0.5f + 1.1f, hz = b.SizeZ * 0.5f + 1.1f;
            switch (slot % 5)
            {
                case 0: b.ToWorld(-b.SizeX * 0.25f, hz, out x, out z); break;
                case 1: b.ToWorld(b.SizeX * 0.25f, hz, out x, out z); break;
                case 2: b.ToWorld(-hx, 0, out x, out z); break;
                case 3: b.ToWorld(hx, 0, out x, out z); break;
                default: b.ToWorld(0, -hz, out x, out z); break;
            }
        }

        // ---------------------------------------------------------------- needs actions

        private bool TryStartEat(WorldState w, Villager v)
        {
            int region = w.Nav.RegionNear(v.X, v.Z, 4);
            StorageEntity best = null; float bestD = float.MaxValue;
            foreach (var s in w.Storages)
            {
                if (s.Inventory.Get(ItemType.Food) <= 0 || w.Nav.RegionNear(s.X, s.Z, 4) != region) continue;
                float d = (s.X - v.X) * (s.X - v.X) + (s.Z - v.Z) * (s.Z - v.Z);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null)
            {
                if (w.Tick - w.LastFoodShortageTick > w.Config.SecondsToTicks(90))
                {
                    w.LastFoodShortageTick = w.Tick;
                    var camp = w.Camp;
                    w.Emit(SimEventType.FoodShortage, v.Id, camp != null ? camp.Id : EntityId.None, camp != null ? camp.X : v.X, camp != null ? camp.Z : v.Z,
                        "仓库里没有食物了，" + v.Name + " 饿着肚子继续工作");
                }
                return false;
            }
            LeaveCurrentTask(w, v);
            Approach(w, best.X, best.Z, v, 2.2f, out float x, out float z);
            BeginWalk(w, v, WalkPurpose.Food, x, z, best.Id);
            return true;
        }

        private bool TryStartDrink(WorldState w, Villager v)
        {
            int region = w.Nav.RegionNear(v.X, v.Z, 4);
            StorageEntity best = null; float bestD = float.MaxValue;
            foreach (var s in w.Storages)
            {
                if (s.Inventory.Get(ItemType.Water) <= 0 || w.Nav.RegionNear(s.X, s.Z, 4) != region) continue;
                float d = (s.X - v.X) * (s.X - v.X) + (s.Z - v.Z) * (s.Z - v.Z);
                if (d < bestD) { bestD = d; best = s; }
            }
            bool shore = w.Water.NearestShore(v.X, v.Z, region, out var shoreCell, out float shoreDist);
            if (best != null && (!shore || Math.Sqrt(bestD) < shoreDist + 25f))
            {
                LeaveCurrentTask(w, v);
                Approach(w, best.X, best.Z, v, 2.2f, out float x, out float z);
                BeginWalk(w, v, WalkPurpose.Water, x, z, best.Id);
                return true;
            }
            if (shore)
            {
                LeaveCurrentTask(w, v);
                w.Nav.CellCenter(shoreCell, out float x, out float z);
                BeginWalk(w, v, WalkPurpose.Water, x, z, EntityId.None);
                return true;
            }
            if (w.Tick - w.LastWaterShortageTick > w.Config.SecondsToTicks(90))
            {
                w.LastWaterShortageTick = w.Tick;
                w.Emit(SimEventType.WaterShortage, v.Id, EntityId.None, v.X, v.Z, v.Name + " 找不到可以喝的水");
            }
            return false;
        }

        private void StartGoToBed(WorldState w, Villager v)
        {
            LeaveCurrentTask(w, v);
            var home = w.FindBuilding(v.HomeId);
            if (home != null && home.IsComplete)
            {
                home.DoorPoint(out float x, out float z);
                BeginWalk(w, v, WalkPurpose.Bed, x, z, home.Id);
                return;
            }
            var camp = w.FindStorage(v.CampId) ?? w.Camp;
            if (camp == null) { BeginSleep(w, v, false); return; }
            CampSpot(camp, v, 5.2f, out float cx, out float cz);
            BeginWalk(w, v, WalkPurpose.Bed, cx, cz, camp.Id);
        }

        private void StartRest(WorldState w, Villager v)
        {
            LeaveCurrentTask(w, v);
            v.State = VillagerState.Idle;
            v.Action = VillagerAction.Sit;
            v.IdleTicksLeft = w.Config.SecondsToTicks(8f);
        }

        private void StartSocial(WorldState w, Villager v)
        {
            var camp = w.FindStorage(v.CampId) ?? w.Camp;
            if (camp == null) { StartWander(w, v); return; }
            CampSpot(camp, v, 3.4f, out float x, out float z);
            float d2 = (x - v.X) * (x - v.X) + (z - v.Z) * (z - v.Z);
            if (d2 < 1.2f)
            {
                v.State = VillagerState.Idle;
                v.Action = VillagerAction.Sit;
                v.FacingDegrees = (float)(Math.Atan2(camp.X - v.X, camp.Z - v.Z) * 180.0 / Math.PI);
                v.IdleTicksLeft = w.Config.SecondsToTicks(10f + 10f * v.Sociability);
                return;
            }
            BeginWalk(w, v, WalkPurpose.Social, x, z, camp.Id);
        }

        private void StartWander(WorldState w, Villager v)
        {
            var camp = w.FindStorage(v.CampId) ?? w.Camp;
            float ox = camp != null ? camp.X : v.X, oz = camp != null ? camp.Z : v.Z;
            var home = w.FindBuilding(v.HomeId);
            if (home != null && home.IsComplete && w.Rng.NextFloat() < 0.5f) home.DoorPoint(out ox, out oz);
            float a = w.Rng.Range(0f, (float)Math.PI * 2f), r = w.Rng.Range(3f, 9f + 8f * v.Curiosity);
            BeginWalk(w, v, WalkPurpose.Wander, ox + (float)Math.Cos(a) * r, oz + (float)Math.Sin(a) * r, EntityId.None);
        }

        /// <summary>Deterministic seat on a ring around the camp fire.</summary>
        public static void CampSpot(StorageEntity camp, Villager v, float radius, out float x, out float z)
        {
            double a = (v.Id.Value * 2.39996323) % (Math.PI * 2);
            x = camp.X + (float)Math.Cos(a) * radius;
            z = camp.Z + (float)Math.Sin(a) * radius;
        }

        private static void Approach(WorldState w, float tx, float tz, Villager v, float radius, out float x, out float z)
        {
            float dx = v.X - tx, dz = v.Z - tz;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-3f) { x = tx + radius; z = tz; return; }
            x = tx + dx / d * radius; z = tz + dz / d * radius;
        }

        private static void LeaveCurrentTask(WorldState w, Villager v)
        {
            var t = w.Tasks.Find(v.TaskId);
            if (t != null) w.Tasks.Leave(w, t, v);
            v.TaskId = EntityId.None;
        }

        /// <summary>Delivers whatever is carried: preferred drop (site) first, else nearest storage with room, else a ground pile.</summary>
        private bool StartDropOff(WorldState w, Villager v)
        {
            int region = w.Nav.RegionNear(v.X, v.Z, 4);
            var site = w.FindBuilding(v.DropId);
            if (site != null && !site.IsComplete && site.Site.Free > 0 && v.Carry.Get(ItemType.Wood) > 0)
            {
                WorkSpot(w, site, 0, out float sx, out float sz);
                if (site.Kind != BuildingKind.Bridge) { site.DoorPoint(out sx, out sz); }
                BeginWalk(w, v, WalkPurpose.DropOff, sx, sz, site.Id);
                return true;
            }
            var store = NearestStorageWithRoom(w, v, region, v.DropId);
            if (store != null)
            {
                Approach(w, store.X, store.Z, v, 2.0f, out float x, out float z);
                BeginWalk(w, v, WalkPurpose.DropOff, x, z, store.Id);
                return true;
            }
            // Storage full: leave a tidy pile next to the camp (never destroy items).
            var camp = w.FindStorage(v.CampId) ?? w.Camp;
            if (camp == null) return false;
            var pile = new ResourcePile { Id = w.Ids.Next(), X = v.X, Z = v.Z, Inventory = new Inventory(EntityId.None, 64) };
            pile.Inventory = new Inventory(pile.Id, 64);
            for (int i = 0; i < Inventory.ItemTypeCount; i++) InventorySystem.Transfer(v.Carry, pile.Inventory, (ItemType)i, v.Carry.Get((ItemType)i));
            w.Piles.Add(pile);
            w.Emit(SimEventType.Hint, v.Id, pile.Id, pile.X, pile.Z, "仓库已满，" + v.Name + " 把物资堆在了地上");
            return false;
        }

        private static StorageEntity NearestStorageWithRoom(WorldState w, Villager v, int region, EntityId prefer)
        {
            StorageEntity best = null; float bestD = float.MaxValue;
            foreach (var s in w.Storages)
            {
                if (s.Inventory.Free <= 0) continue;
                if (w.Nav.RegionNear(s.X, s.Z, 4) != region) continue;
                float d = (s.X - v.X) * (s.X - v.X) + (s.Z - v.Z) * (s.Z - v.Z);
                if (s.Id == prefer) d *= 0.6f;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>Break out of looping work (fishing/building/harvest) at an action boundary.</summary>
        private static bool ShouldBreak(WorldState w, Villager v)
        {
            var c = w.Config;
            return v.Thirst >= c.ThirstToDrink * 1.4f || v.Hunger >= c.HungerToEat * 1.4f || w.IsNight || v.Energy < 0.1f || v.Weak
                || (w.IsEvening && v.Diligence < 0.72f);
        }

        // ================================================================ movement

        public static void BeginWalk(WorldState w, Villager v, WalkPurpose purpose, float goalX, float goalZ, EntityId target)
        {
            v.State = VillagerState.Walking;
            v.Purpose = purpose;
            v.Action = purpose == WalkPurpose.Wander ? VillagerAction.Wander : VillagerAction.None;
            v.TargetId = target;
            v.GoalX = goalX; v.GoalZ = goalZ;
            v.Path.Clear(); v.PathIndex = 0;
            v.NeedsRepath = true;
        }

        private bool PlanPath(WorldState w, Villager v)
        {
            v.NeedsRepath = false;
            v.Path.Clear(); v.PathIndex = 0;
            var start = w.Nav.WorldToCell(v.X, v.Z);
            if (!w.Nav.IsPassable(start))
            {
                if (!w.Nav.FindNearestPassable(start, 20, out var safe)) return false;
                w.Nav.CellCenter(safe, out float sx, out float sz);
                float moved = (float)Math.Sqrt((sx - v.X) * (sx - v.X) + (sz - v.Z) * (sz - v.Z));
                v.X = sx; v.Z = sz;
                // Stepping off a doorstep / work spot that sits in a blocked nav cell is normal; only a real
                // displacement (terrain changed under the villager) is a reportable relocation.
                if (moved > w.Nav.CellSize * 2.5f)
                {
                    w.Stats.Relocations++;
                    w.Emit(SimEventType.VillagerRelocated, v.Id, EntityId.None, v.X, v.Z, v.Name + " 被安全移到附近空地");
                }
                start = safe;
            }
            int region = w.Nav.RegionOf(start);
            var goalCell = w.Nav.WorldToCell(v.GoalX, v.GoalZ);
            if (!w.Nav.FindNearestInRegion(goalCell, region, 4, out goalCell)) return false;
            var status = w.Paths.FindPath(start, goalCell, v.Path);
            if (status != PathStatus.Found) return false;
            w.Paths.SmoothPath(v.Path);
            if (v.Path.Count > 0) v.PathIndex = 1;
            return true;
        }

        private void OnPathFailed(WorldState w, Villager v)
        {
            var task = w.Tasks.Find(v.TaskId);
            w.Stats.PathFailures++;
            w.Emit(SimEventType.PathFailed, v.Id, task != null ? task.TargetId : v.TargetId, v.GoalX, v.GoalZ,
                v.Name + " 无法到达目的地（" + PurposeText(v.Purpose) + "，任务 " + (task != null ? task.Id.ToString() : "无") + "）");
            if (task != null) w.Tasks.Release(w, task, v, true, "通路阻断");
            v.State = VillagerState.Idle;
            v.Action = VillagerAction.Wait;
            v.Path.Clear();
            v.IdleTicksLeft = w.Config.SecondsToTicks(4f);
        }

        public static string PurposeText(WalkPurpose p)
        {
            switch (p)
            {
                case WalkPurpose.Task: return "前往工作";
                case WalkPurpose.DropOff: return "运送物资";
                case WalkPurpose.PickUp: return "领取物资";
                case WalkPurpose.Food: return "去吃饭";
                case WalkPurpose.Water: return "去喝水";
                case WalkPurpose.Bed: return "回家睡觉";
                case WalkPurpose.Social: return "去篝火边";
                case WalkPurpose.Wander: return "散步";
                case WalkPurpose.Arrive: return "迁入山谷";
                default: return "走路";
            }
        }

        /// <summary>Moves along waypoints, then straight to the goal point. Returns true on arrival.</summary>
        private bool StepAlongPath(WorldState w, Villager v)
        {
            var c = w.Config;
            var cell = w.Nav.WorldToCell(v.X, v.Z);
            float speed = c.WalkSpeed * (v.Weak ? 0.6f : 1f) * (v.Carry.IsEmpty ? 1f : 0.88f) * (0.9f + 0.2f * v.Diligence);
            speed /= Math.Max(0.5f, w.Nav.Cost(cell.X, cell.Z));
            float budget = speed * c.TickSeconds;
            float range = ArriveRange(v);

            while (budget > 0)
            {
                float tx, tz;
                bool finalLeg = v.PathIndex >= v.Path.Count;
                if (finalLeg)
                {
                    float gx = v.GoalX - v.X, gz = v.GoalZ - v.Z;
                    float gd = (float)Math.Sqrt(gx * gx + gz * gz);
                    if (gd <= range) return true;
                    float k = (gd - range * 0.8f) / gd;
                    tx = v.X + gx * k; tz = v.Z + gz * k;
                }
                else
                {
                    w.Nav.CellCenter(v.Path[v.PathIndex], out tx, out tz);
                }

                float dx = tx - v.X, dz = tz - v.Z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d > 1e-4f) v.FacingDegrees = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                if (d <= budget)
                {
                    v.X = tx; v.Z = tz; budget -= d;
                    if (finalLeg) { DepositWear(w, v); return true; }
                    v.PathIndex++;
                }
                else
                {
                    v.X += dx / d * budget; v.Z += dz / d * budget;
                    budget = 0;
                }
            }
            DepositWear(w, v);
            return false;
        }

        private static float ArriveRange(Villager v)
        {
            switch (v.Purpose)
            {
                case WalkPurpose.Task: return v.TargetId.IsValid && v.Action != VillagerAction.Wander ? 1.5f : 0.6f;
                case WalkPurpose.Wander: return 1.0f;
                default: return 0.7f;
            }
        }

        private static void DepositWear(WorldState w, Villager v)
        {
            var cell = w.Nav.WorldToCell(v.X, v.Z);
            int idx = cell.Z * w.Nav.Width + cell.X;
            if (idx == v.LastCellIndex) return;
            v.LastCellIndex = idx;
            w.Nav.AddWear(cell.X, cell.Z, w.Config.RoadWearPerStep * (v.Carry.IsEmpty ? 1f : 1.3f));
        }

        // ================================================================ arrival

        private void OnArrived(WorldState w, Villager v)
        {
            v.Path.Clear(); v.PathIndex = 0;
            v.ArrivedTick = w.Tick;
            var c = w.Config;
            var task = w.Tasks.Find(v.TaskId);
            switch (v.Purpose)
            {
                case WalkPurpose.Task:
                    if (task == null || !task.Assignees.Contains(v.Id)) { Idle(v); return; }
                    StartWorkAt(w, v, task);
                    return;
                case WalkPurpose.PickUp:
                    StartAction(w, v, VillagerAction.PickUp, c.DeliverSeconds);
                    return;
                case WalkPurpose.DropOff:
                    StartAction(w, v, VillagerAction.DropOff, c.DeliverSeconds);
                    return;
                case WalkPurpose.Food:
                    StartAction(w, v, VillagerAction.Eat, c.EatSeconds);
                    return;
                case WalkPurpose.Water:
                    StartAction(w, v, VillagerAction.Drink, c.DrinkSeconds);
                    return;
                case WalkPurpose.Bed:
                {
                    var home = w.FindBuilding(v.TargetId);
                    BeginSleep(w, v, home != null && home.IsComplete && home.Kind == BuildingKind.House);
                    return;
                }
                case WalkPurpose.Social:
                {
                    var camp = w.FindStorage(v.TargetId) ?? w.Camp;
                    v.State = VillagerState.Idle;
                    v.Action = VillagerAction.Sit;
                    if (camp != null) v.FacingDegrees = (float)(Math.Atan2(camp.X - v.X, camp.Z - v.Z) * 180.0 / Math.PI);
                    v.IdleTicksLeft = c.SecondsToTicks(12f + 14f * v.Sociability);
                    return;
                }
                case WalkPurpose.Arrive:
                    w.Emit(SimEventType.VillagerMovedIn, EventImportance.Info, v.Id, v.CampId, v.X, v.Z, v.Name + " 来到了营地");
                    Idle(v);
                    return;
                default:
                    v.State = VillagerState.Idle;
                    v.Action = VillagerAction.Wait;
                    v.IdleTicksLeft = c.SecondsToTicks(2f + 4f * (1f - v.Diligence));
                    return;
            }
        }

        private static void Idle(Villager v)
        {
            v.State = VillagerState.Idle;
            v.Action = VillagerAction.None;
            v.IdleTicksLeft = 0;
        }

        private void StartWorkAt(WorldState w, Villager v, WorkTask t)
        {
            var c = w.Config;
            float eff = WorkEfficiency(v);
            switch (t.Type)
            {
                case TaskType.ChopTree:
                {
                    var tree = w.FindTree(t.TargetId);
                    if (tree == null || !tree.Harvestable || tree.ReservedBy != v.Id) { w.Tasks.Complete(w, t); Idle(v); return; }
                    Face(v, tree.X, tree.Z);
                    StartAction(w, v, VillagerAction.Chop, c.ChopSecondsPerLoad / eff);
                    return;
                }
                case TaskType.Build:
                {
                    var b = w.FindBuilding(t.TargetId);
                    if (b == null) { w.Tasks.Complete(w, t); Idle(v); return; }
                    Face(v, b.X, b.Z);
                    StartAction(w, v, VillagerAction.Build, c.BuildSecondsPerStep);
                    return;
                }
                case TaskType.Sow:
                    StartAction(w, v, VillagerAction.Sow, c.SowSeconds / eff);
                    return;
                case TaskType.Harvest:
                    StartAction(w, v, VillagerAction.Harvest, c.HarvestSecondsPerLoad / eff);
                    return;
                case TaskType.Fish:
                {
                    var b = w.FindBuilding(t.TargetId);
                    if (b != null) FaceAwayFrom(v, b.X, b.Z);
                    StartAction(w, v, VillagerAction.Fish, c.FishSecondsPerFood / eff);
                    return;
                }
                case TaskType.FetchWater:
                    StartAction(w, v, VillagerAction.DrawWater, c.DrawWaterSeconds);
                    return;
                default:
                    Idle(v);
                    return;
            }
        }

        private static float WorkEfficiency(Villager v)
        {
            float e = 0.75f + 0.5f * v.Diligence;
            if (v.Energy < 0.25f) e *= 0.8f;
            if (v.Hunger >= 2f || v.Thirst >= 2f) e *= 0.75f;
            return e;
        }

        private static void Face(Villager v, float x, float z)
        {
            float dx = x - v.X, dz = z - v.Z;
            if (dx * dx + dz * dz > 1e-4f) v.FacingDegrees = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
        }

        private static void FaceAwayFrom(Villager v, float x, float z)
        {
            float dx = v.X - x, dz = v.Z - z;
            if (dx * dx + dz * dz > 1e-4f) v.FacingDegrees = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
        }

        private static void StartAction(WorldState w, Villager v, VillagerAction a, float seconds)
        {
            v.State = VillagerState.Working;
            v.Action = a;
            v.ActionTicksLeft = Math.Max(1, w.Config.SecondsToTicks(seconds));
        }

        // ================================================================ action completion

        private void FinishAction(WorldState w, Villager v)
        {
            var c = w.Config;
            var task = w.Tasks.Find(v.TaskId);
            if (task != null && !task.Assignees.Contains(v.Id)) task = null;
            switch (v.Action)
            {
                case VillagerAction.Chop: FinishChop(w, v, task); break;
                case VillagerAction.Build: FinishBuild(w, v, task); break;
                case VillagerAction.Sow:
                {
                    var b = task != null ? w.FindBuilding(task.TargetId) : null;
                    if (b != null && b.Crop == CropState.Fallow)
                    {
                        b.Crop = CropState.Growing;
                        b.CropGrowth = 0f;
                        w.Emit(SimEventType.FieldSown, EventImportance.Info, v.Id, b.Id, b.X, b.Z, v.Name + " 在农田里播下了种子");
                    }
                    if (task != null) w.Tasks.Complete(w, task);
                    Idle(v);
                    break;
                }
                case VillagerAction.Harvest: FinishHarvest(w, v, task); break;
                case VillagerAction.Fish: FinishFish(w, v, task); break;
                case VillagerAction.DrawWater:
                {
                    int amount = v.Carry.Free;
                    if (amount > 0)
                    {
                        // River water is a source: it enters the economy here and is recorded as created.
                        InventorySystem.Seed(v.Carry, ItemType.Water, amount, w.Stats);
                        w.Stats.WaterFetched += amount;
                    }
                    if (!StartDropOff(w, v)) Idle(v);
                    break;
                }
                case VillagerAction.PickUp: FinishPickUp(w, v, task); break;
                case VillagerAction.DropOff: FinishDropOff(w, v, task); break;
                case VillagerAction.Eat:
                {
                    var s = w.FindStorage(v.TargetId);
                    int want = Math.Max(1, Math.Min(2, (int)Math.Floor(v.Hunger + 0.25f)));
                    int ate = 0;
                    while (s != null && ate < want && InventorySystem.Consume(s.Inventory, ItemType.Food, 1, w.Stats)) ate++;
                    if (ate > 0)
                    {
                        v.Hunger = Math.Max(0f, v.Hunger - ate);
                        w.Emit(SimEventType.VillagerAte, v.Id, s.Id, s.X, s.Z, v.Name + " 吃了 " + ate + " 份食物");
                    }
                    Idle(v);
                    break;
                }
                case VillagerAction.Drink:
                {
                    var s = w.FindStorage(v.TargetId);
                    if (s != null)
                    {
                        int want = Math.Max(1, Math.Min(2, (int)Math.Floor(v.Thirst + 0.25f)));
                        int drank = 0;
                        while (drank < want && InventorySystem.Consume(s.Inventory, ItemType.Water, 1, w.Stats)) drank++;
                        v.Thirst = Math.Max(0f, v.Thirst - drank);
                    }
                    else
                    {
                        v.Thirst = 0f; // drinking straight from the river
                    }
                    Idle(v);
                    break;
                }
                default:
                    Idle(v);
                    break;
            }
        }

        private void FinishChop(WorldState w, Villager v, WorkTask task)
        {
            var tree = task != null ? w.FindTree(task.TargetId) : null;
            if (tree != null && tree.ReservedBy == v.Id)
            {
                int moved = InventorySystem.Transfer(tree.Stock, v.Carry, ItemType.Wood, v.Carry.Free);
                if (moved > 0 && tree.IsStump)
                {
                    tree.FelledTick = w.Tick;
                    w.Stats.TreesFelled++;
                    w.Emit(SimEventType.TreeFelled, EventImportance.Info, v.Id, tree.Id, tree.X, tree.Z, v.Name + " 砍倒了一棵树");
                }
            }
            if (!v.Carry.IsEmpty)
            {
                v.DropId = task != null ? task.DestId : EntityId.None;
                if (!StartDropOff(w, v)) Idle(v);
            }
            else
            {
                if (task != null) w.Tasks.Complete(w, task);
                Idle(v);
            }
        }

        private void FinishBuild(WorldState w, Villager v, WorkTask task)
        {
            var b = task != null ? w.FindBuilding(task.TargetId) : null;
            if (b == null) { Idle(v); return; }
            var r = ConstructionSystem.AddWork(w, b, w.Config.BuildSecondsPerStep * WorkEfficiency(v), v);
            if (r == BuildResult.Completed)
            {
                w.Tasks.Complete(w, task);
                Idle(v);
                return;
            }
            if (r == BuildResult.WaitingMaterials || ShouldBreak(w, v))
            {
                w.Tasks.Leave(w, task, v);
                Idle(v);
                v.IdleTicksLeft = w.Config.SecondsToTicks(1f);
                return;
            }
            StartAction(w, v, VillagerAction.Build, w.Config.BuildSecondsPerStep);
        }

        private void FinishHarvest(WorldState w, Villager v, WorkTask task)
        {
            var b = task != null ? w.FindBuilding(task.TargetId) : null;
            if (b != null && b.Crop == CropState.Ripe && b.CropFood > 0)
            {
                int take = Math.Min(v.Carry.Free, b.CropFood);
                if (take > 0)
                {
                    b.CropFood -= take;
                    InventorySystem.Seed(v.Carry, ItemType.Food, take, w.Stats);
                    w.Stats.FoodHarvested += take;
                }
                if (b.CropFood <= 0)
                {
                    b.Crop = CropState.Fallow;
                    b.CropGrowth = 0f;
                    b.Harvests++;
                    w.Emit(SimEventType.FieldHarvested, v.Id, b.Id, b.X, b.Z, v.Name + " 收完了一片农田的庄稼");
                    w.ReachMilestone(Milestones.FirstHarvest, v.Id, b.Id, b.X, b.Z, "村庄迎来了第一次收获");
                    w.Tasks.Complete(w, task);
                }
            }
            else if (task != null) w.Tasks.Complete(w, task);
            v.DropId = EntityId.None;
            if (!v.Carry.IsEmpty) { if (!StartDropOff(w, v)) Idle(v); }
            else Idle(v);
        }

        private void FinishFish(WorldState w, Villager v, WorkTask task)
        {
            var b = task != null ? w.FindBuilding(task.TargetId) : null;
            bool riverOk = b != null && w.Water.DistanceToWater(b.SpotX, b.SpotZ) < 5f;
            if (b != null && riverOk && v.Carry.Free > 0)
            {
                InventorySystem.Seed(v.Carry, ItemType.Food, 1, w.Stats);
                w.Stats.FishCaught++;
                if (w.Stats.FishCaught == 1) w.ReachMilestone(Milestones.FirstFish, v.Id, b.Id, b.X, b.Z, v.Name + " 钓到了村庄的第一条鱼");
            }
            if (!riverOk && task != null) { w.Tasks.Complete(w, task); task = null; }
            bool full = v.Carry.Free == 0;
            if (task != null && !full && !ShouldBreak(w, v))
            {
                StartAction(w, v, VillagerAction.Fish, w.Config.FishSecondsPerFood / WorkEfficiency(v));
                return;
            }
            if (task != null && (ShouldBreak(w, v))) { w.Tasks.Leave(w, task, v); }
            v.DropId = EntityId.None;
            if (!v.Carry.IsEmpty) { if (!StartDropOff(w, v)) Idle(v); }
            else Idle(v);
        }

        private void FinishPickUp(WorldState w, Villager v, WorkTask task)
        {
            if (task == null) { Idle(v); return; }
            if (task.Type == TaskType.Supply)
            {
                var s = w.FindStorage(v.TargetId);
                int want = Math.Min(task.Amount, v.Carry.Free);
                int moved = s != null ? InventorySystem.Transfer(s.Inventory, v.Carry, ItemType.Wood, want) : 0;
                if (moved <= 0) { w.Tasks.Release(w, task, v, false, "仓库木材已被取走"); Idle(v); return; }
                v.DropId = task.DestId;
                if (!StartDropOff(w, v)) Idle(v);
                return;
            }
            if (task.Type == TaskType.HaulPile)
            {
                var p = w.FindPile(task.TargetId);
                if (p != null)
                {
                    for (int i = 0; i < Inventory.ItemTypeCount && v.Carry.Free > 0; i++)
                        InventorySystem.Transfer(p.Inventory, v.Carry, (ItemType)i, v.Carry.Free);
                    if (p.Inventory.IsEmpty) { w.Piles.Remove(p); w.Tasks.Complete(w, task); }
                }
                else w.Tasks.Complete(w, task);
                v.DropId = EntityId.None;
                if (!v.Carry.IsEmpty) { if (!StartDropOff(w, v)) Idle(v); }
                else Idle(v);
                return;
            }
            Idle(v);
        }

        private void FinishDropOff(WorldState w, Villager v, WorkTask task)
        {
            var inv = w.FindInventory(v.TargetId);
            var site = w.FindBuilding(v.TargetId);
            var storage = w.FindStorage(v.TargetId);
            int delivered = 0;
            if (inv != null && (site == null || !site.IsComplete))
            {
                for (int i = 0; i < Inventory.ItemTypeCount; i++)
                {
                    var type = (ItemType)i;
                    if (site != null && type != ItemType.Wood) continue;
                    delivered += InventorySystem.Transfer(v.Carry, inv, type, v.Carry.Get(type));
                }
            }
            if (delivered > 0)
            {
                if (storage != null)
                {
                    int wood = 0; // approximate for the story feed
                    if (task != null && task.Type == TaskType.ChopTree) wood = delivered;
                    if (wood > 0)
                    {
                        w.Stats.WoodDelivered += wood;
                        w.Emit(SimEventType.WoodDelivered, EventImportance.Info, v.Id, storage.Id, storage.X, storage.Z, v.Name + " 把 " + wood + " 木材运进了仓库");
                        w.ReachMilestone(Milestones.FirstWoodDelivered, v.Id, storage.Id, storage.X, storage.Z, v.Name + " 完成了村庄的第一次木材入库");
                    }
                    if (task != null && task.Type == TaskType.FetchWater)
                        w.Emit(SimEventType.WaterFetched, EventImportance.Debug, v.Id, storage.Id, storage.X, storage.Z, v.Name + " 打来了 " + delivered + " 份水");
                }
                if (site != null)
                {
                    w.Stats.WoodDelivered += delivered;
                    w.Emit(SimEventType.WoodDelivered, EventImportance.Info, v.Id, site.Id, site.X, site.Z,
                        v.Name + " 把 " + delivered + " 木材搬到了" + WorldState.KindName(site.Kind) + "工地");
                }
                if (task != null && (task.Type == TaskType.Supply || task.Type == TaskType.ChopTree) && task.DestId == v.TargetId)
                    task.Amount = Math.Max(0, task.Amount - delivered);
            }

            // Anything left (site full, cancelled, storage full) goes to the nearest storage next.
            if (!v.Carry.IsEmpty)
            {
                v.DropId = EntityId.None;
                if (v.TargetId.IsValid && storage != null && storage.Inventory.Free == 0) v.DropId = EntityId.None;
                if (!StartDropOff(w, v)) Idle(v);
                return;
            }

            if (task == null) { Idle(v); return; }
            switch (task.Type)
            {
                case TaskType.ChopTree:
                {
                    var tree = w.FindTree(task.TargetId);
                    bool siteDone = task.DestId.IsValid && w.FindBuilding(task.DestId) != null && task.Amount <= 0;
                    if (tree == null || !tree.Harvestable || siteDone || ShouldBreak(w, v)) { if (tree == null || !tree.Harvestable || siteDone) w.Tasks.Complete(w, task); else w.Tasks.Leave(w, task, v); Idle(v); }
                    else BeginWalk(w, v, WalkPurpose.Task, tree.X, tree.Z, tree.Id);
                    break;
                }
                case TaskType.Harvest:
                {
                    var b = w.FindBuilding(task.TargetId);
                    if (b == null || b.Crop != CropState.Ripe || b.CropFood <= 0 || ShouldBreak(w, v)) { if (b == null || b.CropFood <= 0) w.Tasks.Complete(w, task); else w.Tasks.Leave(w, task, v); Idle(v); }
                    else ContinueTask(w, v, task);
                    break;
                }
                case TaskType.Fish:
                    if (ShouldBreak(w, v)) { w.Tasks.Leave(w, task, v); Idle(v); }
                    else if (!ContinueTask(w, v, task)) Idle(v);
                    break;
                case TaskType.HaulPile:
                {
                    var p = w.FindPile(task.TargetId);
                    if (p == null || p.Inventory.IsEmpty) { w.Tasks.Complete(w, task); Idle(v); }
                    else BeginWalk(w, v, WalkPurpose.PickUp, p.X, p.Z, p.Id);
                    break;
                }
                default:
                    w.Tasks.Complete(w, task);
                    Idle(v);
                    break;
            }
        }

        // ================================================================ sleep

        private static void BeginSleep(WorldState w, Villager v, bool inside)
        {
            v.State = VillagerState.Sleeping;
            v.Action = VillagerAction.Sleep;
            v.InsideHome = inside;
            w.Emit(SimEventType.VillagerSlept, v.Id, v.HomeId, v.X, v.Z, v.Name + (inside ? " 回家睡觉了" : " 在篝火旁睡下了"));
        }

        private static void TickSleep(WorldState w, Villager v)
        {
            var c = w.Config;
            v.Energy = Math.Min(1f, v.Energy + c.TickSeconds / c.GameHoursToSeconds(c.SleepHoursToRest));
            float wake = c.WakeHour + (1f - v.Diligence) * 1.2f;
            bool morning = !w.IsNight && w.Hour >= wake && w.Hour < c.EveningSocialHour;
            bool rested = v.Energy >= 0.95f;
            bool urgent = v.Thirst >= 2.2f || v.Hunger >= 2.6f;
            bool napDone = !w.IsNight && !w.IsEvening && v.Energy >= 0.6f && w.Hour >= c.WakeHour && w.Hour < c.EveningSocialHour; // naps by exhausted villagers
            if ((morning && (rested || w.Hour >= wake + 1.5f)) || urgent || (napDone && w.Hour > wake + 2f))
            {
                v.State = VillagerState.Idle;
                v.Action = VillagerAction.None;
                if (v.InsideHome)
                {
                    var home = w.FindBuilding(v.HomeId);
                    if (home != null) { home.DoorPoint(out v.X, out v.Z); v.FacingDegrees = home.Yaw; }
                }
                v.InsideHome = false;
                v.IdleTicksLeft = c.SecondsToTicks(1f + 3f * (1f - v.Diligence));
                w.Emit(SimEventType.VillagerWoke, v.Id, EntityId.None, v.X, v.Z, v.Name + " 醒了");
            }
        }

        // ================================================================ interruption

        /// <summary>Called when a task target vanishes: stop the current step, keep carried items (they get delivered next).</summary>
        public static void Interrupt(WorldState w, Villager v, string reason)
        {
            if (v.State == VillagerState.Sleeping) return;
            v.State = VillagerState.Idle;
            v.Action = VillagerAction.None;
            v.Path.Clear();
            v.IdleTicksLeft = w.Config.SecondsToTicks(0.5f);
            if (v.DropId.IsValid)
            {
                var b = w.FindBuilding(v.DropId);
                if (b == null || b.IsComplete) v.DropId = EntityId.None;
            }
        }
    }
}
