using System;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>
    /// Slow world processes (GDD §11.3): saplings mature (wood is created then, recorded), stumps rot away,
    /// mature trees occasionally seed nearby ground, fields grow, roads fade if unused.
    /// Runs once per simulated second; all randomness comes from the world RNG.
    /// </summary>
    public sealed class EcologySystem
    {

        public void Tick(WorldState w)
        {
            var c = w.Config;
            if (w.Tick % c.TicksPerSecond != 0) return;
            float dt = 1f;

            float growPerSecond = 1f / (c.SaplingGrowDays * c.SecondsPerGameDay);
            int rotTicks = c.SecondsToTicks(c.StumpRotDays * c.SecondsPerGameDay);
            for (int i = w.Trees.Count - 1; i >= 0; i--)
            {
                var t = w.Trees[i];
                if (t.IsSapling)
                {
                    float wet = w.Water.Wetness(t.X, t.Z);
                    t.Growth = Math.Min(1f, t.Growth + growPerSecond * dt * (0.75f + 0.5f * wet));
                    if (t.Growth >= 1f)
                    {
                        t.Stock.Capacity = c.WoodPerTree;
                        InventorySystem.Seed(t.Stock, ItemType.Wood, c.WoodPerTree, w.Stats);
                        w.Emit(SimEventType.SaplingMatured, EventImportance.Debug, EntityId.None, t.Id, t.X, t.Z, "一棵树苗长成了大树");
                    }
                }
                else if (t.IsStump && t.FelledTick >= 0 && w.Tick - t.FelledTick > rotTicks && !t.ReservedBy.IsValid && !w.Tasks.HasActiveTaskFor(t.Id))
                {
                    w.RemoveTree(t);
                }
            }

            ConstructionSystem.TickFields(w, dt);

            if (w.Tick >= w.EcologyNextSeedTick)
            {
                w.EcologyNextSeedTick = w.Tick + c.SecondsToTicks(c.SecondsPerGameDay / Math.Max(0.1f, c.NaturalSeedingPerDay));
                TrySeedNaturally(w);
            }

            int day = w.Day;
            if (day != w.EcologyLastDecayDay && w.Hour >= 3f && w.Hour < 4f)
            {
                w.EcologyLastDecayDay = day;
                w.Nav.DecayWear(c.RoadDailyDecay);
            }
        }

        private static void TrySeedNaturally(WorldState w)
        {
            if (w.Trees.Count == 0 || w.Trees.Count > 1500) return;
            var parent = w.Trees[w.Rng.Range(0, w.Trees.Count)];
            if (parent.IsSapling || parent.IsStump) return;
            float a = w.Rng.Range(0f, (float)Math.PI * 2f), r = w.Rng.Range(4f, 9f);
            float x = parent.X + (float)Math.Cos(a) * r, z = parent.Z + (float)Math.Sin(a) * r;
            int near = 0;
            foreach (var t in w.Trees)
            {
                float d2 = (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z);
                if (d2 < w.Config.ForestMinSpacing * w.Config.ForestMinSpacing) return;
                if (d2 < 64f) near++;
            }
            if (near > 6) return;
            if (!ForestPlanter.IsPlantable(w, x, z)) return;
            var tree = ForestPlanter.CreateTree(w, x, z, parent.Species, 0.05f, 0);
            w.AddTree(tree);
        }
    }

    /// <summary>Shared rules for placing trees (forest brush, natural seeding, generator).</summary>
    public static class ForestPlanter
    {
        public static bool IsPlantable(WorldState w, float x, float z)
        {
            var c = w.Config;
            if (x < 20 || z < 20 || x > c.WorldSize - 20 || z > c.WorldSize - 20) return false;
            if (w.Height.SampleSlopeDegrees(x, z) > 26f) return false;
            var cell = w.Nav.WorldToCell(x, z);
            if (w.Nav.GetBlock(cell.X, cell.Z) != CellBlock.None || w.Nav.IsBridge(cell.X, cell.Z)) return false;
            if (w.Nav.Roadness(cell.X, cell.Z) > 0.35f) return false;
            if (w.Water.DistanceToWater(x, z) < 3f) return false;
            var camp = w.Camp;
            if (camp != null && (camp.X - x) * (camp.X - x) + (camp.Z - z) * (camp.Z - z) < 16f * 16f) return false;
            foreach (var b in w.Buildings)
            {
                if (Math.Abs(b.X - x) > b.BoundingRadius + 4f || Math.Abs(b.Z - z) > b.BoundingRadius + 4f) continue;
                if (b.Contains(x, z, b.Kind == BuildingKind.Field ? 1.5f : 2.5f)) return false;
                if (b.Kind == BuildingKind.Bridge && BridgePlanner.SegmentDistance(x, z, x, z, b.AX, b.AZ, b.BX, b.BZ) < 4f) return false;
            }
            return true;
        }

        public static TreeEntity CreateTree(WorldState w, float x, float z, TreeSpecies species, float growth, int transaction)
        {
            var c = w.Config;
            var tree = new TreeEntity
            {
                Id = w.Ids.Next(), Species = species, Variant = w.Rng.Range(0, 5), X = x, Z = z,
                RotationDegrees = w.Rng.Range(0f, 360f), Scale = w.Rng.Range(0.85f, 1.2f), Growth = growth,
                PlantedByTransaction = transaction,
            };
            tree.Stock = new Inventory(tree.Id, c.WoodPerTree);
            if (growth >= 1f) InventorySystem.Seed(tree.Stock, ItemType.Wood, c.WoodPerTree, w.Stats);
            return tree;
        }

        /// <summary>Pines on higher/steeper ground, broadleaf near water, twisted oaks in between.</summary>
        public static TreeSpecies ChooseSpecies(WorldState w, float x, float z, float roll)
        {
            float alt = w.Height.SampleHeight(x, z) / w.Height.VerticalRange;
            float wet = w.Water.Wetness(x, z);
            if (alt > 0.3f || roll < 0.15f) return TreeSpecies.Pine;
            if (wet > 0.35f || roll > 0.7f) return TreeSpecies.Common;
            return roll < 0.45f ? TreeSpecies.Twisted : TreeSpecies.Common;
        }
    }
}
