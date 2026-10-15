using System;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>Deterministic starting valley: soft rolling floor, ridge rim at the map edge, camp at centre, grouped forests.</summary>
    public static class WorldGenerator
    {
        public static WorldState Generate(SimConfig config, ulong seed) => Generate(config, seed, true);

        /// <param name="withSettlement">false = empty valley (tutorial/creative start: the player summons the camp).</param>
        public static WorldState Generate(SimConfig config, ulong seed, bool withSettlement)
        {
            var w = new WorldState(config, seed);
            GenerateHeights(w);
            w.Nav.RebuildAll(w.Height);

            float s = config.WorldSize / 500f;
            float cx = 240f * s, cz = 262f * s;

            // A spring-fed stream from the north-east ridge into a pond south-west of the camp (GDD §3: exploration seed with a creek).
            var stream = new GenesisValley.Core.RiverCommand { Width = 5.5f, CommandId = 1 };
            float[] pts = { 362, 452, 334, 392, 306, 330, 286, 268, 266, 214, 236, 176 };
            for (int i = 0; i < pts.Length; i++) stream.Points.Add(pts[i] * s);
            var processor = new CommandProcessor(w);
            var plan = processor.PreviewRiver(stream.Points, stream.Width);
            if (plan.Valid)
            {
                var river = new RiverData { Id = 1, Width = plan.Width, Depth = plan.Depth, PondRadius = plan.PondRadius * 1.8f, Seed = 1 };
                river.ControlPoints.AddRange(stream.Points);
                river.Samples.AddRange(plan.Samples);
                processor.AddRiver(river, null, out _, out _);
            }

            FlattenAround(w, cx, cz, 12f);
            w.Nav.RebuildAll(w.Height);
            foreach (var r in w.Rivers) RiverBuilder.MarkWater(w.Nav, r, true);

            if (withSettlement)
            {
                var camp = new StorageEntity { Id = w.Ids.Next(), Kind = StorageKind.Camp, X = cx, Z = cz };
                camp.Inventory = new Inventory(camp.Id, config.CampCapacity);
                InventorySystem.Seed(camp.Inventory, ItemType.Food, config.CampFood, w.Stats);
                InventorySystem.Seed(camp.Inventory, ItemType.Wood, config.CampWood, w.Stats);
                InventorySystem.Seed(camp.Inventory, ItemType.Water, config.CampWater, w.Stats);
                w.AddStorage(camp);
            }

            // Forest groups at different distances so transport distance matters (GDD §1); one sits across the stream.
            PlantForest(w, 268f * s, 300f * s, 9f, 7, TreeSpecies.Common);
            PlantForest(w, 332f * s, 252f * s, 20f, 24, TreeSpecies.Common);
            PlantForest(w, 178f * s, 318f * s, 22f, 26, TreeSpecies.Pine);
            PlantForest(w, 172f * s, 228f * s, 16f, 16, TreeSpecies.Twisted);
            PlantForest(w, 300f * s, 405f * s, 24f, 22, TreeSpecies.Pine);
            PlantForest(w, 300f * s, 150f * s, 16f, 14, TreeSpecies.Twisted);

            if (withSettlement)
            {
                var camp = w.Camp;
                for (int i = 0; i < config.StartingVillagers; i++)
                {
                    float a = (float)(i * Math.PI * 2 / config.StartingVillagers);
                    var v = PopulationSystem.Spawn(w, cx + (float)Math.Cos(a) * 4.5f, cz + (float)Math.Sin(a) * 4.5f, camp.Id);
                    v.FacingDegrees = (float)(Math.Atan2(cx - v.X, cz - v.Z) * 180.0 / Math.PI);
                }
            }

            w.Undo.Clear();
            w.Events.Clear();
            w.PresentationHeightDirty = IntRect.Empty;
            w.PresentationFullRefresh = true;
            return w;
        }

        public static void PlantForest(WorldState w, float centerX, float centerZ, float radius, int count, TreeSpecies species)
        {
            float minSpacing = w.Config.ForestMinSpacing;
            int attempts = count * 25;
            int planted = 0;
            for (int a = 0; a < attempts && planted < count; a++)
            {
                float ang = w.Rng.Range(0f, (float)Math.PI * 2f);
                float r = radius * (float)Math.Sqrt(w.Rng.NextFloat());
                float x = centerX + (float)Math.Cos(ang) * r, z = centerZ + (float)Math.Sin(ang) * r;
                if (!ForestPlanter.IsPlantable(w, x, z)) continue;
                bool ok = true;
                foreach (var t in w.Trees)
                {
                    if ((t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < minSpacing * minSpacing) { ok = false; break; }
                }
                if (!ok) continue;
                // mix in a few other species for natural variety
                var sp = species;
                float roll = w.Rng.NextFloat();
                if (roll < 0.12f) sp = species == TreeSpecies.Pine ? TreeSpecies.Common : TreeSpecies.Pine;
                var tree = ForestPlanter.CreateTree(w, x, z, sp, 1f, 0);
                tree.Scale = w.Rng.Range(0.85f, 1.25f);
                w.AddTree(tree);
                planted++;
            }
        }

        private static void GenerateHeights(WorldState w)
        {
            var h = w.Height;
            int res = h.Resolution;
            float size = h.WorldSize, half = size * 0.5f;
            var noise = new ValueNoise(w.Seed);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = x * h.Spacing, wz = z * h.Spacing;
                float dx = (wx - half) / half, dz = (wz - half) / half;
                float r = (float)Math.Sqrt(dx * dx + dz * dz);
                float rim = SmoothStep(0.62f, 1.0f, r) * 0.30f;                    // ridge that hides the edge
                float hills = noise.Fbm(wx / 90f, wz / 90f, 3) * 0.045f;              // ~4.5 m rolling hills
                float detail = noise.Fbm(wx / 25f + 31.7f, wz / 25f - 12.3f, 2) * 0.008f;
                float centreDip = (1f - SmoothStep(0f, 0.45f, r)) * -0.015f;          // gentle basin toward the camp
                h.SetNormalized(x, z, 0.20f + rim + hills + detail + centreDip);
            }
        }

        private static void FlattenAround(WorldState w, float cx, float cz, float radius)
        {
            var h = w.Height;
            float target = h.SampleHeight(cx, cz) / h.VerticalRange;
            float outer = radius * 2f;
            for (int z = 0; z < h.Resolution; z++)
            for (int x = 0; x < h.Resolution; x++)
            {
                float wx = x * h.Spacing, wz = z * h.Spacing;
                float d = (float)Math.Sqrt((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz));
                if (d >= outer) continue;
                float t = 1f - SmoothStep(radius, outer, d);
                float cur = h.GetNormalized(x, z);
                h.SetNormalized(x, z, cur + (target - cur) * t);
            }
        }

        private static float SmoothStep(float a, float b, float v)
        {
            float t = (v - a) / (b - a);
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return t * t * (3 - 2 * t);
        }

        private sealed class ValueNoise
        {
            private readonly int[] _perm = new int[512];
            public ValueNoise(ulong seed)
            {
                var rng = new DeterministicRandom(seed ^ 0xA5A5A5A5UL);
                var p = new int[256];
                for (int i = 0; i < 256; i++) p[i] = i;
                for (int i = 255; i > 0; i--) { int j = rng.Range(0, i + 1); int t = p[i]; p[i] = p[j]; p[j] = t; }
                for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
            }

            private float Lattice(int x, int z) => _perm[(_perm[x & 255] + z) & 511] / 255f * 2f - 1f;

            public float Sample(float x, float z)
            {
                int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
                float tx = x - x0, tz = z - z0;
                tx = tx * tx * (3 - 2 * tx); tz = tz * tz * (3 - 2 * tz);
                float a = Lattice(x0, z0), b = Lattice(x0 + 1, z0), c = Lattice(x0, z0 + 1), d = Lattice(x0 + 1, z0 + 1);
                float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
                return ab + (cd - ab) * tz;
            }

            public float Fbm(float x, float z, int octaves)
            {
                float sum = 0, amp = 1, norm = 0;
                for (int i = 0; i < octaves; i++) { sum += Sample(x, z) * amp; norm += amp; amp *= 0.5f; x *= 2; z *= 2; }
                return sum / norm;
            }
        }
    }
}
