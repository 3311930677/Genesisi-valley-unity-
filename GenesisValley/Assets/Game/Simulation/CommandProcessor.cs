using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>Validates and applies player commands (GDD §10). The host is the only writer of WorldState.</summary>
    public sealed class CommandProcessor
    {
        private readonly WorldState _w;
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly Dictionary<long, float> _strokeTotals = new Dictionary<long, float>();
        private float[] _riverMask;
        private int _riverMaskVersion = -1;

        public CommandProcessor(WorldState w) { _w = w; }

        public CommandResult Apply(GameCommand cmd)
        {
            long key = ((long)cmd.PlayerId << 32) | (uint)cmd.CommandId;
            if (_seen.Contains(key)) return CommandResult.Reject("重复命令已忽略");

            CommandResult r;
            switch (cmd)
            {
                case TerrainBrushCommand b: r = ApplyBrush(b); break;
                case ForestCommand f: r = ApplyForest(f); break;
                case RiverCommand rv: r = ApplyRiver(rv); break;
                case SummonCommand s: r = ApplySummon(s); break;
                case BridgeCommand br: r = ApplyBridge(br); break;
                case UndoCommand u: r = ApplyUndo(u); break;
                case EndTransactionCommand e: r = ApplyEnd(e); break;
                default: r = CommandResult.Reject("未知命令"); break;
            }

            _seen.Add(key);
            if (r.Accepted)
            {
                cmd.AuthoritativeSequence = ++_w.LastAuthoritativeSequence;
                cmd.AppliedTick = _w.Tick;
            }
            else
            {
                _w.Emit(cmd is UndoCommand ? SimEventType.UndoRejected : SimEventType.CommandRejected, EntityId.None, EntityId.None, 0, 0, r.Reason);
            }
            return r;
        }

        // ================================================================ terrain

        private CommandResult ApplyBrush(TerrainBrushCommand b)
        {
            var c = _w.Config;
            if (!InWorld(b.CenterX, b.CenterZ)) return CommandResult.Reject("笔刷位置超出山谷边界");
            if (b.Radius < c.BrushMinRadius - 1e-3f || b.Radius > c.BrushMaxRadius + 1e-3f)
                return CommandResult.Reject("笔刷半径超出范围");
            if (!(b.Strength > 0) || b.Strength > c.BrushMaxDeltaPerCommand + 1e-4f)
                return CommandResult.Reject("笔刷强度超出单次限制");

            long strokeKey = ((long)b.PlayerId << 32) | (uint)b.TransactionId;
            _strokeTotals.TryGetValue(strokeKey, out float total);
            if (b.Mode != BrushMode.Flatten && total + b.Strength > c.BrushMaxDeltaPerStroke + 1e-4f)
                return CommandResult.Reject("单笔高度变化已达上限，松开后再继续");
            _strokeTotals[strokeKey] = total + b.Strength;

            var rec = _w.Undo.Open(b.PlayerId, b.TransactionId, UndoKind.Terrain, _w.Tick,
                b.Mode == BrushMode.Raise ? "抬升地形" : b.Mode == BrushMode.Lower ? "降低地形" : "平整地形", _w.Height);
            IntRect dirty;
            if (b.Mode == BrushMode.Flatten)
                dirty = _w.Height.ApplyFlatten(b.CenterX, b.CenterZ, b.Radius, b.TargetHeight, b.Strength, ProtectionMask, rec.Heights);
            else
                dirty = _w.Height.ApplyBrush(b.CenterX, b.CenterZ, b.Radius, b.Mode == BrushMode.Raise ? b.Strength : -b.Strength, ProtectionMask, rec.Heights);
            if (dirty.IsEmpty) return CommandResult.Ok();

            _w.PresentationHeightDirty = _w.PresentationHeightDirty.Union(dirty);
            OnTerrainChanged(dirty);
            return CommandResult.Ok();
        }

        private CommandResult ApplyEnd(EndTransactionCommand e)
        {
            foreach (var r in _w.Undo.Records)
            {
                if (r.Finalized || r.PlayerId != e.PlayerId || r.TransactionId != e.TransactionId) continue;
                _w.Undo.Finalize(r, _w.Height);
                if (r.Kind == UndoKind.Terrain && r.Heights.Before.Count > 0)
                    _w.Emit(SimEventType.TerrainEdited, EventImportance.Info, EntityId.None, EntityId.None, 0, 0, r.Label + "完成");
                if (r.Kind == UndoKind.Forest && r.PlantedTrees.Count > 0)
                {
                    _w.Tasks.Prune();
                    _w.Emit(SimEventType.ForestPlanted, EntityId.None, EntityId.None, 0, 0, "种下了 " + r.PlantedTrees.Count + " 棵树苗，它们会在一天左右长大");
                    _w.ReachMilestone(Milestones.FirstForest, EntityId.None, EntityId.None, 0, 0, "山谷里长出了第一片亲手种下的森林");
                }
            }
            long strokeKey = ((long)e.PlayerId << 32) | (uint)e.TransactionId;
            _strokeTotals.Remove(strokeKey);
            return CommandResult.Ok();
        }

        /// <summary>
        /// Protection band (GDD §4/§11.1): no height change on the camp, buildings, bridges or river channels,
        /// with a soft blend outside them. Shown in the tool preview.
        /// </summary>
        public float ProtectionMask(float x, float z)
        {
            var c = _w.Config;
            float m = 1f;
            foreach (var s in _w.Storages)
            {
                if (s.Kind != StorageKind.Camp) continue;
                float d = (float)Math.Sqrt((x - s.X) * (x - s.X) + (z - s.Z) * (z - s.Z));
                float t = (d - c.CampProtectionRadius) / c.CampProtectionBlend;
                if (t <= 0) return 0f;
                if (t < 1) m = Math.Min(m, t * t * (3 - 2 * t));
            }
            foreach (var b in _w.Buildings)
            {
                float reach = b.BoundingRadius + c.BuildingProtectionBlend + 1.5f;
                if (Math.Abs(b.X - x) > reach || Math.Abs(b.Z - z) > reach) continue;
                float d = b.Kind == BuildingKind.Bridge
                    ? BridgePlanner.SegmentDistance(x, z, x, z, b.AX, b.AZ, b.BX, b.BZ) - 1.2f
                    : b.DistanceOutside(x, z);
                float t = (d - 1f) / c.BuildingProtectionBlend;
                if (t <= 0) return 0f;
                if (t < 1) m = Math.Min(m, t * t * (3 - 2 * t));
            }
            if (_w.Rivers.Count > 0)
            {
                EnsureRiverMask();
                var h = _w.Height;
                int ix = (int)Math.Round(x / h.Spacing), iz = (int)Math.Round(z / h.Spacing);
                if (ix >= 0 && iz >= 0 && ix < h.Resolution && iz < h.Resolution) m = Math.Min(m, _riverMask[iz * h.Resolution + ix]);
            }
            return m;
        }

        private void EnsureRiverMask()
        {
            if (_riverMaskVersion == _w.RiverVersion && _riverMask != null) return;
            _riverMaskVersion = _w.RiverVersion;
            var h = _w.Height;
            if (_riverMask == null) _riverMask = new float[h.Resolution * h.Resolution];
            for (int i = 0; i < _riverMask.Length; i++) _riverMask[i] = 1f;
            foreach (var r in _w.Rivers)
            {
                var b = RiverBuilder.Bounds(h, r);
                for (int z = b.ZMin; z <= b.ZMax; z++)
                for (int x = b.XMin; x <= b.XMax; x++)
                {
                    if (!RiverBuilder.Nearest(r, x * h.Spacing, z * h.Spacing, out float d, out _, out float hw)) continue;
                    float t = (d - hw - RiverBuilder.BankWidth * 0.6f) / 4f;
                    float m = t <= 0 ? 0 : (t >= 1 ? 1 : t * t * (3 - 2 * t));
                    int i = z * h.Resolution + x;
                    if (m < _riverMask[i]) _riverMask[i] = m;
                }
            }
        }

        /// <summary>Terrain transaction aftermath (GDD §11.1): navigation update, then villagers re-plan or are moved to safety.</summary>
        private void OnTerrainChanged(IntRect dirtySamples)
        {
            var cells = _w.Nav.SampleRectToCells(_w.Height, dirtySamples);
            var changed = _w.Nav.Rebuild(_w.Height, cells);
            if (changed.IsEmpty) return;
            InvalidatePaths(changed);
        }

        private void InvalidatePaths(IntRect changed)
        {
            var grown = changed.Expand(1);
            foreach (var v in _w.Villagers)
            {
                if (v.InsideHome) continue;
                var cell = _w.Nav.WorldToCell(v.X, v.Z);
                if (!_w.Nav.IsPassable(cell))
                {
                    if (_w.Nav.FindNearestPassable(cell, 20, out var safe))
                    {
                        _w.Nav.CellCenter(safe, out v.X, out v.Z);
                        _w.Stats.Relocations++;
                        _w.Emit(SimEventType.VillagerRelocated, v.Id, EntityId.None, v.X, v.Z, v.Name + " 因地形变化被安全移到附近空地");
                    }
                    if (v.IsMoving) v.NeedsRepath = true;
                    continue;
                }
                if (!v.IsMoving) continue;
                for (int i = Math.Max(0, v.PathIndex - 1); i < v.Path.Count; i++)
                {
                    if (grown.Contains(v.Path[i].X, v.Path[i].Z)) { v.NeedsRepath = true; break; }
                }
                if (!v.NeedsRepath && v.Path.Count > 0)
                {
                    // smoothed paths skip cells: check straight legs too
                    for (int i = Math.Max(1, v.PathIndex); i < v.Path.Count; i++)
                    {
                        var a = v.Path[i - 1]; var b = v.Path[i];
                        int minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minZ = Math.Min(a.Z, b.Z), maxZ = Math.Max(a.Z, b.Z);
                        if (maxX >= grown.XMin && minX <= grown.XMax && maxZ >= grown.ZMin && minZ <= grown.ZMax) { v.NeedsRepath = true; break; }
                    }
                }
            }
        }

        // ================================================================ forest

        private CommandResult ApplyForest(ForestCommand f)
        {
            var c = _w.Config;
            if (!InWorld(f.CenterX, f.CenterZ)) return CommandResult.Reject("森林笔刷超出山谷边界");
            float radius = Math.Max(4f, Math.Min(f.Radius <= 0 ? c.ForestDefaultRadius : f.Radius, 25f));
            var rec = _w.Undo.Open(f.PlayerId, f.TransactionId, UndoKind.Forest, _w.Tick, "种植森林", _w.Height);

            // Fixed-seed sampling per command (GDD §4): replaying the same command plants the same trees.
            var rng = new DeterministicRandom(_w.Seed ^ ((ulong)(uint)f.CommandId * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)f.PlayerId << 48));
            float spacing = c.ForestMinSpacing;
            float area = (float)Math.PI * radius * radius;
            int maxInCircle = (int)(area / (Math.PI * spacing * spacing * 1.6f));
            int existing = 0;
            foreach (var t in _w.Trees)
                if ((t.X - f.CenterX) * (t.X - f.CenterX) + (t.Z - f.CenterZ) * (t.Z - f.CenterZ) < radius * radius && !t.IsStump) existing++;

            // Replanting clears old stumps under the brush.
            for (int i = _w.Trees.Count - 1; i >= 0; i--)
            {
                var t = _w.Trees[i];
                if (!t.IsStump || t.ReservedBy.IsValid || _w.Tasks.HasActiveTaskFor(t.Id)) continue;
                if ((t.X - f.CenterX) * (t.X - f.CenterX) + (t.Z - f.CenterZ) * (t.Z - f.CenterZ) > radius * radius) continue;
                rec.RemovedTrees.Add(Snapshot(t));
                _w.RemoveTree(t);
            }

            int planted = 0;
            int attempts = c.ForestMaxPerStamp * 6;
            for (int a = 0; a < attempts && planted < c.ForestMaxPerStamp && existing + planted < maxInCircle; a++)
            {
                float ang = rng.Range(0f, (float)Math.PI * 2f);
                float r = radius * (float)Math.Sqrt(rng.NextFloat());
                float x = f.CenterX + (float)Math.Cos(ang) * r, z = f.CenterZ + (float)Math.Sin(ang) * r;
                float roll = rng.NextFloat();
                if (!ForestPlanter.IsPlantable(_w, x, z)) continue;
                bool ok = true;
                foreach (var t in _w.Trees)
                {
                    float dx = t.X - x, dz = t.Z - z;
                    if (Math.Abs(dx) < spacing && Math.Abs(dz) < spacing && dx * dx + dz * dz < spacing * spacing) { ok = false; break; }
                }
                if (!ok) continue;
                var species = f.Species >= 0 && f.Species <= 2 ? (TreeSpecies)f.Species : ForestPlanter.ChooseSpecies(_w, x, z, roll);
                var tree = ForestPlanter.CreateTree(_w, x, z, species, 0.12f + 0.2f * rng.NextFloat(), f.TransactionId);
                _w.AddTree(tree);
                rec.PlantedTrees.Add(tree.Id);
                planted++;
            }
            if (planted == 0 && rec.RemovedTrees.Count == 0)
                return CommandResult.Reject(existing >= maxInCircle ? "这里的树已经足够密了" : "这里不适合种树（太陡、靠水、道路或建筑）");
            return CommandResult.Ok();
        }

        private static TreeSnapshot Snapshot(TreeEntity t) => new TreeSnapshot
        {
            Id = t.Id.Value, Species = t.Species, Variant = t.Variant, X = t.X, Z = t.Z, Rotation = t.RotationDegrees, Scale = t.Scale,
            Growth = t.Growth, Wood = t.Stock.Get(ItemType.Wood), PlantedBy = t.PlantedByTransaction, FelledTick = t.FelledTick,
        };

        private void Restore(TreeSnapshot s)
        {
            var t = new TreeEntity
            {
                Id = new EntityId(s.Id), Species = s.Species, Variant = s.Variant, X = s.X, Z = s.Z, RotationDegrees = s.Rotation,
                Scale = s.Scale, Growth = s.Growth, PlantedByTransaction = s.PlantedBy, FelledTick = s.FelledTick,
            };
            t.Stock = new Inventory(t.Id, _w.Config.WoodPerTree);
            if (s.Wood > 0) InventorySystem.Seed(t.Stock, ItemType.Wood, s.Wood, _w.Stats);
            _w.AddTree(t);
        }

        // ================================================================ river

        public RiverPlan PreviewRiver(IList<float> points, float width)
        {
            var c = _w.Config;
            width = Math.Max(c.RiverMinWidth, Math.Min(c.RiverMaxWidth, width <= 0 ? c.RiverDefaultWidth : width));
            return RiverBuilder.Plan(_w.Height, points, width, c.RiverDepth, c.RiverSampleSpacing, c.RiverMaxCut, c.RiverMaxLength, RiverObstacle);
        }

        private RiverIssue RiverObstacle(float x, float z, float clearance)
        {
            var camp = _w.Camp;
            if (camp != null && (camp.X - x) * (camp.X - x) + (camp.Z - z) * (camp.Z - z) < (clearance + 9f) * (clearance + 9f)) return RiverIssue.CrossesCamp;
            foreach (var b in _w.Buildings)
            {
                if (!b.IsComplete) continue;
                if (Math.Abs(b.X - x) > b.BoundingRadius + clearance + 2 || Math.Abs(b.Z - z) > b.BoundingRadius + clearance + 2) continue;
                if (b.Kind == BuildingKind.Bridge) { if (BridgePlanner.SegmentDistance(x, z, x, z, b.AX, b.AZ, b.BX, b.BZ) < clearance) return RiverIssue.CrossesBuilding; continue; }
                if (b.DistanceOutside(x, z) < clearance) return RiverIssue.CrossesBuilding;
            }
            return RiverIssue.None;
        }

        private CommandResult ApplyRiver(RiverCommand cmd)
        {
            var c = _w.Config;
            if (_w.Rivers.Count >= 8) return CommandResult.Reject("河流数量已达上限");
            var plan = PreviewRiver(cmd.Points, cmd.Width);
            if (!plan.Valid) return CommandResult.Reject(plan.Reason ?? "河道不合法");

            var river = new RiverData { Id = NextRiverId(), Width = plan.Width, Depth = plan.Depth, PondRadius = plan.PondRadius, Seed = cmd.CommandId };
            river.ControlPoints.AddRange(cmd.Points);
            river.Samples.AddRange(plan.Samples);
            var rec = _w.Undo.Open(cmd.PlayerId, cmd.TransactionId != 0 ? cmd.TransactionId : -cmd.CommandId, UndoKind.River, _w.Tick, "开挖河流", _w.Height);
            rec.RiverId = river.Id;
            AddRiver(river, rec, out int cancelled, out int flooded);
            _w.Undo.Finalize(rec, _w.Height);

            _w.Emit(SimEventType.RiverCreated, EntityId.None, EntityId.None, river.Samples[0].X, river.Samples[0].Z,
                "一条 " + river.Length.ToString("0") + " 米长的河流开始流淌，岸边的土地变得湿润" + (flooded > 0 ? "（" + flooded + " 棵树被河水淹没）" : ""));
            _w.ReachMilestone(Milestones.FirstRiver, EntityId.None, EntityId.None, river.Samples[0].X, river.Samples[0].Z, "山谷里有了第一条亲手开挖的河");
            return CommandResult.Ok();
        }

        private int NextRiverId()
        {
            int id = 1;
            foreach (var r in _w.Rivers) id = Math.Max(id, r.Id + 1);
            return id;
        }

        /// <summary>Shared by the command and the world generator: cancels sites in the channel, floods trees, carves, marks water.</summary>
        public void AddRiver(RiverData river, UndoRecord rec, out int cancelledSites, out int floodedTrees)
        {
            cancelledSites = 0; floodedTrees = 0;
            float hw = river.Width * 0.5f;
            for (int i = _w.Buildings.Count - 1; i >= 0; i--)
            {
                var b = _w.Buildings[i];
                if (b.IsComplete) continue;
                RiverBuilder.Nearest(river, b.X, b.Z, out float d, out _, out _);
                if (d < hw + RiverBuilder.BankWidth + b.BoundingRadius) { ConstructionSystem.CancelSite(_w, b, "河道穿过了工地"); cancelledSites++; }
            }
            for (int i = _w.Trees.Count - 1; i >= 0; i--)
            {
                var t = _w.Trees[i];
                RiverBuilder.Nearest(river, t.X, t.Z, out float d, out _, out float thw);
                if (d > thw + 1.2f) continue;
                _w.Tasks.CancelFor(_w, t.Id);
                if (rec != null) rec.RemovedTrees.Add(Snapshot(t));
                int wood = t.Stock.Get(ItemType.Wood);
                if (wood > 0) InventorySystem.Consume(t.Stock, ItemType.Wood, wood, _w.Stats);
                _w.RemoveTree(t);
                floodedTrees++;
            }
            var dirty = RiverBuilder.Carve(_w.Height, river, rec != null ? rec.Heights : null);
            _w.Rivers.Add(river);
            _w.RiverVersion++;
            _w.PresentationHeightDirty = _w.PresentationHeightDirty.Union(dirty);
            var cells = _w.Nav.SampleRectToCells(_w.Height, RiverBuilder.Bounds(_w.Height, river));
            var changed = _w.Nav.Rebuild(_w.Height, cells);
            changed = changed.Union(RiverBuilder.MarkWater(_w.Nav, river, true));
            _w.Water.MarkDirty();
            InvalidatePaths(changed.IsEmpty ? cells : changed);
            SitePlanner.RelocateBlocked(_w);
        }

        // ================================================================ summon / bridge

        private CommandResult ApplySummon(SummonCommand s)
        {
            var c = _w.Config;
            if (!InWorld(s.X, s.Z)) return CommandResult.Reject("召唤点超出山谷边界");
            int count = Math.Max(1, Math.Min(c.SummonMaxPerBatch, s.Count));
            if (_w.Population >= c.PopulationCap) return CommandResult.Reject("人口已达上限 " + c.PopulationCap);
            count = Math.Min(count, c.PopulationCap - _w.Population);
            var cell = _w.Nav.WorldToCell(s.X, s.Z);
            if (!_w.Nav.IsPassable(cell) || _w.Nav.IsWater(cell.X, cell.Z)) return CommandResult.Reject("落点无法通行");

            var camp = _w.Camp;
            if (camp == null)
            {
                // First summon founds the camp with modest starting supplies (GDD §3).
                camp = new StorageEntity { Id = _w.Ids.Next(), Kind = StorageKind.Camp, X = s.X, Z = s.Z };
                camp.Inventory = new Inventory(camp.Id, c.CampCapacity);
                InventorySystem.Seed(camp.Inventory, ItemType.Food, c.CampFood, _w.Stats);
                InventorySystem.Seed(camp.Inventory, ItemType.Wood, c.CampWood, _w.Stats);
                InventorySystem.Seed(camp.Inventory, ItemType.Water, c.CampWater, _w.Stats);
                _w.AddStorage(camp);
                SitePlanner.RelocateBlocked(_w);
            }
            else if (_w.Nav.RegionOf(cell) != _w.Nav.RegionNear(camp.X, camp.Z, 4))
                return CommandResult.Reject("落点与营地之间没有通路");

            string names = "";
            for (int i = 0; i < count; i++)
            {
                double a = i * 2 * Math.PI / count;
                float x = s.X + (float)Math.Cos(a) * 2.5f, z = s.Z + (float)Math.Sin(a) * 2.5f;
                var pc = _w.Nav.WorldToCell(x, z);
                if (!_w.Nav.IsPassable(pc)) { x = s.X; z = s.Z; }
                var v = PopulationSystem.Spawn(_w, x, z, camp.Id);
                names += (i > 0 ? "、" : "") + v.Name;
            }
            _w.Emit(SimEventType.VillagersArrived, EntityId.None, camp.Id, s.X, s.Z, count + " 位居民来到了山谷：" + names);
            if (_w.FoodDays < 1.5f)
                _w.Emit(SimEventType.Hint, EntityId.None, camp.Id, camp.X, camp.Z, "提示：食物只够约 " + _w.FoodDays.ToString("0.0") + " 天，居民会优先开垦农田");
            if (_w.Beds < _w.Population)
                _w.Emit(SimEventType.Hint, EventImportance.Info, EntityId.None, camp.Id, camp.X, camp.Z, "提示：床位不足，居民会先在篝火旁过夜并建造住宅");
            ConstructionSystem.AssignHomes(_w);
            return CommandResult.Ok();
        }

        public BridgePlan PreviewBridge(float ax, float az, float bx, float bz) => BridgePlanner.Plan(_w, ax, az, bx, bz);

        private CommandResult ApplyBridge(BridgeCommand b)
        {
            var plan = BridgePlanner.Plan(_w, b.AX, b.AZ, b.BX, b.BZ);
            if (!plan.Valid) return CommandResult.Reject(plan.Reason);
            int sites = 0;
            foreach (var x in _w.Buildings) if (x.Kind == BuildingKind.Bridge && !x.IsComplete) sites++;
            if (sites >= 3) return CommandResult.Reject("已有 3 座桥在等待施工");
            BridgePlanner.PlaceSite(_w, plan);
            return CommandResult.Ok();
        }

        // ================================================================ undo

        private CommandResult ApplyUndo(UndoCommand u)
        {
            _w.Undo.FinalizeAll(u.PlayerId, _w.Height);
            var rec = _w.Undo.Latest(u.PlayerId);
            if (rec == null) return CommandResult.Reject("没有可以撤销的编辑");
            string why = CheckUndo(rec);
            if (why != null) return CommandResult.Reject("无法撤销「" + rec.Label + "」：" + why);

            if (rec.Kind == UndoKind.River)
            {
                var river = _w.FindRiver(rec.RiverId);
                if (river != null)
                {
                    RiverBuilder.MarkWater(_w.Nav, river, false);
                    _w.Rivers.Remove(river);
                    foreach (var other in _w.Rivers) RiverBuilder.MarkWater(_w.Nav, other, true);
                    _w.RiverVersion++;
                    _w.Water.MarkDirty();
                }
            }
            foreach (var id in rec.PlantedTrees)
            {
                var t = _w.FindTree(id);
                if (t == null) continue;
                int wood = t.Stock.Get(ItemType.Wood);
                if (wood > 0) InventorySystem.Consume(t.Stock, ItemType.Wood, wood, _w.Stats);
                _w.Tasks.CancelFor(_w, t.Id);
                _w.RemoveTree(t);
            }
            var dirty = IntRect.Empty;
            foreach (var kv in rec.Heights.Before)
            {
                _w.Height.Heights[kv.Key] = kv.Value;
                int x = kv.Key % _w.Height.Resolution, z = kv.Key / _w.Height.Resolution;
                dirty = dirty.Union(new IntRect(x, z, x, z));
            }
            foreach (var s in rec.RemovedTrees) if (_w.FindTree(new EntityId(s.Id)) == null) Restore(s);
            if (!dirty.IsEmpty)
            {
                _w.PresentationHeightDirty = _w.PresentationHeightDirty.Union(dirty);
                var cells = _w.Nav.SampleRectToCells(_w.Height, dirty);
                var changed = _w.Nav.Rebuild(_w.Height, cells);
                InvalidatePaths(changed.IsEmpty ? cells : changed.Union(cells));
                SitePlanner.RelocateBlocked(_w);
            }
            _w.Undo.Records.Remove(rec);
            _w.Emit(SimEventType.UndoApplied, EventImportance.Story, EntityId.None, EntityId.None, 0, 0, "已撤销：" + rec.Label);
            return CommandResult.Ok();
        }

        private string CheckUndo(UndoRecord rec)
        {
            foreach (var kv in rec.Heights.Before)
            {
                if (!rec.After.TryGetValue(kv.Key, out float after)) continue;
                if (Math.Abs(_w.Height.Heights[kv.Key] - after) > 1e-6f) return "这片地形之后又被修改过";
            }
            var bounds = rec.Heights.Bounds;
            if (!bounds.IsEmpty)
            {
                float s = _w.Height.Spacing;
                foreach (var b in _w.Buildings)
                {
                    if (b.CreatedTick < rec.Tick) continue;
                    float r = b.BoundingRadius + 2f;
                    if (b.X + r < bounds.XMin * s || b.X - r > bounds.XMax * s || b.Z + r < bounds.ZMin * s || b.Z - r > bounds.ZMax * s) continue;
                    return "村民已经在这里开始建设";
                }
            }
            foreach (var id in rec.PlantedTrees)
            {
                var t = _w.FindTree(id);
                if (t == null) return "部分树木已经被砍伐或消失";
                if (t.ReservedBy.IsValid || (!t.IsSapling && t.Stock.Get(ItemType.Wood) < _w.Config.WoodPerTree)) return "有村民正在砍伐这些树";
            }
            if (rec.Kind == UndoKind.River)
            {
                var river = _w.FindRiver(rec.RiverId);
                foreach (var b in _w.Buildings)
                {
                    if (b.Kind == BuildingKind.FishingHut && river != null)
                    {
                        RiverBuilder.Nearest(river, b.SpotX, b.SpotZ, out float d, out _, out float hw);
                        if (d < hw + 4f) return "已有渔点依赖这条河";
                    }
                    if (b.Kind == BuildingKind.Bridge && river != null)
                    {
                        RiverBuilder.Nearest(river, b.X, b.Z, out float d, out _, out float hw);
                        if (d < hw + 2f) return "河上已经架了桥";
                    }
                }
            }
            return null;
        }

        private bool InWorld(float x, float z)
        {
            var c = _w.Config;
            return !(float.IsNaN(x) || float.IsNaN(z) || x < 0 || z < 0 || x > c.WorldSize || z > c.WorldSize);
        }
    }
}
