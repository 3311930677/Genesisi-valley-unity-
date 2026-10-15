using System;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    public struct BridgePlan
    {
        public bool Valid;
        public string Reason;
        public float AX, AZ, BX, BZ, Length;
        public int Wood;
    }

    /// <summary>
    /// Bridge intent validation (GDD §4/§12): short span, both banks on walkable land, actually crosses water,
    /// at least one bank reachable by villagers. The bridge is a real navigation edge only after construction.
    /// </summary>
    public static class BridgePlanner
    {
        public static BridgePlan Plan(WorldState w, float ax, float az, float bx, float bz)
        {
            var p = new BridgePlan();
            var c = w.Config;
            if (!SnapToBank(w, ref ax, ref az) || !SnapToBank(w, ref bx, ref bz)) return Fail(p, "桥的两端都需要落在可以行走的岸上");
            float dx = bx - ax, dz = bz - az;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            p.AX = ax; p.AZ = az; p.BX = bx; p.BZ = bz; p.Length = len;
            p.Wood = (int)Math.Ceiling(len * c.BridgeWoodPerMeter);
            if (len < 3f) return Fail(p, "桥太短了");
            if (len > c.BridgeMaxSpan + 0.01f) return Fail(p, "跨度太长（最多" + (int)c.BridgeMaxSpan + "米）");

            bool crossesWater = false;
            int steps = (int)Math.Ceiling(len / 0.5f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var cell = w.Nav.WorldToCell(ax + dx * t, az + dz * t);
                if (w.Nav.IsWater(cell.X, cell.Z)) crossesWater = true;
                if (w.Nav.IsBuilding(cell.X, cell.Z)) return Fail(p, "桥会穿过建筑");
            }
            if (!crossesWater) return Fail(p, "这里没有需要跨越的水面");

            foreach (var b in w.Buildings)
            {
                if (b.Kind != BuildingKind.Bridge) continue;
                if (SegmentDistance(ax, az, bx, bz, b.AX, b.AZ, b.BX, b.BZ) < 4f) return Fail(p, "附近已经有桥了");
            }

            int ra = w.Nav.RegionNear(ax, az, 2), rb = w.Nav.RegionNear(bx, bz, 2);
            int home = w.Camp != null ? w.Nav.RegionNear(w.Camp.X, w.Camp.Z, 4) : 0;
            bool aOk = ra != 0 && ra == home, bOk = rb != 0 && rb == home;
            if (!aOk && !bOk)
            {
                foreach (var v in w.Villagers)
                {
                    int rv = w.Nav.RegionNear(v.X, v.Z, 3);
                    if (rv == ra && ra != 0) aOk = true;
                    if (rv == rb && rb != 0) bOk = true;
                }
            }
            if (!aOk && !bOk) return Fail(p, "两岸都没有村民能够到达");
            if (!aOk && bOk)
            {
                // Construction happens from the reachable bank (A).
                p.AX = bx; p.AZ = bz; p.BX = ax; p.BZ = az;
            }
            p.Valid = true;
            return p;
        }

        public static BuildingEntity PlaceSite(WorldState w, BridgePlan p)
        {
            var c = w.Config;
            float dx = p.BX - p.AX, dz = p.BZ - p.AZ;
            var b = new BuildingEntity
            {
                Id = w.Ids.Next(), Kind = BuildingKind.Bridge, X = (p.AX + p.BX) * 0.5f, Z = (p.AZ + p.BZ) * 0.5f,
                Yaw = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI), SizeX = 2.4f, SizeZ = p.Length,
                AX = p.AX, AZ = p.AZ, BX = p.BX, BZ = p.BZ,
                AY = w.Height.SampleHeight(p.AX, p.AZ), BY = w.Height.SampleHeight(p.BX, p.BZ),
                State = BuildingState.Site, CreatedTick = w.Tick, WoodRequired = p.Wood,
                WorkRequired = p.Length * c.BridgeWorkPerMeter, Variant = w.Rng.Range(0, 1000),
            };
            b.Site = new Inventory(b.Id, b.WoodRequired);
            w.AddBuilding(b);
            w.Emit(SimEventType.BridgePlanned, EntityId.None, b.Id, b.X, b.Z, "规划：一座 " + p.Length.ToString("0") + " 米的木桥（需要木材 " + p.Wood + "）");
            return b;
        }

        private static bool SnapToBank(WorldState w, ref float x, ref float z)
        {
            var cell = w.Nav.WorldToCell(x, z);
            for (int r = 0; r <= 3; r++)
            {
                float best = float.MaxValue; GridCell found = cell; bool ok = false;
                for (int oz = -r; oz <= r; oz++)
                for (int ox = -r; ox <= r; ox++)
                {
                    if (Math.Abs(ox) != r && Math.Abs(oz) != r) continue;
                    int cx = cell.X + ox, cz = cell.Z + oz;
                    if (!w.Nav.IsPassable(cx, cz) || w.Nav.IsWater(cx, cz) || w.Nav.IsBridge(cx, cz)) continue;
                    float d = ox * ox + oz * oz;
                    if (d < best) { best = d; found = new GridCell(cx, cz); ok = true; }
                }
                if (ok)
                {
                    if (r > 0) w.Nav.CellCenter(found, out x, out z);
                    return true;
                }
            }
            return false;
        }

        private static BridgePlan Fail(BridgePlan p, string reason) { p.Valid = false; p.Reason = reason; return p; }

        public static float SegmentDistance(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
        {
            float d = Math.Min(PointSeg(ax, az, cx, cz, dx, dz), PointSeg(bx, bz, cx, cz, dx, dz));
            d = Math.Min(d, Math.Min(PointSeg(cx, cz, ax, az, bx, bz), PointSeg(dx, dz, ax, az, bx, bz)));
            return d;
        }

        private static float PointSeg(float px, float pz, float ax, float az, float bx, float bz)
        {
            float ex = bx - ax, ez = bz - az, l2 = ex * ex + ez * ez;
            float t = l2 > 1e-6f ? ((px - ax) * ex + (pz - az) * ez) / l2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            float qx = ax + ex * t - px, qz = az + ez * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }
    }
}
