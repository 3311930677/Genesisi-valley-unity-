using System;
using System.Collections.Generic;

namespace GenesisValley.World
{
    /// <summary>
    /// Lightweight ecology layer on the nav grid (GDD §11.3): wetness decays with distance from water,
    /// fertility depends on slope/altitude only (water improves wetness, never rock fertility).
    /// Also owns the list of shore interaction points used by water fetching and fishing.
    /// </summary>
    public sealed class WaterMap
    {
        private readonly PassabilityGrid _nav;
        private readonly float[] _distance;
        private readonly float[] _wetness;
        private readonly List<GridCell> _shore = new List<GridCell>();
        private int _builtVersion = -1;
        public float FalloffMeters = 14f;

        /// <summary>Incremented when wetness is recomputed (presentation tints the grass).</summary>
        public int Version { get; private set; }

        public WaterMap(PassabilityGrid nav)
        {
            _nav = nav;
            _distance = new float[nav.CellCount];
            _wetness = new float[nav.CellCount];
        }

        public IReadOnlyList<GridCell> Shore { get { Ensure(); return _shore; } }

        public float Wetness(float wx, float wz)
        {
            Ensure();
            var c = _nav.WorldToCell(wx, wz);
            return _wetness[c.Z * _nav.Width + c.X];
        }

        public float WetnessAt(int x, int z) { Ensure(); return _wetness[z * _nav.Width + x]; }

        public float DistanceToWater(float wx, float wz)
        {
            Ensure();
            var c = _nav.WorldToCell(wx, wz);
            return _distance[c.Z * _nav.Width + c.X];
        }

        /// <summary>Flat, low ground is fertile; steep or high rocky ground is poor. Independent of wetness.</summary>
        public static float Fertility(HeightField h, float wx, float wz, float maxWalkableSlope)
        {
            float slope = h.SampleSlopeDegrees(wx, wz);
            float alt = h.SampleHeight(wx, wz) / h.VerticalRange;
            float fs = 1f - HeightField.SmoothStep01((slope - 4f) / (maxWalkableSlope * 0.6f));
            float fa = 1f - HeightField.SmoothStep01((alt - 0.30f) / 0.12f);
            return Math.Max(0.15f, fs * fa);
        }

        public void MarkDirty() => _builtVersion = -1;

        private void Ensure()
        {
            if (_builtVersion == _nav.Version) return;
            _builtVersion = _nav.Version;
            Rebuild();
        }

        private void Rebuild()
        {
            int w = _nav.Width;
            float cs = _nav.CellSize;
            const float Inf = 1e6f;
            bool any = false;
            for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                bool water = _nav.IsWater(x, z);
                _distance[z * w + x] = water ? 0 : Inf;
                any |= water;
            }
            if (any)
            {
                // Two-pass chamfer distance transform (3-4 metric) in cell units.
                for (int z = 0; z < w; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    float d = _distance[i];
                    if (x > 0) d = Math.Min(d, _distance[i - 1] + 1f);
                    if (z > 0) d = Math.Min(d, _distance[i - w] + 1f);
                    if (x > 0 && z > 0) d = Math.Min(d, _distance[i - w - 1] + 1.4142f);
                    if (x < w - 1 && z > 0) d = Math.Min(d, _distance[i - w + 1] + 1.4142f);
                    _distance[i] = d;
                }
                for (int z = w - 1; z >= 0; z--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = z * w + x;
                    float d = _distance[i];
                    if (x < w - 1) d = Math.Min(d, _distance[i + 1] + 1f);
                    if (z < w - 1) d = Math.Min(d, _distance[i + w] + 1f);
                    if (x < w - 1 && z < w - 1) d = Math.Min(d, _distance[i + w + 1] + 1.4142f);
                    if (x > 0 && z < w - 1) d = Math.Min(d, _distance[i + w - 1] + 1.4142f);
                    _distance[i] = d;
                }
            }
            for (int i = 0; i < _distance.Length; i++)
            {
                float m = _distance[i] * cs;
                _distance[i] = m;
                _wetness[i] = any ? (float)Math.Exp(-m / FalloffMeters) : 0f;
            }

            _shore.Clear();
            for (int z = 1; z < w - 1; z++)
            for (int x = 1; x < w - 1; x++)
            {
                if (!_nav.IsPassable(x, z) || _nav.IsWater(x, z) || _nav.IsBridge(x, z)) continue;
                if (_nav.IsWater(x + 1, z) || _nav.IsWater(x - 1, z) || _nav.IsWater(x, z + 1) || _nav.IsWater(x, z - 1))
                    _shore.Add(new GridCell(x, z));
            }
            Version++;
        }

        /// <summary>Closest shore point reachable inside a region (0 = any region).</summary>
        public bool NearestShore(float wx, float wz, int region, out GridCell cell, out float distance)
        {
            Ensure();
            cell = default; distance = float.MaxValue;
            bool found = false;
            foreach (var c in _shore)
            {
                if (region != 0 && _nav.RegionOf(c) != region) continue;
                _nav.CellCenter(c, out float cx, out float cz);
                float d = (cx - wx) * (cx - wx) + (cz - wz) * (cz - wz);
                if (d < distance) { distance = d; cell = c; found = true; }
            }
            if (found) distance = (float)Math.Sqrt(distance);
            return found;
        }
    }
}
