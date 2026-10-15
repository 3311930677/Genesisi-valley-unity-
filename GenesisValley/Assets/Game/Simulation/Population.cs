using System;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>
    /// Arrivals (GDD §3/§4): newcomers immigrate at dawn only when there are free beds, a food surplus and water;
    /// the summon tool creates up to 10 at once within the population cap. No births in V1 (children are reserved).
    /// </summary>
    public sealed class PopulationSystem
    {
        private static readonly string[] Names =
        {
            "艾拉", "林木", "阿禾", "小溪", "石头", "青竹", "秋实", "云朵", "麦穗", "晓光", "春桃", "松子",
            "阿满", "小枫", "白露", "长生", "海棠", "铁柱", "月牙", "山杏", "冬青", "小米", "阿岚", "槐花",
            "柳絮", "大川", "芦苇", "红豆", "青禾", "竹影", "阿炭", "星河", "菱角", "木棉", "苍耳", "桑葚",
            "阿泽", "茉莉", "石榴", "南风", "小雪", "金桂", "阿柏", "荷叶", "黎明", "雨生", "野菊", "银杏",
            "阿峰", "葵子",
        };

        public void Tick(WorldState w)
        {
            var c = w.Config;
            if (w.Tick % c.TicksPerSecond != 0) return;
            int day = w.Day;
            if (day == w.LastImmigrationDay || w.Hour < 8f || w.Hour > 9f) return;
            w.LastImmigrationDay = day;
            int freeBeds = w.Beds - w.Population;
            if (freeBeds < 2 || w.FoodDays < c.ImmigrationFoodDays || w.WaterDays < 0.5f || w.Population >= c.PopulationCap) return;
            var camp = w.Camp;
            if (!FindEntrance(w, out float ex, out float ez)) return;
            int n = Math.Min(c.ImmigrantsPerArrival, Math.Min(freeBeds, c.PopulationCap - w.Population));
            string names = "";
            for (int i = 0; i < n; i++)
            {
                var v = Spawn(w, ex + i * 0.8f, ez, camp.Id);
                VillagerBrain.BeginWalk(w, v, WalkPurpose.Arrive, camp.X + w.Rng.Range(-3f, 3f), camp.Z + w.Rng.Range(-3f, 3f), camp.Id);
                names += (i > 0 ? "、" : "") + v.Name;
            }
            w.Stats.Arrivals += n;
            w.Emit(SimEventType.VillagersArrived, EntityId.None, camp.Id, ex, ez, names + " 听说山谷里有空房和余粮，迁了进来");
            w.ReachMilestone(Milestones.FirstPopulationGrowth, EntityId.None, camp.Id, camp.X, camp.Z, "村庄第一次迎来了新居民");
            ConstructionSystem.AssignHomes(w);
        }

        /// <summary>Walkable cell in the camp's region far toward the valley mouth (south), so arrivals visibly walk in.</summary>
        public static bool FindEntrance(WorldState w, out float x, out float z)
        {
            var camp = w.Camp;
            x = camp.X; z = camp.Z;
            int region = w.Nav.RegionNear(camp.X, camp.Z, 4);
            for (float d = 110f; d >= 20f; d -= 6f)
            {
                for (int k = -3; k <= 3; k++)
                {
                    float a = (float)(-Math.PI / 2 + k * 0.25);
                    float px = camp.X + (float)Math.Cos(a) * d, pz = camp.Z + (float)Math.Sin(a) * d;
                    var cell = w.Nav.WorldToCell(px, pz);
                    if (w.Nav.IsPassable(cell) && w.Nav.RegionOf(cell) == region) { w.Nav.CellCenter(cell, out x, out z); return true; }
                }
            }
            return false;
        }

        public static Villager Spawn(WorldState w, float x, float z, EntityId campId)
        {
            var c = w.Config;
            int index = w.Villagers.Count + w.Stats.Arrivals;
            string name = Names[index % Names.Length];
            bool taken = true; int suffix = 2;
            string baseName = name;
            while (taken)
            {
                taken = false;
                foreach (var o in w.Villagers) if (o.Name == name) { taken = true; break; }
                if (taken) name = baseName + suffix++;
            }
            var v = new Villager { Id = w.Ids.Next(), Name = name, CampId = campId, X = x, Z = z };
            v.Gender = IsFemaleName(baseName) ? 1 : 0;
            v.AppearanceSeed = w.Rng.Range(0, 1 << 20);
            v.Carry = new Inventory(v.Id, c.CarryCapacity);
            v.Diligence = w.Rng.Range(0.3f, 0.9f);
            v.Curiosity = w.Rng.Range(0.2f, 0.8f);
            v.Sociability = w.Rng.Range(0.2f, 0.8f);
            v.Hunger = w.Rng.Range(0f, 0.5f);
            v.Thirst = w.Rng.Range(0f, 0.5f);
            v.Energy = w.Rng.Range(0.8f, 1f);
            v.FacingDegrees = w.Rng.Range(0f, 360f);
            w.AddVillager(v);
            return v;
        }

        private static bool IsFemaleName(string n)
        {
            switch (n)
            {
                case "艾拉": case "小溪": case "云朵": case "麦穗": case "春桃": case "阿满": case "白露": case "海棠": case "月牙": case "山杏":
                case "小米": case "阿岚": case "槐花": case "柳絮": case "红豆": case "菱角": case "木棉": case "桑葚": case "茉莉": case "石榴":
                case "小雪": case "金桂": case "荷叶": case "野菊": case "银杏": case "青禾": case "葵子":
                    return true;
                default: return false;
            }
        }
    }
}
