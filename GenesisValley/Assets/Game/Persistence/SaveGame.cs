using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GenesisValley.Core;
using GenesisValley.Simulation;
using GenesisValley.World;

namespace GenesisValley.Persistence
{
    public sealed class SaveHeader
    {
        public int SchemaVersion;
        public string GameVersion;
        public string WorldId;
        public ulong Seed;
        public long Tick;
        public ulong ConfigHash;
        // Slot summary (lets the menu list saves without decoding the body)
        public string WorldName;
        public int Day;
        public int Population;
        public long SavedUtcTicks;
    }

    public sealed class SaveLoadException : Exception
    {
        public SaveLoadException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Versioned binary snapshot (GDD §15). Header: schemaVersion, game version, world id, seed, tick, config hash (+ slot summary).
    /// Body (deflate-compressed): heights, rivers, roads, trees, storages, buildings/sites/bridges, piles, villagers,
    /// tasks with reservations, stats and the story feed. Transient paths and nav caches are rebuilt on load.
    /// </summary>
    public static class SaveGame
    {
        public const int SchemaVersion = 2;
        private const uint Magic = 0x56535647; // "GVSV"
        public static string GameVersion = "0.3.0-vertical-slice";

        // ================================================================== write

        public static byte[] Serialize(WorldState w)
        {
            byte[] body = SerializeBody(w);
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    WriteHeader(bw, w);
                    bw.Write(body.Length);
                    using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true)) z.Write(body, 0, body.Length);
                }
                return ms.ToArray();
            }
        }

        private static void WriteHeader(BinaryWriter bw, WorldState w)
        {
            bw.Write(Magic);
            bw.Write(SchemaVersion);
            bw.Write(GameVersion ?? "");
            bw.Write(w.WorldId ?? "");
            bw.Write(w.Seed);
            bw.Write(w.Tick);
            bw.Write(w.Config.ComputeHash());
            bw.Write(w.WorldName ?? "");
            bw.Write(w.Day);
            bw.Write(w.Population);
            bw.Write(DateTime.UtcNow.Ticks);
        }

        /// <summary>Canonical uncompressed body (also used for the state hash).</summary>
        public static byte[] SerializeBody(WorldState w)
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, true)) WriteBody(bw, w);
                return ms.ToArray();
            }
        }

        private static void WriteBody(BinaryWriter bw, WorldState w)
        {
            bw.Write(w.Ids.NextValue);
            bw.Write(w.Rng.State);
            bw.Write(w.LastAuthoritativeSequence);
            bw.Write(w.LastFoodShortageTick);
            bw.Write(w.LastWaterShortageTick);
            bw.Write(w.LastImmigrationDay);
            bw.Write(w.TickOffset);
            bw.Write(w.PlannerRetryTick.Length);
            foreach (var t in w.PlannerRetryTick) bw.Write(t);
            bw.Write(w.LastNoWaterHintTick); bw.Write(w.EcologyLastDecayDay); bw.Write(w.EcologyNextSeedTick);

            // heights
            bw.Write(w.Height.Resolution);
            WriteFloats(bw, w.Height.Heights);

            // rivers
            bw.Write(w.Rivers.Count);
            foreach (var r in w.Rivers)
            {
                bw.Write(r.Id); bw.Write(r.Version); bw.Write(r.Seed); bw.Write(r.Width); bw.Write(r.Depth); bw.Write(r.PondRadius);
                bw.Write(r.ControlPoints.Count);
                foreach (var p in r.ControlPoints) bw.Write(p);
                bw.Write(r.Samples.Count);
                foreach (var s in r.Samples) { bw.Write(s.X); bw.Write(s.Z); bw.Write(s.Surface); bw.Write(s.HalfWidth); bw.Write(s.Valid); }
            }

            // road wear
            bw.Write(w.Nav.CellCount);
            WriteFloats(bw, w.Nav.RawWear);

            // trees
            bw.Write(w.Trees.Count);
            foreach (var t in w.Trees)
            {
                bw.Write(t.Id.Value); bw.Write((int)t.Species); bw.Write(t.Variant);
                bw.Write(t.X); bw.Write(t.Z); bw.Write(t.RotationDegrees); bw.Write(t.Scale);
                bw.Write(t.Growth); bw.Write(t.FelledTick); bw.Write(t.PlantedByTransaction);
                WriteInventory(bw, t.Stock);
                bw.Write(t.ReservedBy.Value); bw.Write(t.PathFailures); bw.Write(t.PlanCooldownUntilTick);
            }

            // storages
            bw.Write(w.Storages.Count);
            foreach (var s in w.Storages)
            {
                bw.Write(s.Id.Value); bw.Write((int)s.Kind); bw.Write(s.BuildingId.Value); bw.Write(s.X); bw.Write(s.Z);
                WriteInventory(bw, s.Inventory);
            }

            // buildings
            bw.Write(w.Buildings.Count);
            foreach (var b in w.Buildings)
            {
                bw.Write(b.Id.Value); bw.Write((int)b.Kind); bw.Write(b.Variant);
                bw.Write(b.X); bw.Write(b.Z); bw.Write(b.Yaw); bw.Write(b.SizeX); bw.Write(b.SizeZ); bw.Write(b.PadHeight);
                bw.Write((int)b.State); bw.Write(b.WoodRequired); bw.Write(b.WoodConsumed); bw.Write(b.WorkRequired); bw.Write(b.WorkDone);
                WriteInventory(bw, b.Site);
                bw.Write(b.CreatedTick); bw.Write(b.CompletedTick); bw.Write(b.StorageId.Value);
                bw.Write(b.Residents.Count); foreach (var r in b.Residents) bw.Write(r.Value);
                bw.Write(b.Beds);
                bw.Write((int)b.Crop); bw.Write(b.CropGrowth); bw.Write(b.CropFood); bw.Write(b.Harvests);
                bw.Write(b.SpotX); bw.Write(b.SpotZ);
                bw.Write(b.AX); bw.Write(b.AZ); bw.Write(b.BX); bw.Write(b.BZ); bw.Write(b.AY); bw.Write(b.BY);
            }

            // piles
            bw.Write(w.Piles.Count);
            foreach (var p in w.Piles)
            {
                bw.Write(p.Id.Value); bw.Write(p.X); bw.Write(p.Z); WriteInventory(bw, p.Inventory); bw.Write(p.ReservedBy.Value);
            }

            // villagers
            bw.Write(w.Villagers.Count);
            foreach (var v in w.Villagers)
            {
                bw.Write(v.Id.Value); bw.Write(v.Name ?? ""); bw.Write(v.Gender); bw.Write(v.AppearanceSeed);
                bw.Write(v.X); bw.Write(v.Z); bw.Write(v.FacingDegrees);
                bw.Write(v.Hunger); bw.Write(v.Thirst); bw.Write(v.Energy); bw.Write(v.Health); bw.Write(v.Weak);
                bw.Write(v.Diligence); bw.Write(v.Curiosity); bw.Write(v.Sociability); bw.Write((int)v.Role);
                WriteInventory(bw, v.Carry);
                bw.Write((int)v.State); bw.Write((int)v.Action); bw.Write((int)v.Purpose);
                bw.Write(v.TaskId.Value); bw.Write(v.TargetId.Value); bw.Write(v.DropId.Value);
                bw.Write(v.ActionTicksLeft); bw.Write(v.IdleTicksLeft); bw.Write(v.GoalX); bw.Write(v.GoalZ);
                bw.Write(v.HomeId.Value); bw.Write(v.CampId.Value); bw.Write(v.TaskStartedTick); bw.Write(v.ArrivedTick);
                bw.Write(v.InsideHome);
                bw.Write(v.WorkedSecondsToday); bw.Write(v.NeedsRepath); bw.Write(v.PathIndex); bw.Write(v.LastCellIndex);
                bw.Write(v.Path.Count);
                foreach (var c in v.Path) { bw.Write((short)c.X); bw.Write((short)c.Z); }
                bw.Write(v.Recent.Count);
                foreach (var e in v.Recent) { bw.Write(e.Tick); bw.Write(e.Text ?? ""); }
            }

            // tasks
            bw.Write(w.Tasks.Tasks.Count);
            foreach (var t in w.Tasks.Tasks)
            {
                bw.Write(t.Id.Value); bw.Write((int)t.Type); bw.Write(t.TargetId.Value); bw.Write(t.SourceId.Value); bw.Write(t.DestId.Value);
                bw.Write(t.Amount); bw.Write((int)t.State); bw.Write(t.MaxAssignees); bw.Write(t.FailCount);
                bw.Write(t.CooldownUntilTick); bw.Write(t.CreatedTick); bw.Write(t.Priority);
                bw.Write(t.Assignees.Count); foreach (var a in t.Assignees) bw.Write(a.Value);
            }

            // stats
            var st = w.Stats;
            for (int i = 0; i < Inventory.ItemTypeCount; i++) { bw.Write(st.Created[i]); bw.Write(st.Consumed[i]); }
            bw.Write(st.WoodDelivered); bw.Write(st.TreesFelled); bw.Write(st.FoodHarvested); bw.Write(st.FishCaught);
            bw.Write(st.WaterFetched); bw.Write(st.BuildingsCompleted); bw.Write(st.Arrivals); bw.Write(st.PathFailures);
            bw.Write(st.Relocations); bw.Write((int)st.Reached);

            // story feed
            var story = w.Events.Story;
            int from = Math.Max(0, story.Count - 120);
            bw.Write(story.Count - from);
            for (int i = from; i < story.Count; i++)
            {
                var e = story[i];
                bw.Write(e.Tick); bw.Write((int)e.Type); bw.Write((int)e.Importance); bw.Write(e.Actor.Value); bw.Write(e.Target.Value);
                bw.Write(e.X); bw.Write(e.Z); bw.Write(e.Message ?? "");
            }
        }

        private static void WriteInventory(BinaryWriter bw, Inventory inv)
        {
            if (inv == null) { bw.Write(-1); return; }
            bw.Write(inv.Capacity);
            for (int i = 0; i < Inventory.ItemTypeCount; i++) bw.Write(inv.Get((ItemType)i));
        }

        private static void WriteFloats(BinaryWriter bw, float[] data)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }

        // ================================================================== read

        public static SaveHeader ReadHeader(BinaryReader br)
        {
            if (br.ReadUInt32() != Magic) throw new SaveLoadException("不是创世纪谷存档文件");
            var hdr = new SaveHeader { SchemaVersion = br.ReadInt32() };
            if (hdr.SchemaVersion > SchemaVersion) throw new SaveLoadException("存档版本 " + hdr.SchemaVersion + " 比游戏更新，无法载入");
            if (hdr.SchemaVersion < 2) throw new SaveLoadException("存档版本 " + hdr.SchemaVersion + " 太旧（技术验证版），请新建世界");
            hdr.GameVersion = br.ReadString();
            hdr.WorldId = br.ReadString();
            hdr.Seed = br.ReadUInt64();
            hdr.Tick = br.ReadInt64();
            hdr.ConfigHash = br.ReadUInt64();
            hdr.WorldName = br.ReadString();
            hdr.Day = br.ReadInt32();
            hdr.Population = br.ReadInt32();
            hdr.SavedUtcTicks = br.ReadInt64();
            return hdr;
        }

        public static SaveHeader PeekHeader(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs, Encoding.UTF8)) return ReadHeader(br);
        }

        public static WorldState Deserialize(byte[] data, SimConfig config, out SaveHeader header)
        {
            try
            {
                using (var ms = new MemoryStream(data))
                using (var br = new BinaryReader(ms, Encoding.UTF8))
                {
                    header = ReadHeader(br);
                    int bodyLen = br.ReadInt32();
                    if (bodyLen <= 0 || bodyLen > 256 * 1024 * 1024) throw new SaveLoadException("存档长度异常");
                    var body = new byte[bodyLen];
                    using (var z = new DeflateStream(ms, CompressionMode.Decompress, true))
                    {
                        int read = 0;
                        while (read < bodyLen)
                        {
                            int n = z.Read(body, read, bodyLen - read);
                            if (n <= 0) throw new SaveLoadException("存档数据被截断");
                            read += n;
                        }
                    }
                    var w = new WorldState(config, header.Seed) { WorldId = header.WorldId, Tick = header.Tick, WorldName = header.WorldName };
                    using (var bbr = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
                    {
                        ReadBody(bbr, w);
                        if (bbr.BaseStream.Position != bbr.BaseStream.Length) throw new SaveLoadException("存档末尾有多余数据");
                    }
                    RebuildDerived(w);
                    w.Emit(SimEventType.WorldLoaded, EventImportance.Info, EntityId.None, EntityId.None, 0, 0, "已载入世界（第 " + w.Day + " 天 " + w.ClockText + "）");
                    return w;
                }
            }
            catch (SaveLoadException) { throw; }
            catch (Exception e) { throw new SaveLoadException("存档损坏或不完整", e); }
        }

        /// <summary>Nav flags, water, bridges and reservations are derived data: rebuilt from the snapshot, never trusted.</summary>
        private static void RebuildDerived(WorldState w)
        {
            w.Nav.RebuildAll(w.Height);
            foreach (var r in w.Rivers) RiverBuilder.MarkWater(w.Nav, r, true);
            foreach (var b in w.Buildings)
            {
                if (b.Kind == BuildingKind.Bridge) { if (b.IsComplete) SitePlanner.ApplyBridgeDeck(w, b, true); }
                else if (b.BlocksNavigation)
                {
                    float rad = b.BoundingRadius + 1f;
                    var cells = w.Nav.WorldRectToCells(b.X - rad, b.Z - rad, b.X + rad, b.Z + rad);
                    for (int cz = cells.ZMin; cz <= cells.ZMax; cz++)
                    for (int cx = cells.XMin; cx <= cells.XMax; cx++)
                    {
                        w.Nav.CellCenter(new GridCell(cx, cz), out float wx, out float wz);
                        if (b.Contains(wx, wz, -0.2f)) w.Nav.SetFlag(cx, cz, CellBlock.Building, true);
                    }
                }
            }
            w.Water.MarkDirty();
            w.RiverVersion++;
            foreach (var v in w.Villagers)
            {
                if (v.State == VillagerState.Sleeping && v.InsideHome && w.FindBuilding(v.HomeId) == null) v.InsideHome = false;
            }
            w.Tasks.ValidateReservations(w);
            w.PresentationFullRefresh = true;
            w.PresentationHeightDirty = IntRect.Empty;
        }

        private static void ReadBody(BinaryReader br, WorldState w)
        {
            w.Ids.Restore(br.ReadInt32());
            w.Rng.Restore(br.ReadUInt64());
            w.LastAuthoritativeSequence = br.ReadInt64();
            w.LastFoodShortageTick = br.ReadInt64();
            w.LastWaterShortageTick = br.ReadInt64();
            w.LastImmigrationDay = br.ReadInt32();
            w.TickOffset = br.ReadInt64();
            int nPlanner = br.ReadInt32();
            if (nPlanner != w.PlannerRetryTick.Length) throw new SaveLoadException("规划器数据异常");
            for (int k = 0; k < nPlanner; k++) w.PlannerRetryTick[k] = br.ReadInt64();
            w.LastNoWaterHintTick = br.ReadInt64(); w.EcologyLastDecayDay = br.ReadInt32(); w.EcologyNextSeedTick = br.ReadInt64();

            int res = br.ReadInt32();
            if (res != w.Height.Resolution) throw new SaveLoadException("高度图分辨率不匹配");
            ReadFloats(br, w.Height.Heights);

            int rivers = br.ReadInt32();
            for (int i = 0; i < rivers; i++)
            {
                var r = new RiverData
                {
                    Id = br.ReadInt32(), Version = br.ReadInt32(), Seed = br.ReadInt32(), Width = br.ReadSingle(), Depth = br.ReadSingle(), PondRadius = br.ReadSingle(),
                };
                int cp = br.ReadInt32();
                for (int k = 0; k < cp; k++) r.ControlPoints.Add(br.ReadSingle());
                int ns = br.ReadInt32();
                for (int k = 0; k < ns; k++)
                    r.Samples.Add(new RiverSample { X = br.ReadSingle(), Z = br.ReadSingle(), Surface = br.ReadSingle(), HalfWidth = br.ReadSingle(), Valid = br.ReadBoolean() });
                w.Rivers.Add(r);
            }

            int cells = br.ReadInt32();
            if (cells != w.Nav.CellCount) throw new SaveLoadException("通行网格尺寸不匹配");
            ReadFloats(br, w.Nav.RawWear);

            int trees = br.ReadInt32();
            for (int i = 0; i < trees; i++)
            {
                var t = new TreeEntity
                {
                    Id = new EntityId(br.ReadInt32()), Species = (TreeSpecies)br.ReadInt32(), Variant = br.ReadInt32(),
                    X = br.ReadSingle(), Z = br.ReadSingle(), RotationDegrees = br.ReadSingle(), Scale = br.ReadSingle(),
                    Growth = br.ReadSingle(), FelledTick = br.ReadInt64(), PlantedByTransaction = br.ReadInt32(),
                };
                t.Stock = ReadInventory(br, t.Id);
                t.ReservedBy = new EntityId(br.ReadInt32()); t.PathFailures = br.ReadInt32(); t.PlanCooldownUntilTick = br.ReadInt64();
                w.AddTree(t);
            }

            int storages = br.ReadInt32();
            for (int i = 0; i < storages; i++)
            {
                var s = new StorageEntity { Id = new EntityId(br.ReadInt32()), Kind = (StorageKind)br.ReadInt32(), BuildingId = new EntityId(br.ReadInt32()), X = br.ReadSingle(), Z = br.ReadSingle() };
                s.Inventory = ReadInventory(br, s.Id);
                w.AddStorage(s);
            }

            int buildings = br.ReadInt32();
            for (int i = 0; i < buildings; i++)
            {
                var b = new BuildingEntity
                {
                    Id = new EntityId(br.ReadInt32()), Kind = (BuildingKind)br.ReadInt32(), Variant = br.ReadInt32(),
                    X = br.ReadSingle(), Z = br.ReadSingle(), Yaw = br.ReadSingle(), SizeX = br.ReadSingle(), SizeZ = br.ReadSingle(), PadHeight = br.ReadSingle(),
                    State = (BuildingState)br.ReadInt32(), WoodRequired = br.ReadInt32(), WoodConsumed = br.ReadInt32(), WorkRequired = br.ReadSingle(), WorkDone = br.ReadSingle(),
                };
                b.Site = ReadInventory(br, b.Id);
                b.CreatedTick = br.ReadInt64(); b.CompletedTick = br.ReadInt64(); b.StorageId = new EntityId(br.ReadInt32());
                int nr = br.ReadInt32();
                for (int k = 0; k < nr; k++) b.Residents.Add(new EntityId(br.ReadInt32()));
                b.Beds = br.ReadInt32();
                b.Crop = (CropState)br.ReadInt32(); b.CropGrowth = br.ReadSingle(); b.CropFood = br.ReadInt32(); b.Harvests = br.ReadInt32();
                b.SpotX = br.ReadSingle(); b.SpotZ = br.ReadSingle();
                b.AX = br.ReadSingle(); b.AZ = br.ReadSingle(); b.BX = br.ReadSingle(); b.BZ = br.ReadSingle(); b.AY = br.ReadSingle(); b.BY = br.ReadSingle();
                w.AddBuilding(b);
            }

            int piles = br.ReadInt32();
            for (int i = 0; i < piles; i++)
            {
                var p = new ResourcePile { Id = new EntityId(br.ReadInt32()), X = br.ReadSingle(), Z = br.ReadSingle() };
                p.Inventory = ReadInventory(br, p.Id);
                p.ReservedBy = new EntityId(br.ReadInt32());
                w.Piles.Add(p);
            }

            int villagers = br.ReadInt32();
            for (int i = 0; i < villagers; i++)
            {
                var v = new Villager { Id = new EntityId(br.ReadInt32()), Name = br.ReadString(), Gender = br.ReadInt32(), AppearanceSeed = br.ReadInt32() };
                v.X = br.ReadSingle(); v.Z = br.ReadSingle(); v.FacingDegrees = br.ReadSingle();
                v.Hunger = br.ReadSingle(); v.Thirst = br.ReadSingle(); v.Energy = br.ReadSingle(); v.Health = br.ReadSingle(); v.Weak = br.ReadBoolean();
                v.Diligence = br.ReadSingle(); v.Curiosity = br.ReadSingle(); v.Sociability = br.ReadSingle(); v.Role = (JobRole)br.ReadInt32();
                v.Carry = ReadInventory(br, v.Id);
                v.State = (VillagerState)br.ReadInt32(); v.Action = (VillagerAction)br.ReadInt32(); v.Purpose = (WalkPurpose)br.ReadInt32();
                v.TaskId = new EntityId(br.ReadInt32()); v.TargetId = new EntityId(br.ReadInt32()); v.DropId = new EntityId(br.ReadInt32());
                v.ActionTicksLeft = br.ReadInt32(); v.IdleTicksLeft = br.ReadInt32(); v.GoalX = br.ReadSingle(); v.GoalZ = br.ReadSingle();
                v.HomeId = new EntityId(br.ReadInt32()); v.CampId = new EntityId(br.ReadInt32()); v.TaskStartedTick = br.ReadInt64(); v.ArrivedTick = br.ReadInt64();
                v.InsideHome = br.ReadBoolean();
                v.WorkedSecondsToday = br.ReadSingle(); v.NeedsRepath = br.ReadBoolean(); v.PathIndex = br.ReadInt32(); v.LastCellIndex = br.ReadInt32();
                int np = br.ReadInt32();
                if (np < 0 || np > 100000) throw new SaveLoadException("村民路径数据异常");
                for (int k = 0; k < np; k++) { int px = br.ReadInt16(); int pz = br.ReadInt16(); v.Path.Add(new GridCell(px, pz)); }
                int ne = br.ReadInt32();
                for (int k = 0; k < ne; k++) v.Recent.Add(new PersonalEvent { Tick = br.ReadInt64(), Text = br.ReadString() });
                w.AddVillager(v);
            }

            int tasks = br.ReadInt32();
            for (int i = 0; i < tasks; i++)
            {
                var t = new WorkTask
                {
                    Id = new EntityId(br.ReadInt32()), Type = (TaskType)br.ReadInt32(), TargetId = new EntityId(br.ReadInt32()),
                    SourceId = new EntityId(br.ReadInt32()), DestId = new EntityId(br.ReadInt32()), Amount = br.ReadInt32(),
                    State = (TaskState)br.ReadInt32(), MaxAssignees = br.ReadInt32(), FailCount = br.ReadInt32(),
                    CooldownUntilTick = br.ReadInt64(), CreatedTick = br.ReadInt64(), Priority = br.ReadSingle(),
                };
                int na = br.ReadInt32();
                for (int k = 0; k < na; k++) t.Assignees.Add(new EntityId(br.ReadInt32()));
                w.Tasks.Tasks.Add(t);
            }

            var st = w.Stats;
            for (int i = 0; i < Inventory.ItemTypeCount; i++) { st.Created[i] = br.ReadInt64(); st.Consumed[i] = br.ReadInt64(); }
            st.WoodDelivered = br.ReadInt64(); st.TreesFelled = br.ReadInt32(); st.FoodHarvested = br.ReadInt32(); st.FishCaught = br.ReadInt32();
            st.WaterFetched = br.ReadInt32(); st.BuildingsCompleted = br.ReadInt32(); st.Arrivals = br.ReadInt32(); st.PathFailures = br.ReadInt32();
            st.Relocations = br.ReadInt32(); st.Reached = (Milestones)br.ReadInt32();

            int story = br.ReadInt32();
            for (int i = 0; i < story; i++)
            {
                w.Events.RestoreStory(new SimEvent
                {
                    Tick = br.ReadInt64(), Type = (SimEventType)br.ReadInt32(), Importance = (EventImportance)br.ReadInt32(),
                    Actor = new EntityId(br.ReadInt32()), Target = new EntityId(br.ReadInt32()), X = br.ReadSingle(), Z = br.ReadSingle(), Message = br.ReadString(),
                });
            }
        }

        private static Inventory ReadInventory(BinaryReader br, EntityId owner)
        {
            int cap = br.ReadInt32();
            if (cap < 0) return null;
            var inv = new Inventory(owner, cap);
            for (int i = 0; i < Inventory.ItemTypeCount; i++)
            {
                int n = br.ReadInt32();
                if (n < 0) throw new SaveLoadException("库存数量为负");
                inv.SetRaw((ItemType)i, n);
            }
            return inv;
        }

        private static void ReadFloats(BinaryReader br, float[] target)
        {
            var bytes = br.ReadBytes(target.Length * 4);
            if (bytes.Length != target.Length * 4) throw new SaveLoadException("存档数据被截断");
            Buffer.BlockCopy(bytes, 0, target, 0, bytes.Length);
            for (int i = 0; i < target.Length; i++) if (float.IsNaN(target[i]) || float.IsInfinity(target[i])) throw new SaveLoadException("存档包含非法数值");
        }

        // ================================================================== hashing & files

        /// <summary>SHA-256 over the canonical uncompressed body + tick/seed. Transient paths/events are not part of it.</summary>
        public static string ComputeStateHash(WorldState w)
        {
            using (var sha = SHA256.Create())
            {
                var body = SerializeBody(w);
                var bytes = sha.ComputeHash(body);
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Write to temp, verify by reading back, keep one backup, then atomically replace (GDD §15).</summary>
        public static void WriteFile(string path, WorldState w) => WriteBytes(path, Serialize(w), w.Config);

        /// <summary>For background saving: snapshot bytes are produced on the sim thread, then written here on any thread.</summary>
        public static void WriteBytes(string path, byte[] data, SimConfig config)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllBytes(tmp, data);
            Deserialize(File.ReadAllBytes(tmp), config, out _); // verify readable before replacing
            if (File.Exists(path)) File.Replace(tmp, path, bak);
            else File.Move(tmp, path);
        }

        /// <summary>Loads the main file; if it is corrupt, tries the backup and reports which one was used.</summary>
        public static WorldState ReadFile(string path, SimConfig config, out SaveHeader header, out bool usedBackup)
        {
            usedBackup = false;
            try
            {
                return Deserialize(File.ReadAllBytes(path), config, out header);
            }
            catch (Exception primary) when (File.Exists(path + ".bak"))
            {
                usedBackup = true;
                try { return Deserialize(File.ReadAllBytes(path + ".bak"), config, out header); }
                catch (Exception) { throw new SaveLoadException("主存档和备份都无法载入", primary); }
            }
        }
    }
}
