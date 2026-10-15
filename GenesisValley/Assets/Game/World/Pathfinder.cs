using System;
using System.Collections.Generic;

namespace GenesisValley.World
{
    public enum PathStatus
    {
        Found = 0,
        StartBlocked = 1,
        GoalBlocked = 2,
        NoRoute = 3,
        BudgetExceeded = 4,
    }

    /// <summary>
    /// The project's single path implementation (GDD §12): 8-neighbour A* on the passability grid,
    /// no corner cutting, slope-weighted cost, bounded node budget.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly PassabilityGrid _grid;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _search;
        private readonly MinHeap _open = new MinHeap();

        public int NodeBudget = 40000;
        public int LastExpanded { get; private set; }

        public Pathfinder(PassabilityGrid grid)
        {
            _grid = grid;
            int n = grid.Width * grid.Width;
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
        }

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float Diag = 1.41421356f;

        public PathStatus FindPath(GridCell start, GridCell goal, List<GridCell> result)
        {
            result.Clear();
            LastExpanded = 0;
            if (!_grid.IsPassable(start)) return PathStatus.StartBlocked;
            if (!_grid.IsPassable(goal)) return PathStatus.GoalBlocked;
            int w = _grid.Width;
            if (start.Equals(goal)) { result.Add(goal); return PathStatus.Found; }

            _search++;
            if (_search == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _search = 1; }
            _open.Clear();
            int s = start.Z * w + start.X, gi = goal.Z * w + goal.X;
            Touch(s); _g[s] = 0; _parent[s] = -1;
            _hScale = _grid.MinCost;
            _open.Push(s, Heuristic(start.X, start.Z, goal));

            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closed[cur]) continue;
                _closed[cur] = true;
                if (cur == gi) { Reconstruct(cur, result); return PathStatus.Found; }
                if (++LastExpanded > NodeBudget) return PathStatus.BudgetExceeded;

                int cx = cur % w, cz = cur / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (!_grid.IsPassable(nx, nz)) continue;
                    if (k >= 4 && (!_grid.IsPassable(cx + DX[k], cz) || !_grid.IsPassable(cx, cz + DZ[k]))) continue;
                    int ni = nz * w + nx;
                    Touch(ni);
                    if (_closed[ni]) continue;
                    float step = (k >= 4 ? Diag : 1f) * 0.5f * (_grid.Cost(cx, cz) + _grid.Cost(nx, nz));
                    float ng = _g[cur] + step;
                    if (ng < _g[ni])
                    {
                        _g[ni] = ng; _parent[ni] = cur;
                        _open.Push(ni, ng + Heuristic(nx, nz, goal));
                    }
                }
            }
            return PathStatus.NoRoute;
        }

        /// <summary>
        /// Greedy string-pulling: drops intermediate cells while a straight walk stays on walkable cells of similar cost,
        /// so villagers walk natural lines instead of grid zig-zags (roads still win because cost bands are kept).
        /// </summary>
        public void SmoothPath(List<GridCell> path)
        {
            if (path.Count < 3) return;
            _smooth.Clear();
            _smooth.Add(path[0]);
            int anchor = 0;
            for (int i = 2; i < path.Count; i++)
            {
                if (!LineWalkable(path[anchor], path[i]))
                {
                    _smooth.Add(path[i - 1]);
                    anchor = i - 1;
                }
            }
            _smooth.Add(path[path.Count - 1]);
            path.Clear();
            path.AddRange(_smooth);
        }

        private readonly List<GridCell> _smooth = new List<GridCell>();

        private bool LineWalkable(GridCell a, GridCell b)
        {
            float ax = a.X + 0.5f, az = a.Z + 0.5f, bx = b.X + 0.5f, bz = b.Z + 0.5f;
            float dx = bx - ax, dz = bz - az;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > 24f) return false;
            int steps = (int)Math.Ceiling(len / 0.35f);
            float c0 = _grid.Cost(a.X, a.Z);
            float px = -dz / Math.Max(len, 1e-4f) * 0.35f, pz = dx / Math.Max(len, 1e-4f) * 0.35f;
            for (int k = 1; k < steps; k++)
            {
                float t = k / (float)steps;
                float x = ax + dx * t, z = az + dz * t;
                for (int side = -1; side <= 1; side++)
                {
                    int cx = (int)Math.Floor(x + px * side), cz = (int)Math.Floor(z + pz * side);
                    if (!_grid.IsPassable(cx, cz)) return false;
                    if (side == 0 && Math.Abs(_grid.Cost(cx, cz) - c0) > 0.45f) return false;
                }
            }
            return true;
        }

        private void Touch(int i)
        {
            if (_stamp[i] == _search) return;
            _stamp[i] = _search; _g[i] = float.MaxValue; _closed[i] = false; _parent[i] = -1;
        }

        private float _hScale = 1f;

        /// <summary>Octile distance scaled by the cheapest possible cell cost (roads), so A* stays admissible.</summary>
        private float Heuristic(int x, int z, GridCell goal)
        {
            int dx = Math.Abs(x - goal.X), dz = Math.Abs(z - goal.Z);
            return ((dx + dz) + (Diag - 2f) * Math.Min(dx, dz)) * _hScale;
        }

        private void Reconstruct(int end, List<GridCell> result)
        {
            int w = _grid.Width;
            for (int c = end; c != -1; c = _parent[c]) result.Add(new GridCell(c % w, c / w));
            result.Reverse();
        }

        private sealed class MinHeap
        {
            private int[] _items = new int[1024];
            private float[] _keys = new float[1024];
            public int Count { get; private set; }
            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length) { Array.Resize(ref _items, Count * 2); Array.Resize(ref _keys, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p]; _keys[i] = _keys[p]; i = p;
                }
                _items[i] = item; _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int last = _items[--Count]; float lk = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= Count) break;
                    int c = (l + 1 < Count && _keys[l + 1] < _keys[l]) ? l + 1 : l;
                    if (_keys[c] >= lk) break;
                    _items[i] = _items[c]; _keys[i] = _keys[c]; i = c;
                }
                if (Count > 0) { _items[i] = last; _keys[i] = lk; }
                return top;
            }
        }
    }
}
