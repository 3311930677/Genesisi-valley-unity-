using System;
using System.Collections.Generic;

namespace GenesisValley.World
{
    public struct GridCell : IEquatable<GridCell>
    {
        public int X, Z;
        public GridCell(int x, int z) { X = x; Z = z; }
        public bool Equals(GridCell o) => X == o.X && Z == o.Z;
        public override bool Equals(object obj) => obj is GridCell o && Equals(o);
        public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
        public override string ToString() => $"[{X},{Z}]";
    }

    [Flags]
    public enum CellBlock : byte
    {
        None = 0,
        Slope = 1,
        Border = 2,
        Water = 4,
        Building = 8,
    }

    /// <summary>
    /// 2D walkability grid (~2 m cells, GDD §12). Rebuilt only inside dirty regions after terrain transactions.
    /// Water and building flags are owned by the river/building systems and survive slope rebuilds.
    /// Bridge decks are an overlay: they make water/bank cells walkable but never buildings or the map border.
    /// Traffic wear lowers movement cost so frequently used routes become roads (GDD §13).
    /// </summary>
    public sealed class PassabilityGrid
    {
        public readonly int Width;
        public readonly float CellSize;
        public readonly float MaxSlopeDegrees;
        private readonly int _border;
        private readonly CellBlock[] _block;
        private readonly float[] _slope;
        private readonly byte[] _bridge;
        private readonly float[] _wear;
        private readonly int[] _region;
        private int _regionVersion = -1;
        private readonly Queue<int> _flood = new Queue<int>();

        public float RoadWearForFullRoad = 60f;
        public float RoadSpeedBonus = 0.35f;

        /// <summary>Incremented whenever any cell's passability changes; paths/regions remember the version they used.</summary>
        public int Version { get; private set; }
        /// <summary>Incremented when road wear crosses a visual threshold (presentation repaints roads).</summary>
        public int RoadVersion { get; private set; }

        public PassabilityGrid(float worldSize, float cellSize, float maxSlopeDegrees, int borderCells)
        {
            CellSize = cellSize;
            Width = (int)Math.Ceiling(worldSize / cellSize);
            MaxSlopeDegrees = maxSlopeDegrees;
            _border = borderCells;
            int n = Width * Width;
            _block = new CellBlock[n];
            _slope = new float[n];
            _bridge = new byte[n];
            _wear = new float[n];
            _region = new int[n];
        }

        public int CellCount => Width * Width;
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Width;

        public bool IsPassable(int x, int z)
        {
            if (!InBounds(x, z)) return false;
            int i = z * Width + x;
            var b = _block[i];
            if (b == CellBlock.None) return true;
            return _bridge[i] != 0 && (b & (CellBlock.Border | CellBlock.Building)) == 0;
        }

        public bool IsPassable(GridCell c) => IsPassable(c.X, c.Z);
        public CellBlock GetBlock(int x, int z) => _block[z * Width + x];
        public float GetSlope(int x, int z) => _slope[z * Width + x];
        public bool IsWater(int x, int z) => InBounds(x, z) && (_block[z * Width + x] & CellBlock.Water) != 0;
        public bool IsBridge(int x, int z) => InBounds(x, z) && _bridge[z * Width + x] != 0;
        public bool IsBuilding(int x, int z) => InBounds(x, z) && (_block[z * Width + x] & CellBlock.Building) != 0;
        public float GetWear(int x, int z) => _wear[z * Width + x];
        public float Roadness(int x, int z) { float r = _wear[z * Width + x] / RoadWearForFullRoad; return r > 1 ? 1 : r; }

        /// <summary>Movement cost multiplier: slope slows, roads speed up (min ≈ 1 - RoadSpeedBonus).</summary>
        public float Cost(int x, int z)
        {
            int i = z * Width + x;
            if (_bridge[i] != 0) return 1f - RoadSpeedBonus;
            float c = 1f + 1.5f * (_slope[i] / MaxSlopeDegrees);
            return c * (1f - RoadSpeedBonus * Roadness(x, z));
        }

        public float MinCost => 1f - RoadSpeedBonus;

        /// <summary>Cost without road wear (slope only; bridges count as flat ground).</summary>
        public float BaseCost(int x, int z)
        {
            int i = z * Width + x;
            if (_bridge[i] != 0) return 1f;
            return 1f + 1.5f * (_slope[i] / MaxSlopeDegrees);
        }

        public GridCell WorldToCell(float wx, float wz)
            => new GridCell(Clamp((int)Math.Floor(wx / CellSize), 0, Width - 1), Clamp((int)Math.Floor(wz / CellSize), 0, Width - 1));

        public void CellCenter(GridCell c, out float wx, out float wz)
        {
            wx = (c.X + 0.5f) * CellSize;
            wz = (c.Z + 0.5f) * CellSize;
        }

        public void RebuildAll(HeightField h) => Rebuild(h, new IntRect(0, 0, Width - 1, Width - 1));

        /// <summary>Converts a dirty height-sample rect to the affected cell rect.</summary>
        public IntRect SampleRectToCells(HeightField h, IntRect samples)
        {
            if (samples.IsEmpty) return IntRect.Empty;
            float s = h.Spacing;
            return new IntRect(
                (int)Math.Floor(samples.XMin * s / CellSize) - 1, (int)Math.Floor(samples.ZMin * s / CellSize) - 1,
                (int)Math.Floor(samples.XMax * s / CellSize) + 1, (int)Math.Floor(samples.ZMax * s / CellSize) + 1).Clamp(Width, Width);
        }

        public IntRect WorldRectToCells(float minX, float minZ, float maxX, float maxZ)
            => new IntRect((int)Math.Floor(minX / CellSize), (int)Math.Floor(minZ / CellSize),
                (int)Math.Floor(maxX / CellSize), (int)Math.Floor(maxZ / CellSize)).Clamp(Width, Width);

        /// <summary>Recomputes slope/blocking for the given cell rect. Returns the rect of cells whose passability changed.</summary>
        public IntRect Rebuild(HeightField h, IntRect cells)
        {
            cells = cells.Clamp(Width, Width);
            var changed = IntRect.Empty;
            for (int z = cells.ZMin; z <= cells.ZMax; z++)
            for (int x = cells.XMin; x <= cells.XMax; x++)
            {
                int i = z * Width + x;
                bool wasPassable = IsPassable(x, z);
                float slope = MaxSlopeInCell(h, x, z);
                _slope[i] = slope;
                CellBlock b = _block[i] & (CellBlock.Water | CellBlock.Building);
                if (slope > MaxSlopeDegrees) b |= CellBlock.Slope;
                if (x < _border || z < _border || x >= Width - _border || z >= Width - _border) b |= CellBlock.Border;
                _block[i] = b;
                if (wasPassable != IsPassable(x, z)) changed = changed.Union(new IntRect(x, z, x, z));
            }
            if (!changed.IsEmpty) Version++;
            return changed;
        }

        /// <summary>Sets or clears an owned flag (Water/Building) on one cell; returns true if passability changed.</summary>
        public bool SetFlag(int x, int z, CellBlock flag, bool on)
        {
            if (!InBounds(x, z)) return false;
            int i = z * Width + x;
            bool was = IsPassable(x, z);
            if (on) _block[i] |= flag; else _block[i] &= ~flag;
            bool now = IsPassable(x, z);
            if (was != now) { Version++; return true; }
            return false;
        }

        public bool SetBridge(int x, int z, bool on)
        {
            if (!InBounds(x, z)) return false;
            int i = z * Width + x;
            bool was = IsPassable(x, z);
            _bridge[i] = (byte)(on ? Math.Min(255, _bridge[i] + 1) : Math.Max(0, _bridge[i] - 1));
            bool now = IsPassable(x, z);
            if (was != now) { Version++; return true; }
            return false;
        }

        /// <summary>Walking deposits wear; crossing the "visible road" threshold bumps RoadVersion.</summary>
        public void AddWear(int x, int z, float amount)
        {
            if (!InBounds(x, z)) return;
            int i = z * Width + x;
            float before = _wear[i];
            float after = Math.Min(RoadWearForFullRoad * 1.5f, before + amount);
            _wear[i] = after;
            if ((int)(before / (RoadWearForFullRoad * 0.25f)) != (int)(after / (RoadWearForFullRoad * 0.25f))) RoadVersion++;
        }

        public void DecayWear(float factor)
        {
            for (int i = 0; i < _wear.Length; i++) _wear[i] *= factor;
            RoadVersion++;
        }

        public float[] RawWear => _wear;
        public byte[] RawBridge => _bridge;

        /// <summary>Restores owned flags from persistence (water/building bits only; slope/border are recomputed).</summary>
        public byte GetOwnedBits(int i) => (byte)(_block[i] & (CellBlock.Water | CellBlock.Building));
        public void SetOwnedBits(int i, byte bits)
        {
            _block[i] = (_block[i] & ~(CellBlock.Water | CellBlock.Building)) | ((CellBlock)bits & (CellBlock.Water | CellBlock.Building));
            Version++;
        }

        private float MaxSlopeInCell(HeightField h, int cx, int cz)
        {
            float x0 = cx * CellSize, z0 = cz * CellSize, step = CellSize * 0.5f;
            float maxGrad = 0;
            for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 2; i++)
            {
                float hc = h.SampleHeight(x0 + i * step, z0 + j * step);
                if (i < 2) maxGrad = Math.Max(maxGrad, Math.Abs(h.SampleHeight(x0 + (i + 1) * step, z0 + j * step) - hc) / step);
                if (j < 2) maxGrad = Math.Max(maxGrad, Math.Abs(h.SampleHeight(x0 + i * step, z0 + (j + 1) * step) - hc) / step);
            }
            return (float)(Math.Atan(maxGrad) * 180.0 / Math.PI);
        }

        /// <summary>Breadth search in rings for the closest walkable cell. Returns false if none within maxRadius.</summary>
        public bool FindNearestPassable(GridCell from, int maxRadius, out GridCell result)
        {
            if (IsPassable(from)) { result = from; return true; }
            for (int r = 1; r <= maxRadius; r++)
            {
                float best = float.MaxValue; result = from; bool found = false;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                    int x = from.X + dx, z = from.Z + dz;
                    if (!IsPassable(x, z)) continue;
                    float d = dx * dx + dz * dz;
                    if (d < best) { best = d; result = new GridCell(x, z); found = true; }
                }
                if (found) return true;
            }
            result = from;
            return false;
        }

        /// <summary>Like FindNearestPassable but restricted to a connectivity region (so targets across rivers are not picked).</summary>
        public bool FindNearestInRegion(GridCell from, int region, int maxRadius, out GridCell result)
        {
            EnsureRegions();
            for (int r = 0; r <= maxRadius; r++)
            {
                float best = float.MaxValue; result = from; bool found = false;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                    int x = from.X + dx, z = from.Z + dz;
                    if (!InBounds(x, z) || _region[z * Width + x] != region || region == 0) continue;
                    float d = dx * dx + dz * dz;
                    if (d < best) { best = d; result = new GridCell(x, z); found = true; }
                }
                if (found) return true;
            }
            result = from;
            return false;
        }

        // ------------------------------------------------------------------ connectivity

        /// <summary>Connected-component id of a cell (0 = blocked). Recomputed lazily when <see cref="Version"/> changes.</summary>
        public int RegionOf(int x, int z)
        {
            if (!InBounds(x, z)) return 0;
            EnsureRegions();
            return _region[z * Width + x];
        }

        public int RegionOf(GridCell c) => RegionOf(c.X, c.Z);

        /// <summary>Region of the nearest walkable cell to a world point (targets like trees sit on walkable ground).</summary>
        public int RegionNear(float wx, float wz, int searchRadius = 3)
        {
            var c = WorldToCell(wx, wz);
            if (!FindNearestPassable(c, searchRadius, out var p)) return 0;
            return RegionOf(p);
        }

        public int RegionCount { get; private set; }

        private void EnsureRegions()
        {
            if (_regionVersion == Version) return;
            _regionVersion = Version;
            Array.Clear(_region, 0, _region.Length);
            int next = 0;
            for (int start = 0; start < _region.Length; start++)
            {
                if (_region[start] != 0) continue;
                int sx = start % Width, sz = start / Width;
                if (!IsPassable(sx, sz)) continue;
                next++;
                _region[start] = next;
                _flood.Enqueue(start);
                while (_flood.Count > 0)
                {
                    int cur = _flood.Dequeue();
                    int cx = cur % Width, cz = cur / Width;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (!IsPassable(nx, nz)) continue;
                        int ni = nz * Width + nx;
                        if (_region[ni] != 0) continue;
                        _region[ni] = next;
                        _flood.Enqueue(ni);
                    }
                }
            }
            RegionCount = next;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
