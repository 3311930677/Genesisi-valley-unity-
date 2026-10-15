using System;
using System.Collections.Generic;

namespace GenesisValley.World
{
    public struct RiverSample
    {
        public float X, Z;
        /// <summary>Water surface height in metres (monotonically non-increasing along flow).</summary>
        public float Surface;
        public float HalfWidth;
        public bool Valid;
    }

    /// <summary>
    /// Authoritative river description (GDD §11.2): control points, width, depth, sampled surface, seed and version.
    /// Saved data is enough to rebuild bed carving, water flags and the surface ribbon without scene meshes.
    /// </summary>
    public sealed class RiverData
    {
        public int Id;
        public int Version = 1;
        public int Seed;
        public float Width;
        public float Depth;
        public float PondRadius;
        public readonly List<float> ControlPoints = new List<float>();
        public readonly List<RiverSample> Samples = new List<RiverSample>();

        public float Length
        {
            get
            {
                float l = 0;
                for (int i = 1; i < Samples.Count; i++)
                {
                    float dx = Samples[i].X - Samples[i - 1].X, dz = Samples[i].Z - Samples[i - 1].Z;
                    l += (float)Math.Sqrt(dx * dx + dz * dz);
                }
                return l;
            }
        }
    }

    public enum RiverIssue
    {
        None = 0,
        TooShort,
        TooLong,
        UphillCut,
        CrossesBuilding,
        CrossesCamp,
        OutOfBounds,
    }

    public sealed class RiverPlan
    {
        public readonly List<RiverSample> Samples = new List<RiverSample>();
        public bool Valid;
        public RiverIssue Issue;
        public string Reason;
        public float Width;
        public float Depth;
        public float PondRadius;
        public float Length;
        public bool Reversed;
        public IntRect SampleBounds = IntRect.Empty;
    }

    /// <summary>Answers "is this world point inside something a river may not cross?" (built houses, camp).</summary>
    public delegate RiverIssue RiverObstacleTest(float x, float z, float clearance);

    /// <summary>
    /// River geometry rules (GDD §11.2): spline sampling at ~1.5 m, surface never climbs, excavation limited,
    /// built houses cannot be crossed. Pure functions so the UI preview and the authoritative command share one path.
    /// </summary>
    public static class RiverBuilder
    {
        public const float SurfaceBelowGround = 0.35f;
        public const float BankWidth = 4.5f;
        public const float BankRise = 1.1f;

        public static RiverPlan Plan(HeightField h, IList<float> points, float width, float depth, float spacing,
            float maxCut, float maxLength, RiverObstacleTest obstacles)
        {
            var plan = new RiverPlan { Width = width, Depth = depth, PondRadius = Math.Max(5f, width * 1.3f) };
            var pts = new List<float>(points);
            if (pts.Count < 4) { Fail(plan, RiverIssue.TooShort, "河流至少需要两个点"); return plan; }

            var raw = SampleSpline(pts, spacing);
            float hw = width * 0.5f;
            float length = 0;
            for (int i = 2; i < raw.Count; i += 2)
                length += Dist(raw[i - 2], raw[i - 1], raw[i], raw[i + 1]);
            plan.Length = length;
            if (length < 12f) { Fail(plan, RiverIssue.TooShort, "河道太短（至少12米）"); }
            if (length > maxLength) { Fail(plan, RiverIssue.TooLong, "河道太长（最多" + (int)maxLength + "米）"); }

            int n = raw.Count / 2;
            var ground = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = raw[2 * i], z = raw[2 * i + 1];
                float nx, nz; Normal(raw, i, out nx, out nz);
                float g = h.SampleHeight(x, z);
                g = Math.Min(g, h.SampleHeight(x + nx * hw, z + nz * hw));
                g = Math.Min(g, h.SampleHeight(x - nx * hw, z - nz * hw));
                ground[i] = g;
            }

            // Water flows from the higher end; drawing direction does not matter to the player.
            if (ground[0] < ground[n - 1])
            {
                plan.Reversed = true;
                Array.Reverse(ground);
                var rev = new List<float>(raw.Count);
                for (int i = n - 1; i >= 0; i--) { rev.Add(raw[2 * i]); rev.Add(raw[2 * i + 1]); }
                raw = rev;
            }

            float surface = ground[0] - SurfaceBelowGround;
            float worldMax = (h.Resolution - 1) * h.Spacing;
            for (int i = 0; i < n; i++)
            {
                float x = raw[2 * i], z = raw[2 * i + 1];
                float target = ground[i] - SurfaceBelowGround;
                if (i > 0) surface = Math.Min(surface - 0.002f * spacing, target);
                var s = new RiverSample { X = x, Z = z, Surface = surface, HalfWidth = hw, Valid = true };
                float centreGround = h.SampleHeight(x, z);
                if (centreGround - (surface - depth) > maxCut)
                {
                    s.Valid = false;
                    if (plan.Issue == RiverIssue.None) Fail(plan, RiverIssue.UphillCut, "坡度过大：水不能爬坡，这一段需要开挖超过" + (int)maxCut + "米");
                }
                if (x < 8 || z < 8 || x > worldMax - 8 || z > worldMax - 8)
                {
                    s.Valid = false;
                    if (plan.Issue == RiverIssue.None) Fail(plan, RiverIssue.OutOfBounds, "河道超出山谷边界");
                }
                if (obstacles != null)
                {
                    var issue = obstacles(x, z, hw + 2f);
                    if (issue != RiverIssue.None)
                    {
                        s.Valid = false;
                        if (plan.Issue == RiverIssue.None)
                            Fail(plan, issue, issue == RiverIssue.CrossesCamp ? "不能穿过营地" : "不能穿过已建住宅");
                    }
                }
                plan.Samples.Add(s);
            }
            if (plan.Issue == RiverIssue.None) plan.Valid = true;

            float pad = hw + BankWidth + plan.PondRadius + 2f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var s in plan.Samples)
            {
                minX = Math.Min(minX, s.X); minZ = Math.Min(minZ, s.Z); maxX = Math.Max(maxX, s.X); maxZ = Math.Max(maxZ, s.Z);
            }
            float sp = h.Spacing;
            plan.SampleBounds = new IntRect((int)Math.Floor((minX - pad) / sp), (int)Math.Floor((minZ - pad) / sp),
                (int)Math.Ceiling((maxX + pad) / sp), (int)Math.Ceiling((maxZ + pad) / sp)).Clamp(h.Resolution, h.Resolution);
            return plan;
        }

        private static void Fail(RiverPlan p, RiverIssue issue, string reason)
        {
            p.Valid = false;
            if (p.Issue != RiverIssue.None) return;
            p.Issue = issue; p.Reason = reason;
        }

        /// <summary>Carves the bed, shapes banks and the end pond. Only lowers inside the channel; banks may get a small levee.</summary>
        public static IntRect Carve(HeightField h, RiverData r, HeightEditRecorder recorder)
        {
            var b = Bounds(h, r);
            var dirty = IntRect.Empty;
            float sp = h.Spacing;
            for (int z = b.ZMin; z <= b.ZMax; z++)
            for (int x = b.XMin; x <= b.XMax; x++)
            {
                float wx = x * sp, wz = z * sp;
                if (!Nearest(r, wx, wz, out float d, out float surface, out float hw)) continue;
                float cur = h.GetMeters(x, z);
                float next = cur;
                if (d < hw)
                {
                    float t = d / hw;
                    float bed = surface - r.Depth * (float)Math.Pow(Math.Max(0, 1 - t * t), 0.6);
                    next = Math.Min(cur, bed);
                }
                else if (d < hw + BankWidth)
                {
                    float t = (d - hw) / BankWidth;
                    float bank = surface + 0.05f + BankRise * HeightField.SmoothStep01(t);
                    next = Math.Min(cur, bank);
                    if (t < 0.45f && next < surface + 0.18f) next = surface + 0.18f; // keep the water inside its banks
                }
                else continue;
                if (Math.Abs(next - cur) > 1e-4f && h.SetMetersRecorded(x, z, next, recorder))
                    dirty = dirty.Union(new IntRect(x, z, x, z));
            }
            return dirty;
        }

        /// <summary>Distance from a point to the river centreline (or pond), with the interpolated surface height there.</summary>
        public static bool Nearest(RiverData r, float x, float z, out float dist, out float surface, out float halfWidth)
        {
            dist = float.MaxValue; surface = 0; halfWidth = 0;
            var s = r.Samples;
            if (s.Count == 0) return false;
            for (int i = 0; i < s.Count - 1; i++)
            {
                var a = s[i]; var c = s[i + 1];
                float ex = c.X - a.X, ez = c.Z - a.Z;
                float l2 = ex * ex + ez * ez;
                float t = l2 > 1e-6f ? ((x - a.X) * ex + (z - a.Z) * ez) / l2 : 0;
                t = t < 0 ? 0 : (t > 1 ? 1 : t);
                float px = a.X + ex * t, pz = a.Z + ez * t;
                float d = (float)Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                if (d < dist)
                {
                    dist = d;
                    surface = a.Surface + (c.Surface - a.Surface) * t;
                    halfWidth = a.HalfWidth + (c.HalfWidth - a.HalfWidth) * t;
                }
            }
            if (s.Count == 1) { dist = Dist(x, z, s[0].X, s[0].Z); surface = s[0].Surface; halfWidth = s[0].HalfWidth; }
            // The pond at the river mouth behaves like a wide round section.
            var end = s[s.Count - 1];
            if (r.PondRadius > 0)
            {
                float pd = Dist(x, z, end.X, end.Z);
                float equiv = pd - (r.PondRadius - end.HalfWidth);
                if (equiv < dist) { dist = Math.Max(0, equiv); surface = end.Surface; halfWidth = end.HalfWidth; }
            }
            return true;
        }

        public static IntRect Bounds(HeightField h, RiverData r)
        {
            float pad = r.Width * 0.5f + BankWidth + r.PondRadius + 2f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var s in r.Samples)
            {
                minX = Math.Min(minX, s.X); minZ = Math.Min(minZ, s.Z); maxX = Math.Max(maxX, s.X); maxZ = Math.Max(maxZ, s.Z);
            }
            float sp = h.Spacing;
            return new IntRect((int)Math.Floor((minX - pad) / sp), (int)Math.Floor((minZ - pad) / sp),
                (int)Math.Ceiling((maxX + pad) / sp), (int)Math.Ceiling((maxZ + pad) / sp)).Clamp(h.Resolution, h.Resolution);
        }

        /// <summary>Marks (or clears) Water flags for cells whose centre lies inside the wetted channel.</summary>
        public static IntRect MarkWater(PassabilityGrid nav, RiverData r, bool on)
        {
            float pad = r.Width * 0.5f + r.PondRadius + 2f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var s in r.Samples)
            {
                minX = Math.Min(minX, s.X); minZ = Math.Min(minZ, s.Z); maxX = Math.Max(maxX, s.X); maxZ = Math.Max(maxZ, s.Z);
            }
            var cells = nav.WorldRectToCells(minX - pad, minZ - pad, maxX + pad, maxZ + pad);
            var changed = IntRect.Empty;
            for (int z = cells.ZMin; z <= cells.ZMax; z++)
            for (int x = cells.XMin; x <= cells.XMax; x++)
            {
                nav.CellCenter(new GridCell(x, z), out float wx, out float wz);
                if (!Nearest(r, wx, wz, out float d, out _, out float hw)) continue;
                if (d > hw - 0.25f) continue;
                if (nav.SetFlag(x, z, CellBlock.Water, on)) changed = changed.Union(new IntRect(x, z, x, z));
            }
            return changed;
        }

        // ------------------------------------------------------------------ spline helpers

        /// <summary>Centripetal-ish Catmull-Rom through the control points, resampled at even arc length.</summary>
        public static List<float> SampleSpline(List<float> pts, float spacing)
        {
            int m = pts.Count / 2;
            var dense = new List<float>();
            for (int i = 0; i < m - 1; i++)
            {
                int i0 = Math.Max(0, i - 1), i3 = Math.Min(m - 1, i + 2);
                float p0x = pts[2 * i0], p0z = pts[2 * i0 + 1], p1x = pts[2 * i], p1z = pts[2 * i + 1];
                float p2x = pts[2 * (i + 1)], p2z = pts[2 * (i + 1) + 1], p3x = pts[2 * i3], p3z = pts[2 * i3 + 1];
                float segLen = Dist(p1x, p1z, p2x, p2z);
                int steps = Math.Max(2, (int)(segLen / (spacing * 0.25f)));
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    dense.Add(CatmullRom(p0x, p1x, p2x, p3x, t));
                    dense.Add(CatmullRom(p0z, p1z, p2z, p3z, t));
                }
            }
            dense.Add(pts[2 * (m - 1)]); dense.Add(pts[2 * (m - 1) + 1]);

            var outPts = new List<float> { dense[0], dense[1] };
            float acc = 0;
            for (int i = 2; i < dense.Count; i += 2)
            {
                float d = Dist(dense[i - 2], dense[i - 1], dense[i], dense[i + 1]);
                acc += d;
                if (acc >= spacing) { outPts.Add(dense[i]); outPts.Add(dense[i + 1]); acc = 0; }
            }
            float lx = dense[dense.Count - 2], lz = dense[dense.Count - 1];
            if (Dist(outPts[outPts.Count - 2], outPts[outPts.Count - 1], lx, lz) > spacing * 0.3f) { outPts.Add(lx); outPts.Add(lz); }
            return outPts;
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        private static void Normal(List<float> raw, int i, out float nx, out float nz)
        {
            int n = raw.Count / 2;
            int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
            float dx = raw[2 * b] - raw[2 * a], dz = raw[2 * b + 1] - raw[2 * a + 1];
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-5f) { nx = 1; nz = 0; return; }
            nx = -dz / l; nz = dx / l;
        }

        private static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    }
}
