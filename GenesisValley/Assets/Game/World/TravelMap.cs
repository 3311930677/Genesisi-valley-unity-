using System;
using System.Collections.Generic;

namespace GenesisValley.World
{
    /// <summary>
    /// Dijkstra travel-cost field from one source cell (the camp) over the nav grid. Recomputed lazily when the grid
    /// changes. Lets the planner rank trees, fields and water by real walking distance (a tree across the river is far
    /// until a bridge exists — GDD §1 "distance changes transport cost").
    /// </summary>
    public sealed class TravelMap
    {
        private readonly PassabilityGrid _nav;
        private readonly float[] _cost;
        private int _version = -1;
        private GridCell _source;
        private float[] _cellCost;
        private int[] _heapItem;
        private float[] _heapKey;
        private int _heapCount;

        public TravelMap(PassabilityGrid nav)
        {
            _nav = nav;
            _cost = new float[nav.CellCount];
        }

        /// <summary>Walking cost in metres from the source to the nearest walkable cell around (x,z); +inf if unreachable.</summary>
        public float CostTo(float x, float z)
        {
            var c = _nav.WorldToCell(x, z);
            float best = float.PositiveInfinity;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = c.X + dx, cz = c.Z + dz;
                if (!_nav.InBounds(cx, cz)) continue;
                float v = _cost[cz * _nav.Width + cx];
                if (v < best) best = v;
            }
            return best;
        }

        public void Ensure(float sourceX, float sourceZ)
        {
            var s = _nav.WorldToCell(sourceX, sourceZ);
            if (!_nav.FindNearestPassable(s, 6, out s)) return;
            // Pure function of the static grid (slope/blocks/bridges, not road wear) so it is identical after save/load.
            if (_version == _nav.Version && s.Equals(_source)) return;
            _version = _nav.Version; _source = s;
            Compute();
        }

        private void Compute()
        {
            int w = _nav.Width;
            int n = w * w;
            float cs = _nav.CellSize;
            if (_cellCost == null || _cellCost.Length != n) { _cellCost = new float[n]; _heapItem = new int[n * 2]; _heapKey = new float[n * 2]; }
            // Snapshot per-cell cost once (NaN = impassable) so the inner loop is pure array work.
            for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                _cellCost[i] = _nav.IsPassable(x, z) ? _nav.BaseCost(x, z) : -1f;
                _cost[i] = float.PositiveInfinity;
            }
            _heapCount = 0;
            int start = _source.Z * w + _source.X;
            _cost[start] = 0;
            Push(start, 0);
            float diag = 1.41421356f * cs * 0.5f, orth = cs * 0.5f;
            while (_heapCount > 0)
            {
                Pop(out int cur, out float key);
                if (key > _cost[cur]) continue;
                int cx = cur % w, cz = cur / w;
                float cc = _cellCost[cur];
                bool l = cx > 0 && _cellCost[cur - 1] >= 0, r = cx < w - 1 && _cellCost[cur + 1] >= 0;
                bool d = cz > 0 && _cellCost[cur - w] >= 0, u = cz < w - 1 && _cellCost[cur + w] >= 0;
                if (r) Relax(cur + 1, key + orth * (cc + _cellCost[cur + 1]));
                if (l) Relax(cur - 1, key + orth * (cc + _cellCost[cur - 1]));
                if (u) Relax(cur + w, key + orth * (cc + _cellCost[cur + w]));
                if (d) Relax(cur - w, key + orth * (cc + _cellCost[cur - w]));
                if (r && u && _cellCost[cur + w + 1] >= 0) Relax(cur + w + 1, key + diag * (cc + _cellCost[cur + w + 1]));
                if (l && u && _cellCost[cur + w - 1] >= 0) Relax(cur + w - 1, key + diag * (cc + _cellCost[cur + w - 1]));
                if (r && d && _cellCost[cur - w + 1] >= 0) Relax(cur - w + 1, key + diag * (cc + _cellCost[cur - w + 1]));
                if (l && d && _cellCost[cur - w - 1] >= 0) Relax(cur - w - 1, key + diag * (cc + _cellCost[cur - w - 1]));
            }
        }

        private void Relax(int ni, float ng)
        {
            if (ng < _cost[ni]) { _cost[ni] = ng; Push(ni, ng); }
        }

        private void Push(int item, float key)
        {
            if (_heapCount == _heapItem.Length)
            {
                Array.Resize(ref _heapItem, _heapItem.Length * 2);
                Array.Resize(ref _heapKey, _heapKey.Length * 2);
            }
            int i = _heapCount++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_heapKey[p] <= key) break;
                _heapItem[i] = _heapItem[p]; _heapKey[i] = _heapKey[p]; i = p;
            }
            _heapItem[i] = item; _heapKey[i] = key;
        }

        private void Pop(out int item, out float key)
        {
            item = _heapItem[0]; key = _heapKey[0];
            int n = --_heapCount;
            if (n == 0) return;
            int li = _heapItem[n]; float lk = _heapKey[n];
            int i = 0;
            while (true)
            {
                int c = 2 * i + 1;
                if (c >= n) break;
                if (c + 1 < n && _heapKey[c + 1] < _heapKey[c]) c++;
                if (_heapKey[c] >= lk) break;
                _heapItem[i] = _heapItem[c]; _heapKey[i] = _heapKey[c]; i = c;
            }
            _heapItem[i] = li; _heapKey[i] = lk;
        }
    }
}
