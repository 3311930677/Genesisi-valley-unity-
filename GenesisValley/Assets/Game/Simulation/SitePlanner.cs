using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    public struct SiteCandidate
    {
        public float X, Z, Yaw, Score;
        public float SpotX, SpotZ;
        public bool Valid;
    }

    /// <summary>
    /// Site selection (GDD §6): generate a limited set of candidates around the camp (a loose ring template with
    /// deterministic jitter), filter (slope, water, other buildings, trees, roads, door access), then pick the best score.
    /// Also owns land occupation: pad levelling, navigation blocking and bridge decks.
    /// </summary>
    public static class SitePlanner
    {
        public static void Footprint(WorldState w, BuildingKind k, out float sx, out float sz)
        {
            switch (k)
            {
                case BuildingKind.House: sx = 6f; sz = 8f; return;
                case BuildingKind.Storehouse: sx = 7f; sz = 9f; return;
                case BuildingKind.Field: sx = 4f; sz = 6f; return;
                case BuildingKind.FishingHut: sx = 4f; sz = 4f; return;
                default: sx = 2.4f; sz = 6f; return;
            }
        }

        public static SiteCandidate FindSite(WorldState w, BuildingKind kind)
        {
            var best = new SiteCandidate { Score = float.MinValue };
            var camp = w.Camp;
            if (camp == null) return best;
            var c = w.Config;
            Footprint(w, kind, out float sx, out float sz);
            int campRegion = w.Nav.RegionNear(camp.X, camp.Z, 4);

            if (kind == BuildingKind.FishingHut) return FindFishingSite(w, campRegion, sx, sz);

            float minR = c.SiteSearchMinRadius + (kind == BuildingKind.Field ? 6f : 0f);
            float maxR = kind == BuildingKind.Field ? c.FieldSearchMaxRadius : c.SiteSearchMaxRadius;
            int ring = 0;
            for (float r = minR; r <= maxR; r += 4f, ring++)
            {
                int steps = Math.Max(10, (int)(2 * Math.PI * r / 5f));
                for (int i = 0; i < steps; i++)
                {
                    float a = (float)(i * 2 * Math.PI / steps + ring * 0.37);
                    float jitter = Hash01(w.Seed, ring, i) * 2.4f - 1.2f;
                    float x = camp.X + (float)Math.Cos(a) * (r + jitter);
                    float z = camp.Z + (float)Math.Sin(a) * (r + jitter);
                    // Doors face the camp: a readable, cosy cluster.
                    float yaw = (float)(Math.Atan2(camp.X - x, camp.Z - z) * 180.0 / Math.PI);
                    yaw = (float)Math.Round(yaw / 15f) * 15f;
                    if (kind == BuildingKind.Field) yaw = (float)Math.Round(yaw / 45f) * 45f;
                    var cand = Evaluate(w, kind, x, z, yaw, sx, sz, campRegion, r);
                    if (cand.Valid && cand.Score > best.Score) best = cand;
                }
                // Early out: score ≤ -distance·k + bonus, so farther rings can no longer win.
                if (best.Score > float.MinValue)
                {
                    float k = kind == BuildingKind.Storehouse ? 1.5f : 1f;
                    float bonus = 0;
                    foreach (var b in w.Buildings)
                    {
                        if (kind == BuildingKind.House && b.Kind == BuildingKind.House) bonus += 6f;
                        if (kind == BuildingKind.Field && b.Kind == BuildingKind.Field) bonus += 8f;
                    }
                    if (kind == BuildingKind.Field) bonus += 95f;
                    if (best.Score > -(r + 4f - 1.3f) * k + bonus) break;
                }
            }
            return best;
        }

        private static SiteCandidate FindFishingSite(WorldState w, int campRegion, float sx, float sz)
        {
            var best = new SiteCandidate { Score = float.MinValue };
            var camp = w.Camp;
            var shore = w.Water.Shore;
            for (int i = 0; i < shore.Count; i += 2)
            {
                var cell = shore[i];
                if (w.Nav.RegionOf(cell) != campRegion) continue;
                w.Nav.CellCenter(cell, out float spotX, out float spotZ);
                float dc = (float)Math.Sqrt((spotX - camp.X) * (spotX - camp.X) + (spotZ - camp.Z) * (spotZ - camp.Z));
                if (dc > 80f || dc < 10f) continue;
                // Direction away from the water.
                FindWaterDirection(w, cell, out float wx, out float wz);
                float hx = spotX - wx * (sz * 0.5f + 1.2f), hz = spotZ - wz * (sz * 0.5f + 1.2f);
                float yaw = (float)(Math.Atan2(-wx, -wz) * 180.0 / Math.PI); // door faces inland, jetty faces the water
                var cand = Evaluate(w, BuildingKind.FishingHut, hx, hz, yaw, sx, sz, campRegion, dc);
                if (!cand.Valid) continue;
                cand.SpotX = spotX; cand.SpotZ = spotZ;
                float travel = w.Travel.CostTo(spotX, spotZ);
                if (float.IsInfinity(travel)) continue;
                cand.Score = -travel;
                if (cand.Score > best.Score) best = cand;
            }
            return best;
        }

        private static void FindWaterDirection(WorldState w, GridCell cell, out float dx, out float dz)
        {
            dx = 0; dz = 0;
            for (int k = 0; k < 8; k++)
            {
                int ox = k == 0 ? 1 : k == 1 ? -1 : k == 4 || k == 5 ? 1 : k == 6 || k == 7 ? -1 : 0;
                int oz = k == 2 ? 1 : k == 3 ? -1 : k == 4 || k == 6 ? 1 : k == 5 || k == 7 ? -1 : 0;
                if (w.Nav.IsWater(cell.X + ox, cell.Z + oz)) { dx += ox; dz += oz; }
            }
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-3f) { dx = 0; dz = 1; return; }
            dx /= l; dz /= l;
        }

        public static SiteCandidate Evaluate(WorldState w, BuildingKind kind, float x, float z, float yaw, float sx, float sz, int campRegion, float distFromCamp)
        {
            var cand = new SiteCandidate { X = x, Z = z, Yaw = yaw };
            var c = w.Config;
            float lim = c.WorldSize - 25f;
            if (x < 25f || z < 25f || x > lim || z > lim) return cand;

            var probe = new BuildingEntity { X = x, Z = z, Yaw = yaw, SizeX = sx, SizeZ = sz, Kind = kind };
            float margin = kind == BuildingKind.Field ? 1.2f : kind == BuildingKind.FishingHut ? 0.3f : 1.8f;
            float rad = probe.BoundingRadius + margin;

            // Other buildings (including sites) with clearance; never on the camp itself.
            var camp = w.Camp;
            if (camp != null && Dist(x, z, camp.X, camp.Z) < probe.BoundingRadius + c.CampProtectionRadius + 4f) return cand;
            foreach (var b in w.Buildings)
            {
                float minD = b.BoundingRadius + probe.BoundingRadius + 1.5f;
                if (Dist(b.X, b.Z, x, z) > minD) continue;
                if (b.Kind == BuildingKind.Bridge) return cand;
                if (OverlapOBB(b, probe, 1.6f)) return cand;
            }
            // Slope + height range across the pad (sampled on a small lattice).
            float lo = float.MaxValue, hi = float.MinValue, maxSlope = 0;
            for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
            {
                probe.ToWorld(i * (sx * 0.5f + 0.5f) / 2f, j * (sz * 0.5f + 0.5f) / 2f, out float px, out float pz);
                float h = w.Height.SampleHeight(px, pz);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                maxSlope = Math.Max(maxSlope, w.Height.SampleSlopeDegrees(px, pz));
            }
            bool hut = kind == BuildingKind.FishingHut;
            if (hi - lo > c.SiteMaxHeightRange * (hut ? 1.6f : 1f) || maxSlope > c.SiteMaxSlopeDegrees * (hut ? 1.7f : 1f)) return cand;

            // Navigation: no water / buildings / blocked cells in the expanded footprint; avoid established roads.
            var cells = w.Nav.WorldRectToCells(x - rad, z - rad, x + rad, z + rad);
            float roadPenalty = 0;
            for (int cz = cells.ZMin; cz <= cells.ZMax; cz++)
            for (int cx = cells.XMin; cx <= cells.XMax; cx++)
            {
                w.Nav.CellCenter(new GridCell(cx, cz), out float wx, out float wz);
                if (!probe.Contains(wx, wz, margin)) continue;
                var blk = w.Nav.GetBlock(cx, cz);
                if (blk != CellBlock.None || w.Nav.IsBridge(cx, cz)) return cand;
                roadPenalty += w.Nav.Roadness(cx, cz);
            }
            if (roadPenalty > 3f) return cand;

            foreach (var p in w.Piles) if (probe.Contains(p.X, p.Z, 1f)) return cand;
            foreach (var t in w.Trees) if (Math.Abs(t.X - x) < rad && Math.Abs(t.Z - z) < rad && probe.Contains(t.X, t.Z, 1.0f)) return cand;
            if (kind != BuildingKind.FishingHut)
            {
                foreach (var r in w.Rivers)
                {
                    RiverBuilder.Nearest(r, x, z, out float d, out _, out float hw);
                    if (d < hw + RiverBuilder.BankWidth + probe.BoundingRadius * 0.7f) return cand;
                }
            }

            // Door must open onto walkable ground connected to the camp.
            probe.DoorPoint(out float doorX, out float doorZ);
            var doorCell = w.Nav.WorldToCell(doorX, doorZ);
            if (kind != BuildingKind.Field)
            {
                if (!w.Nav.IsPassable(doorCell) || w.Nav.RegionOf(doorCell) != campRegion) return cand;
            }
            else if (w.Nav.RegionNear(x, z, 3) != campRegion) return cand;

            float travel = w.Travel.CostTo(doorX, doorZ);
            if (float.IsInfinity(travel)) return cand;
            distFromCamp = Math.Max(distFromCamp, travel * 0.8f);
            float score = -distFromCamp * 1.0f - (hi - lo) * 10f - maxSlope * 1.2f - roadPenalty * 6f;
            switch (kind)
            {
                case BuildingKind.Field:
                {
                    float wet = w.Water.Wetness(x, z);
                    float fert = WaterMap.Fertility(w.Height, x, z, c.MaxWalkableSlopeDegrees);
                    score += wet * 70f + fert * 25f;
                    // fields like company: cluster with other fields
                    foreach (var b in w.Buildings) if (b.Kind == BuildingKind.Field && Dist(b.X, b.Z, x, z) < 16f) score += 8f;
                    break;
                }
                case BuildingKind.House:
                    foreach (var b in w.Buildings) if (b.Kind == BuildingKind.House && Dist(b.X, b.Z, x, z) < 18f) score += 6f;
                    foreach (var b in w.Buildings) if (b.Kind == BuildingKind.Field && Dist(b.X, b.Z, x, z) < 14f) score -= 10f;
                    break;
                case BuildingKind.Storehouse:
                    score -= distFromCamp * 0.5f;
                    break;
            }
            cand.Score = score;
            cand.Valid = true;
            return cand;
        }

        /// <summary>Creates the site: pad levelling, nav blocking, villagers moved out, event.</summary>
        public static BuildingEntity PlaceSite(WorldState w, BuildingKind kind, SiteCandidate cand)
        {
            var c = w.Config;
            Footprint(w, kind, out float sx, out float sz);
            var b = new BuildingEntity
            {
                Id = w.Ids.Next(), Kind = kind, X = cand.X, Z = cand.Z, Yaw = cand.Yaw, SizeX = sx, SizeZ = sz,
                State = BuildingState.Site, CreatedTick = w.Tick, Variant = (int)(Hash01(w.Seed, (int)cand.X * 31, (int)cand.Z * 17) * 1000),
                SpotX = cand.SpotX, SpotZ = cand.SpotZ,
            };
            switch (kind)
            {
                case BuildingKind.House: b.WoodRequired = c.HouseWood; b.WorkRequired = c.HouseWork; break;
                case BuildingKind.Storehouse: b.WoodRequired = c.StorehouseWood; b.WorkRequired = c.StorehouseWork; break;
                case BuildingKind.Field: b.WoodRequired = c.FieldWood; b.WorkRequired = c.FieldWork; break;
                case BuildingKind.FishingHut: b.WoodRequired = c.FishingHutWood; b.WorkRequired = c.FishingHutWork; break;
            }
            b.Site = new Inventory(b.Id, b.WoodRequired);
            LevelPad(w, b);
            w.AddBuilding(b);
            ApplyFootprint(w, b, true);
            w.Emit(SimEventType.SitePlanned, EntityId.None, b.Id, b.X, b.Z, "规划：新的" + WorldState.KindName(kind) + "工地（需要木材 " + b.WoodRequired + "）");
            return b;
        }

        /// <summary>Levels the ground under a building to its mean height, blending softly into the surroundings.</summary>
        public static void LevelPad(WorldState w, BuildingEntity b)
        {
            var h = w.Height;
            float sum = 0; int n = 0;
            for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
            {
                b.ToWorld(i * b.SizeX * 0.25f, j * b.SizeZ * 0.25f, out float px, out float pz);
                sum += h.SampleHeight(px, pz); n++;
            }
            float target = sum / n;
            b.PadHeight = target;
            float blend = b.Kind == BuildingKind.Field ? 3f : 3.5f;
            float rad = b.BoundingRadius + blend + 1f;
            var rect = h.SampleRect(b.X, b.Z, rad);
            var dirty = IntRect.Empty;
            float s = h.Spacing;
            for (int z = rect.ZMin; z <= rect.ZMax; z++)
            for (int x = rect.XMin; x <= rect.XMax; x++)
            {
                float wx = x * s, wz = z * s;
                float d = b.DistanceOutside(wx, wz);
                float inner = b.Kind == BuildingKind.Field ? 0.2f : 0.8f;
                float t;
                if (d <= inner) t = 1f;
                else if (d < inner + blend) t = 1f - HeightField.SmoothStep01((d - inner) / blend);
                else continue;
                float cur = h.GetMeters(x, z);
                float next = cur + (target - cur) * t;
                if (h.SetMetersRecorded(x, z, next, null)) dirty = dirty.Union(new IntRect(x, z, x, z));
            }
            if (dirty.IsEmpty) return;
            w.PresentationHeightDirty = w.PresentationHeightDirty.Union(dirty);
            var cells = w.Nav.SampleRectToCells(h, dirty);
            w.Nav.Rebuild(h, cells);
        }

        /// <summary>Marks/clears Building flags under the footprint and relocates anyone standing there.</summary>
        public static void ApplyFootprint(WorldState w, BuildingEntity b, bool on)
        {
            if (!b.BlocksNavigation) return;
            float rad = b.BoundingRadius + 1f;
            var cells = w.Nav.WorldRectToCells(b.X - rad, b.Z - rad, b.X + rad, b.Z + rad);
            for (int cz = cells.ZMin; cz <= cells.ZMax; cz++)
            for (int cx = cells.XMin; cx <= cells.XMax; cx++)
            {
                w.Nav.CellCenter(new GridCell(cx, cz), out float wx, out float wz);
                if (!b.Contains(wx, wz, -0.2f)) continue;
                w.Nav.SetFlag(cx, cz, CellBlock.Building, on);
            }
            if (on) RelocateBlocked(w);
        }

        /// <summary>Bridge deck overlay: cells along A→B become walkable over water and banks.</summary>
        public static void ApplyBridgeDeck(WorldState w, BuildingEntity b, bool on)
        {
            float dx = b.BX - b.AX, dz = b.BZ - b.AZ;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(2, (int)Math.Ceiling(len / 0.5f));
            var seen = new HashSet<int>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = b.AX + dx * t, z = b.AZ + dz * t;
                var cell = w.Nav.WorldToCell(x, z);
                int key = cell.Z * w.Nav.Width + cell.X;
                if (seen.Add(key)) w.Nav.SetBridge(cell.X, cell.Z, on);
            }
            w.Water.MarkDirty();
        }

        public static void RelocateBlocked(WorldState w)
        {
            foreach (var v in w.Villagers)
            {
                if (v.InsideHome) continue;
                var cell = w.Nav.WorldToCell(v.X, v.Z);
                if (w.Nav.IsPassable(cell)) { if (v.IsMoving) v.NeedsRepath = true; continue; }
                if (w.Nav.FindNearestPassable(cell, 20, out var safe))
                {
                    w.Nav.CellCenter(safe, out v.X, out v.Z);
                    w.Stats.Relocations++;
                    w.Emit(SimEventType.VillagerRelocated, v.Id, EntityId.None, v.X, v.Z, v.Name + " 被安全移到附近空地");
                }
                v.NeedsRepath = true;
            }
        }

        // ------------------------------------------------------------------ helpers

        public static bool OverlapOBB(BuildingEntity a, BuildingEntity b, float margin)
        {
            // Separating axis test on the 4 axes of the two rectangles.
            return !Separated(a, b, a.FacingX, a.FacingZ, margin) && !Separated(a, b, a.FacingZ, -a.FacingX, margin)
                && !Separated(a, b, b.FacingX, b.FacingZ, margin) && !Separated(a, b, b.FacingZ, -b.FacingX, margin);
        }

        private static bool Separated(BuildingEntity a, BuildingEntity b, float ax, float az, float margin)
        {
            Project(a, ax, az, out float amin, out float amax);
            Project(b, ax, az, out float bmin, out float bmax);
            return amax + margin < bmin || bmax + margin < amin;
        }

        private static void Project(BuildingEntity r, float ax, float az, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float lx = (k & 1) == 0 ? -r.SizeX * 0.5f : r.SizeX * 0.5f;
                float lz = (k & 2) == 0 ? -r.SizeZ * 0.5f : r.SizeZ * 0.5f;
                r.ToWorld(lx, lz, out float wx, out float wz);
                float p = wx * ax + wz * az;
                min = Math.Min(min, p); max = Math.Max(max, p);
            }
        }

        public static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        public static float Hash01(ulong seed, int a, int b)
        {
            ulong h = seed ^ ((ulong)(uint)a * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)b * 0xC2B2AE3D27D4EB4FUL);
            h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL; h ^= h >> 33;
            return (h >> 40) / (float)(1UL << 24);
        }
    }
}
