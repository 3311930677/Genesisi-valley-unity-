using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenesisValley.Core;
using GenesisValley.Persistence;
using GenesisValley.Simulation;
using GenesisValley.World;
using NUnit.Framework;

namespace GenesisValley.Tests
{
    /// <summary>
    /// Stage A–C acceptance and GDD §15 key cases. Engine-free: runs in the Test Runner (EditMode) and headless (dotnet test).
    /// </summary>
    public class GameplayTests
    {
        private const ulong Seed = 20261015;

        private static SimulationRunner NewRunner(out WorldState w)
        {
            w = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            return new SimulationRunner(w);
        }

        private static int Minutes(WorldState w, float m) => w.Config.SecondsToTicks(m * 60f);
        private static int Days(WorldState w, float d) => w.Config.SecondsToTicks(d * w.Config.SecondsPerGameDay);

        private static void AssertConservation(WorldState w, string when)
        {
            for (int i = 0; i < Inventory.ItemTypeCount; i++)
            {
                var it = (ItemType)i;
                Assert.AreEqual(w.Stats.Created[i] - w.Stats.Consumed[i], w.TotalItems(it), it + " conservation " + when);
            }
        }

        /// <summary>Finds an open, plantable, river-free spot (deterministic scan) so tests don't depend on map art.</summary>
        private static void OpenSpot(WorldState w, float minCampDist, float clearRadius, out float x, out float z)
        {
            var camp = w.Camp;
            for (float r = minCampDist; r < 160f; r += 6f)
            for (int i = 0; i < 24; i++)
            {
                float a = i * 6.2831853f / 24f;
                x = camp.X + (float)System.Math.Cos(a) * r; z = camp.Z + (float)System.Math.Sin(a) * r;
                bool ok = true;
                for (int k = 0; k < 9 && ok; k++)
                {
                    float px = x + (k % 3 - 1) * clearRadius * 0.7f, pz = z + (k / 3 - 1) * clearRadius * 0.7f;
                    ok = ForestPlanter.IsPlantable(w, px, pz);
                }
                if (!ok) continue;
                float cx = x, cz = z;
                if (w.Trees.Any(t => (t.X - cx) * (t.X - cx) + (t.Z - cz) * (t.Z - cz) < (clearRadius + 4) * (clearRadius + 4))) continue;
                bool nearRiver = false;
                foreach (var rv in w.Rivers) { RiverBuilder.Nearest(rv, x, z, out float d, out _, out float hw); if (d < hw + clearRadius + 12f) nearRiver = true; }
                if (nearRiver) continue;
                if (w.Buildings.Any(b => SitePlanner.Dist(b.X, b.Z, cx, cz) < b.BoundingRadius + clearRadius + 6f)) continue;
                return;
            }
            throw new System.Exception("no open spot");
        }

        // ------------------------------------------------------------------ inventory / generator

        [Test]
        public void Inventory_Transfer_IsAtomicAndConserving()
        {
            var stats = new WorldStats();
            var a = new Inventory(new EntityId(1), 10);
            var b = new Inventory(new EntityId(2), 3);
            InventorySystem.Seed(a, ItemType.Wood, 8, stats);
            int moved = InventorySystem.Transfer(a, b, ItemType.Wood, 5);
            Assert.AreEqual(3, moved, "capacity of target limits the move");
            Assert.AreEqual(5, a.Get(ItemType.Wood));
            Assert.AreEqual(3, b.Get(ItemType.Wood));
            Assert.AreEqual(0, InventorySystem.Transfer(a, b, ItemType.Wood, 1), "full target accepts nothing");
            Assert.AreEqual(0, InventorySystem.Transfer(a, a, ItemType.Wood, 1), "self transfer is a no-op");
            Assert.AreEqual(8, a.Get(ItemType.Wood) + b.Get(ItemType.Wood));
        }

        [Test]
        public void Generator_IsDeterministic_AndSeedsTheValley()
        {
            var a = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            var b = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            Assert.AreEqual(SaveGame.ComputeStateHash(a), SaveGame.ComputeStateHash(b));
            var c = a.Config;
            Assert.AreEqual(c.StartingVillagers, a.Villagers.Count);
            Assert.Greater(a.Trees.Count, 60);
            Assert.AreEqual(1, a.Rivers.Count, "the valley starts with one stream");
            Assert.Greater(a.Water.Shore.Count, 20);
            Assert.AreEqual(c.CampFood, a.Camp.Inventory.Get(ItemType.Food));
            Assert.AreEqual(c.CampWood, a.Camp.Inventory.Get(ItemType.Wood));
            Assert.AreEqual(c.CampWater, a.Camp.Inventory.Get(ItemType.Water));
            foreach (var v in a.Villagers) Assert.IsTrue(a.Nav.IsPassable(a.Nav.WorldToCell(v.X, v.Z)), v.Name + " spawns on walkable ground");
        }

        // ------------------------------------------------------------------ stage A: chop → carry → build

        [Test]
        public void StageA_VillagersChopDeliverAndBuildFirstHouse()
        {
            var r = NewRunner(out var w);
            r.Run(Minutes(w, 3));
            Assert.Greater(w.Stats.TreesFelled, 0, "trees are felled within 3 minutes");
            Assert.Greater(w.Stats.WoodDelivered, 0, "wood physically arrives");
            r.Run(Days(w, 1.6f));
            var house = w.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.House && b.IsComplete);
            Assert.IsNotNull(house, "the first house is finished by the second day");
            Assert.AreEqual(w.Config.HouseWood, house.WoodConsumed, "a house consumes exactly its wood cost");
            Assert.Greater(w.Villagers.Count(v => v.HomeId == house.Id), 0, "someone moved in");
            AssertConservation(w, "after first house");
        }

        [Test]
        public void Items_AreConserved_AndReservationsStayExclusive()
        {
            var r = NewRunner(out var w);
            var owners = new HashSet<EntityId>();
            int steps = Days(w, 1.2f);
            for (int i = 0; i < steps; i++)
            {
                r.Step();
                if (i % 50 != 0) continue;
                AssertConservation(w, "tick " + w.Tick);
                owners.Clear();
                foreach (var t in w.Tasks.Tasks)
                {
                    if (!t.IsActive || t.Type != TaskType.ChopTree) continue;
                    Assert.IsTrue(owners.Add(t.TargetId), "tree " + t.TargetId + " has two active chop tasks");
                    Assert.LessOrEqual(t.Assignees.Count, t.MaxAssignees);
                }
                foreach (var tr in w.Trees)
                {
                    if (!tr.ReservedBy.IsValid) continue;
                    var task = w.Tasks.Tasks.FirstOrDefault(t => t.IsActive && t.TargetId == tr.Id);
                    Assert.IsNotNull(task, "a reserved tree always has a live task");
                }
                foreach (var v in w.Villagers) Assert.LessOrEqual(v.Carry.Total, v.Carry.Capacity);
            }
            Assert.AreEqual(0, w.Tasks.ValidateReservations(w), "no stale reservations");
        }

        [Test]
        public void Food_IsEatenAtAboutTwoUnitsPerAdultPerDay()
        {
            var w = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            var r = new SimulationRunner(w);
            r.Run(Days(w, 1));
            long before = w.Stats.Consumed[(int)ItemType.Food];
            int pop = w.Population;
            r.Run(Days(w, 1));
            long eaten = w.Stats.Consumed[(int)ItemType.Food] - before;
            Assert.That(eaten, Is.InRange(pop * 2 - 6, pop * 2 + 8), "about 2 food per adult per day (GDD §5)");
        }

        // ------------------------------------------------------------------ terrain tools

        [Test]
        public void TerrainBrush_StrokeLimit_Duplicates_AndCampProtection()
        {
            var r = NewRunner(out var w);
            var camp = w.Camp;
            float campH = w.Height.SampleHeight(camp.X, camp.Z);
            float px = camp.X + 30f, pz = camp.Z + 12f;
            int v0 = w.Nav.Version;
            int n = (int)System.Math.Round(w.Config.BrushMaxDeltaPerStroke / w.Config.BrushMaxDeltaPerCommand);
            for (int i = 0; i < n; i++)
            {
                var res = r.ApplyImmediate(new TerrainBrushCommand { CommandId = 100 + i, PlayerId = 1, TransactionId = 1, Mode = BrushMode.Raise, CenterX = px, CenterZ = pz, Radius = 3f, Strength = w.Config.BrushMaxDeltaPerCommand });
                Assert.IsTrue(res.Accepted, res.Reason);
            }
            Assert.Greater(w.Nav.Version, v0, "navigation updated");
            var over = r.ApplyImmediate(new TerrainBrushCommand { CommandId = 200, PlayerId = 1, TransactionId = 1, Mode = BrushMode.Raise, CenterX = px, CenterZ = pz, Radius = 3f, Strength = w.Config.BrushMaxDeltaPerCommand });
            Assert.IsFalse(over.Accepted, "stroke height limit");
            var dup = r.ApplyImmediate(new TerrainBrushCommand { CommandId = 100, PlayerId = 1, TransactionId = 2, Mode = BrushMode.Raise, CenterX = px, CenterZ = pz, Radius = 3f, Strength = 0.5f });
            Assert.IsFalse(dup.Accepted, "same command id applies only once");

            r.ApplyImmediate(new TerrainBrushCommand { CommandId = 300, PlayerId = 1, TransactionId = 3, Mode = BrushMode.Raise, CenterX = camp.X, CenterZ = camp.Z, Radius = 6f, Strength = 0.5f });
            Assert.AreEqual(campH, w.Height.SampleHeight(camp.X, camp.Z), 1e-4f, "camp protection band keeps its ground");
        }

        [Test]
        public void Flatten_ConvergesToLockedTarget_WithoutOvershoot()
        {
            var r = NewRunner(out var w);
            OpenSpot(w, 30f, 6f, out float x, out float z);
            float target = w.Height.SampleHeight(x, z) + 1.5f;
            for (int i = 0; i < 40; i++)
                r.ApplyImmediate(new TerrainBrushCommand { CommandId = 10 + i, PlayerId = 1, TransactionId = 7, Mode = BrushMode.Flatten, CenterX = x, CenterZ = z, Radius = 5f, Strength = w.Config.BrushMaxDeltaPerCommand, TargetHeight = target });
            Assert.AreEqual(target, w.Height.SampleHeight(x, z), 0.05f, "centre reaches the target recorded at stroke start");
            Assert.LessOrEqual(w.Height.SampleHeight(x + 2f, z), target + 0.01f, "never overshoots");
        }

        [Test]
        public void Wall_BlocksRoute_AndPathfinderDetours()
        {
            var r = NewRunner(out var w);
            var camp = w.Camp;
            float rx = camp.X - 45f;
            var from = w.Nav.WorldToCell(rx, camp.Z - 30);
            var to = w.Nav.WorldToCell(rx, camp.Z + 30);
            var before = new List<GridCell>();
            Assert.AreEqual(PathStatus.Found, w.Paths.FindPath(from, to, before));
            int cmd = 1;
            for (float x = rx - 18; x <= rx + 18; x += 2f)
            {
                int tx = (int)(x * 10);
                float total = 0;
                while (total + w.Config.BrushMaxDeltaPerCommand <= w.Config.BrushMaxDeltaPerStroke)
                {
                    r.ApplyImmediate(new TerrainBrushCommand { CommandId = cmd++, PlayerId = 1, TransactionId = tx, Mode = BrushMode.Raise, CenterX = x, CenterZ = camp.Z, Radius = 3f, Strength = w.Config.BrushMaxDeltaPerCommand });
                    total += w.Config.BrushMaxDeltaPerCommand;
                }
                r.ApplyImmediate(new EndTransactionCommand { CommandId = cmd++, PlayerId = 1, TransactionId = tx });
            }
            bool flankBlocked = !w.Nav.IsPassable(w.Nav.WorldToCell(rx, camp.Z + 1.5f)) || !w.Nav.IsPassable(w.Nav.WorldToCell(rx, camp.Z - 1.5f));
            Assert.IsTrue(flankBlocked, "ridge flank must be unwalkable on the straight route");
            var after = new List<GridCell>();
            Assert.AreEqual(PathStatus.Found, w.Paths.FindPath(from, to, after), "a detour exists");
            Assert.Greater(PathLength(after), PathLength(before) + 4f, "route got longer after the edit");
            foreach (var c in after) Assert.IsTrue(w.Nav.IsPassable(c));
        }

        private static float PathLength(List<GridCell> p)
        {
            float len = 0;
            for (int i = 1; i < p.Count; i++)
            {
                int dx = p[i].X - p[i - 1].X, dz = p[i].Z - p[i - 1].Z;
                len += (float)System.Math.Sqrt(dx * dx + dz * dz);
            }
            return len;
        }

        [Test]
        public void Undo_RestoresTerrainExactly()
        {
            var r = NewRunner(out var w);
            float x = w.Camp.X + 35f, z = w.Camp.Z + 25f;
            var before = w.Height.ComputeHash();
            r.ApplyImmediate(new TerrainBrushCommand { CommandId = 1, PlayerId = 1, TransactionId = 1, Mode = BrushMode.Raise, CenterX = x, CenterZ = z, Radius = 6f, Strength = 0.5f });
            r.ApplyImmediate(new TerrainBrushCommand { CommandId = 2, PlayerId = 1, TransactionId = 1, Mode = BrushMode.Raise, CenterX = x + 2, CenterZ = z, Radius = 6f, Strength = 0.5f });
            r.ApplyImmediate(new EndTransactionCommand { CommandId = 3, PlayerId = 1, TransactionId = 1 });
            Assert.AreNotEqual(before, w.Height.ComputeHash());
            var res = r.ApplyImmediate(new UndoCommand { CommandId = 4, PlayerId = 1 });
            Assert.IsTrue(res.Accepted, res.Reason);
            Assert.AreEqual(before, w.Height.ComputeHash(), "undo restores every height sample");
            Assert.IsFalse(r.ApplyImmediate(new UndoCommand { CommandId = 5, PlayerId = 1 }).Accepted, "nothing left to undo");
        }

        // ------------------------------------------------------------------ forest

        [Test]
        public void Forest_IsDeterministic_RespectsDensity_AndUndoRemovesSaplings()
        {
            var a = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            var b = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            OpenSpot(a, 30f, 10f, out float x, out float z);
            var ra = new SimulationRunner(a); var rb = new SimulationRunner(b);
            int t0 = a.Trees.Count;
            foreach (var r in new[] { ra, rb })
            {
                var res = r.ApplyImmediate(new ForestCommand { CommandId = 1, PlayerId = 1, TransactionId = 9, CenterX = x, CenterZ = z, Radius = 10f });
                Assert.IsTrue(res.Accepted, res.Reason);
                r.ApplyImmediate(new EndTransactionCommand { CommandId = 2, PlayerId = 1, TransactionId = 9 });
            }
            int planted = a.Trees.Count - t0;
            Assert.Greater(planted, 3);
            Assert.LessOrEqual(planted, a.Config.ForestMaxPerStamp);
            Assert.AreEqual(SaveGame.ComputeStateHash(a), SaveGame.ComputeStateHash(b), "same command → same forest");
            // Stamping the same spot again quickly saturates.
            for (int i = 0; i < 6; i++) ra.ApplyImmediate(new ForestCommand { CommandId = 10 + i, PlayerId = 1, TransactionId = 20 + i, CenterX = x, CenterZ = z, Radius = 10f });
            int dense = a.Trees.Count(t => (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < 100f);
            Assert.Less(dense, 40, "density cap holds");
            var u = rb.ApplyImmediate(new UndoCommand { CommandId = 99, PlayerId = 1 });
            Assert.IsTrue(u.Accepted, u.Reason);
            Assert.AreEqual(t0, b.Trees.Count, "undo removes planted saplings");
        }

        // ------------------------------------------------------------------ rivers

        [Test]
        public void River_RejectsCampCrossing_AndAppliesElsewhere_ThenUndo()
        {
            var r = NewRunner(out var w);
            var camp = w.Camp;
            var bad = r.Commands.PreviewRiver(new List<float> { camp.X - 30, camp.Z, camp.X + 30, camp.Z }, 4f);
            Assert.IsFalse(bad.Valid, "a river may not cut through the camp");

            var pts = new List<float> { 110f, 420f, 130f, 380f, 150f, 340f };
            var plan = r.Commands.PreviewRiver(pts, 4f);
            Assert.IsTrue(plan.Valid, plan.Reason);
            int rivers0 = w.Rivers.Count;
            var cmd = new RiverCommand { CommandId = 50, PlayerId = 1, TransactionId = 50, Width = 4f };
            cmd.Points.AddRange(pts);
            var res = r.ApplyImmediate(cmd);
            Assert.IsTrue(res.Accepted, res.Reason);
            Assert.AreEqual(rivers0 + 1, w.Rivers.Count);
            var mid = plan.Samples[plan.Samples.Count / 2];
            Assert.IsTrue(w.Nav.IsWater(w.Nav.WorldToCell(mid.X, mid.Z).X, w.Nav.WorldToCell(mid.X, mid.Z).Z), "the channel is water on the nav grid");
            for (int i = 1; i < plan.Samples.Count; i++)
                Assert.LessOrEqual(plan.Samples[i].Surface, plan.Samples[i - 1].Surface + 1e-4f, "water surface never flows uphill");

            var u = r.ApplyImmediate(new UndoCommand { CommandId = 51, PlayerId = 1 });
            Assert.IsTrue(u.Accepted, u.Reason);
            Assert.AreEqual(rivers0, w.Rivers.Count);
            AssertConservation(w, "after river undo");
        }

        // ------------------------------------------------------------------ bridge

        [Test]
        public void Bridge_JoinsBanks_WhenBuilt()
        {
            var r = NewRunner(out var w);
            var river = w.Rivers[0];
            var s = river.Samples[river.Samples.Count / 2];
            // Perpendicular to the local flow.
            var s2 = river.Samples[river.Samples.Count / 2 + 1];
            float dx = s2.X - s.X, dz = s2.Z - s.Z, l = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float px = -dz / l, pz = dx / l, half = river.Width * 0.5f + 3f;
            float ax = s.X + px * half, az = s.Z + pz * half, bx = s.X - px * half, bz = s.Z - pz * half;
            var plan = r.Commands.PreviewBridge(ax, az, bx, bz);
            Assert.IsTrue(plan.Valid, plan.Reason);
            Assert.Less(plan.Length, 30f);
            var res = r.ApplyImmediate(new BridgeCommand { CommandId = 70, PlayerId = 1, AX = ax, AZ = az, BX = bx, BZ = bz });
            Assert.IsTrue(res.Accepted, res.Reason);
            var site = w.Buildings.Last(b => b.Kind == BuildingKind.Bridge);
            Assert.IsFalse(site.IsComplete);
            // Fast-forward the work (logistics are covered elsewhere): consume the cost and complete.
            InventorySystem.Seed(site.Site, ItemType.Wood, site.WoodRequired, w.Stats);
            ConstructionSystem.Complete(w, site, null);
            Assert.IsTrue(site.IsComplete);
            Assert.AreEqual(w.Nav.RegionNear(plan.AX, plan.AZ, 3), w.Nav.RegionNear(plan.BX, plan.BZ, 3), "both banks are connected");
            var path = new List<GridCell>();
            Assert.AreEqual(PathStatus.Found, w.Paths.FindPath(w.Nav.WorldToCell(plan.AX, plan.AZ), w.Nav.WorldToCell(plan.BX, plan.BZ), path));
            Assert.Less(PathLength(path) * w.Nav.CellSize, plan.Length * 3f + 10f, "the crossing is direct");
        }

        // ------------------------------------------------------------------ sites

        [Test]
        public void CancelledSite_RefundsDeliveredWood_WithoutDuplication()
        {
            var r = NewRunner(out var w);
            BuildingEntity site = null;
            for (int i = 0; i < Days(w, 1.5f) && site == null; i++)
            {
                r.Step();
                site = w.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.House && !b.IsComplete && b.Site.Get(ItemType.Wood) + b.WoodConsumed >= 4);
            }
            Assert.IsNotNull(site, "a house site received wood");
            long woodBefore = w.TotalItems(ItemType.Wood);
            ConstructionSystem.CancelSite(w, site, "测试取消");
            Assert.IsFalse(w.Buildings.Contains(site));
            AssertConservation(w, "after cancel");
            Assert.LessOrEqual(w.TotalItems(ItemType.Wood), woodBefore, "cancel never creates wood");
            r.Run(Minutes(w, 4));
            AssertConservation(w, "after the refund pile is hauled");
            Assert.AreEqual(0, w.Tasks.ValidateReservations(w));
        }

        [Test]
        public void BlockedTarget_ReleasesTask_AndVillageContinues()
        {
            var r = NewRunner(out var w);
            var tree = w.Trees.Where(t => t.Harvestable).OrderBy(t => (t.X - w.Camp.X) * (t.X - w.Camp.X) + (t.Z - w.Camp.Z) * (t.Z - w.Camp.Z)).Skip(3).First();
            int cmd = 1, tx = 1000;
            for (int a = 0; a < 28; a++)
            {
                float ang = a * 3.14159f * 2 / 28;
                float x = tree.X + (float)System.Math.Cos(ang) * 8f, z = tree.Z + (float)System.Math.Sin(ang) * 8f;
                float total = 0;
                while (total + w.Config.BrushMaxDeltaPerCommand <= w.Config.BrushMaxDeltaPerStroke)
                {
                    r.ApplyImmediate(new TerrainBrushCommand { CommandId = cmd++, PlayerId = 2, TransactionId = tx, Mode = BrushMode.Raise, CenterX = x, CenterZ = z, Radius = 3f, Strength = w.Config.BrushMaxDeltaPerCommand });
                    total += w.Config.BrushMaxDeltaPerCommand;
                }
                r.ApplyImmediate(new EndTransactionCommand { CommandId = cmd++, PlayerId = 2, TransactionId = tx++ });
            }
            r.Run(Minutes(w, 8));
            foreach (var t in w.Tasks.Tasks)
                if (t.TargetId == tree.Id && t.IsActive)
                    foreach (var id in t.Assignees)
                    {
                        var v = w.Villagers.First(x => x.Id == id);
                        Assert.AreEqual(w.Nav.RegionNear(tree.X, tree.Z, 3), w.Nav.RegionNear(v.X, v.Z, 3), "only villagers that can reach the tree hold its task");
                    }
            Assert.Greater(w.Stats.WoodDelivered, 0, "the rest of the village keeps working");
            Assert.AreEqual(0, w.Tasks.ValidateReservations(w));
        }

        // ------------------------------------------------------------------ summon

        [Test]
        public void Summon_AddsVillagers_AndRejectsWater()
        {
            var r = NewRunner(out var w);
            int p0 = w.Population;
            var ok = r.ApplyImmediate(new SummonCommand { CommandId = 1, PlayerId = 1, X = w.Camp.X + 15f, Z = w.Camp.Z + 10f, Count = 3 });
            Assert.IsTrue(ok.Accepted, ok.Reason);
            Assert.AreEqual(p0 + 3, w.Population);
            var s = w.Rivers[0].Samples[w.Rivers[0].Samples.Count / 3];
            var bad = r.ApplyImmediate(new SummonCommand { CommandId = 2, PlayerId = 1, X = s.X, Z = s.Z, Count = 2 });
            Assert.IsFalse(bad.Accepted, "cannot drop villagers into the river");
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void SaveLoad_RoundTrip_HashMatches_AndKeepsRunning()
        {
            var r = NewRunner(out var w);
            r.Run(Days(w, 1.3f));
            string hash = SaveGame.ComputeStateHash(w);
            var bytes = SaveGame.Serialize(w);
            var loaded = SaveGame.Deserialize(bytes, SimConfig.CreateDefault(), out var hdr);
            Assert.AreEqual(SaveGame.SchemaVersion, hdr.SchemaVersion);
            Assert.AreEqual(w.Tick, hdr.Tick);
            Assert.AreEqual(hash, SaveGame.ComputeStateHash(loaded), "state hash identical across save/load");
            Assert.AreEqual(w.Buildings.Count, loaded.Buildings.Count);
            Assert.AreEqual(w.Villagers.Count, loaded.Villagers.Count);
            Assert.AreEqual(w.Events.Story.Count, loaded.Events.Story.Count, "the story feed survives");

            var r2 = new SimulationRunner(loaded);
            long delivered = loaded.Stats.WoodDelivered;
            r2.Run(Minutes(loaded, 4));
            AssertConservation(loaded, "after load");
            Assert.Greater(loaded.Stats.WoodDelivered + loaded.Stats.FoodHarvested + loaded.Stats.WaterFetched, delivered, "work continues after load");
        }

        [Test]
        public void SaveLoad_IsBitDeterministic_AfterLoad()
        {
            var r = NewRunner(out var w);
            r.Run(Minutes(w, 5));
            var loaded = SaveGame.Deserialize(SaveGame.Serialize(w), SimConfig.CreateDefault(), out _);
            var r2 = new SimulationRunner(loaded);
            r.Run(Minutes(w, 3));
            r2.Run(Minutes(loaded, 3));
            Assert.AreEqual(SaveGame.ComputeStateHash(w), SaveGame.ComputeStateHash(loaded), "continuing from a save gives the same future");
        }

        [Test]
        public void SaveFile_AtomicWrite_BackupRecovery_AndHeaderPeek()
        {
            var w = WorldGenerator.Generate(SimConfig.CreateDefault(), Seed);
            string dir = Path.Combine(Path.GetTempPath(), "gv_test_" + System.Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "slot0.gvsave");
            try
            {
                SaveGame.WriteFile(path, w);
                var peek = SaveGame.PeekHeader(path);
                Assert.IsNotNull(peek);
                Assert.AreEqual(w.Population, peek.Population);
                new SimulationRunner(w).Run(50);
                SaveGame.WriteFile(path, w);
                Assert.IsTrue(File.Exists(path + ".bak"));
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                var loaded = SaveGame.ReadFile(path, SimConfig.CreateDefault(), out _, out bool usedBackup);
                Assert.IsTrue(usedBackup);
                Assert.AreEqual(0, loaded.Tick);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------------ stage C: unattended 30 minutes

        [Test]
        public void StageC_ThirtyMinutesUnattended_VillageIsSustainableAndGrows()
        {
            var r = NewRunner(out var w);
            int ticks = w.Config.SecondsToTicks(30 * 60f);
            int minFood = int.MaxValue;
            for (int i = 0; i < ticks; i++)
            {
                r.Step();
                if (i % 600 == 0) minFood = System.Math.Min(minFood, w.Stored(ItemType.Food));
            }
            AssertConservation(w, "after 30 minutes");
            Assert.GreaterOrEqual(w.Population, w.Config.StartingVillagers, "nobody left or died");
            Assert.Greater(w.Population, w.Config.StartingVillagers, "the village attracted newcomers");
            Assert.GreaterOrEqual(w.CountBuildings(BuildingKind.House, false), 3);
            Assert.GreaterOrEqual(w.CountBuildings(BuildingKind.Field, false), 1);
            Assert.GreaterOrEqual(w.CountBuildings(BuildingKind.Storehouse, false), 1);
            Assert.Greater(minFood, 0, "food never ran out");
            Assert.IsTrue(w.Villagers.All(v => v.Health > 0.5f), "everyone is healthy");
            Assert.AreEqual(0, w.Stats.PathFailures, "no unresolved path failures");
            Assert.Less(r.WorstStepMs, 250.0, "no pathological tick spikes");
        }
    }
}
