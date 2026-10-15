using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.UI
{
    /// <summary>Shared text/icon helpers so the HUD, inspector and chronicle describe things the same way.</summary>
    public static class HudUtil
    {
        public static float ClockSeconds(WorldState w, long tick) => (tick + w.TickOffset) * w.Config.TickSeconds;
        public static int DayOf(WorldState w, long tick) => (int)(ClockSeconds(w, tick) / w.Config.SecondsPerGameDay) + 1;

        public static string ClockOf(WorldState w, long tick)
        {
            float frac = (ClockSeconds(w, tick) % w.Config.SecondsPerGameDay) / w.Config.SecondsPerGameDay;
            float h = frac * 24f; int hh = (int)h; int mm = (int)((h - hh) * 60f);
            return hh.ToString("00") + ":" + mm.ToString("00");
        }

        public static string When(WorldState w, long tick) => "第 " + DayOf(w, tick) + " 天 " + ClockOf(w, tick);

        public static string Ago(WorldState w, long tick)
        {
            float s = (w.Tick - tick) * w.Config.TickSeconds;
            float gameMinutes = s / w.Config.SecondsPerGameDay * 24f * 60f;
            if (gameMinutes < 1f) return "刚刚";
            if (gameMinutes < 60f) return Mathf.RoundToInt(gameMinutes) + " 分钟前";
            float hours = gameMinutes / 60f;
            if (hours < 24f) return Mathf.RoundToInt(hours) + " 小时前";
            return Mathf.RoundToInt(hours / 24f) + " 天前";
        }

        public static string PartOfDay(float hour)
        {
            if (hour < 5f) return "深夜";
            if (hour < 7f) return "清晨";
            if (hour < 11f) return "上午";
            if (hour < 13f) return "正午";
            if (hour < 17f) return "下午";
            if (hour < 19.5f) return "黄昏";
            if (hour < 22f) return "夜晚";
            return "深夜";
        }

        public static string EventIcon(SimEventType t)
        {
            switch (t)
            {
                case SimEventType.TreeFelled: return "Axe";
                case SimEventType.WoodDelivered: return "Wood";
                case SimEventType.BuildingCompleted: return "House";
                case SimEventType.SitePlanned:
                case SimEventType.SiteStageStarted: return "Hammer";
                case SimEventType.SiteWaiting: return "Warning";
                case SimEventType.SiteCancelled: return "Close";
                case SimEventType.FieldSown:
                case SimEventType.FieldHarvested:
                case SimEventType.VillagerAte: return "Food";
                case SimEventType.FishCaught: return "Fish";
                case SimEventType.WaterFetched: return "Water";
                case SimEventType.RiverCreated: return "River";
                case SimEventType.ForestPlanted:
                case SimEventType.SaplingMatured: return "Forest";
                case SimEventType.VillagersArrived: return "Population";
                case SimEventType.VillagerMovedIn: return "Bed";
                case SimEventType.VillagerSlept: return "Sleep";
                case SimEventType.VillagerWoke: return "Sun";
                case SimEventType.Milestone: return "Star";
                case SimEventType.FoodShortage:
                case SimEventType.WaterShortage:
                case SimEventType.VillagerWeak: return "Warning";
                case SimEventType.VillagerRecovered: return "Heart";
                case SimEventType.BridgePlanned:
                case SimEventType.RegionConnected: return "Bridge";
                case SimEventType.Hint: return "Info";
                case SimEventType.Saved: return "Save";
                case SimEventType.UndoApplied: return "Undo";
                case SimEventType.TerrainEdited: return "Raise";
            }
            return "Scroll";
        }

        public static Color EventColor(SimEvent e)
        {
            if (e.Importance == EventImportance.Warning) return UIStyle.Warn;
            if (e.Importance == EventImportance.Milestone) return UIStyle.Accent;
            switch (e.Type)
            {
                case SimEventType.RiverCreated:
                case SimEventType.WaterFetched:
                case SimEventType.FishCaught: return UIStyle.Water;
                case SimEventType.ForestPlanted:
                case SimEventType.SaplingMatured:
                case SimEventType.FieldHarvested:
                case SimEventType.VillagerRecovered: return UIStyle.Good;
            }
            return UIStyle.Text;
        }

        public static string RoleName(JobRole r)
        {
            switch (r)
            {
                case JobRole.Woodcutter: return "伐木";
                case JobRole.Builder: return "建造";
                case JobRole.Farmer: return "农耕";
                case JobRole.Fisher: return "捕鱼";
                case JobRole.WaterCarrier: return "挑水";
            }
            return "杂务";
        }

        public static string ItemName(ItemType t) => t == ItemType.Food ? "食物" : t == ItemType.Wood ? "木材" : "清水";
        public static string ItemIcon(ItemType t) => t == ItemType.Food ? "Food" : t == ItemType.Wood ? "Wood" : "Water";

        public static string SpeciesName(TreeSpecies s) => s == TreeSpecies.Pine ? "松树" : s == TreeSpecies.Twisted ? "虬枝老树" : "阔叶树";

        public static string KindIcon(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.House: return "House";
                case BuildingKind.Storehouse: return "Storage";
                case BuildingKind.Field: return "Food";
                case BuildingKind.FishingHut: return "Fish";
                case BuildingKind.Bridge: return "Bridge";
            }
            return "House";
        }

        /// <summary>Plain sentence for what a villager is doing right now (inspector + hover tip).</summary>
        public static string Doing(WorldState w, Villager v)
        {
            if (v.InsideHome && v.State == VillagerState.Sleeping) return "在家中熟睡";
            switch (v.State)
            {
                case VillagerState.Sleeping: return v.HomeId.IsValid ? "睡觉" : "在营地露宿";
                case VillagerState.Walking:
                {
                    string where = TargetName(w, v.TargetId);
                    string p = VillagerBrain.PurposeText(v.Purpose);
                    return string.IsNullOrEmpty(where) || v.Purpose == WalkPurpose.Wander || v.Purpose == WalkPurpose.Social ? p : p + " · " + where;
                }
            }
            switch (v.Action)
            {
                case VillagerAction.Chop: return "砍树";
                case VillagerAction.Build:
                {
                    var b = w.FindBuilding(v.TargetId);
                    return b != null ? "建造" + WorldState.KindName(b.Kind) + "（" + ConstructionSystem.StageName(b.Kind, b.Stage) + "）" : "建造";
                }
                case VillagerAction.Sow: return "播种";
                case VillagerAction.Harvest: return "收割庄稼";
                case VillagerAction.Fish: return "钓鱼";
                case VillagerAction.DrawWater: return "打水";
                case VillagerAction.PickUp: return "领取物资";
                case VillagerAction.DropOff: return "放下物资";
                case VillagerAction.Eat: return "吃饭";
                case VillagerAction.Drink: return "喝水";
                case VillagerAction.Sleep: return "睡觉";
                case VillagerAction.Sit: return "在篝火边歇息";
                case VillagerAction.Wander: return "四处走走";
                case VillagerAction.Wait: return "等待";
            }
            return "发呆";
        }

        public static string TargetName(WorldState w, EntityId id)
        {
            if (!id.IsValid) return null;
            var b = w.FindBuilding(id); if (b != null) return (b.IsComplete ? "" : "工地·") + WorldState.KindName(b.Kind);
            if (w.FindTree(id) != null) return "树林";
            var s = w.FindStorage(id); if (s != null) return s.Kind == StorageKind.Camp ? "营地" : "仓库";
            if (w.FindPile(id) != null) return "木材堆";
            var v = w.FindVillager(id); if (v != null) return v.Name;
            return null;
        }

        /// <summary>Short one-word mood derived from needs, never invented.</summary>
        public static string Mood(Villager v, out Color c)
        {
            if (v.Weak || v.Health < 0.35f) { c = UIStyle.Bad; return "虚弱"; }
            if (v.Hunger >= 1.5f) { c = UIStyle.Warn; return "饥饿"; }
            if (v.Thirst >= 1.5f) { c = UIStyle.Warn; return "口渴"; }
            if (v.Energy < 0.2f) { c = UIStyle.Warn; return "疲惫"; }
            if (!v.HomeId.IsValid) { c = UIStyle.TextDim; return "盼着有个家"; }
            c = UIStyle.Good; return "安好";
        }

        public static string Fmt(float v) => v >= 99f ? "∞" : v >= 10f ? v.ToString("0") : v.ToString("0.0");
    }
}
