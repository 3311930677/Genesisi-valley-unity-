using System.Collections.Generic;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Non-interactive dressing: rocks on slopes, pebbles and reeds along banks, bushes and mushrooms near woods —
    /// deterministic per world seed, instanced per (chunk, model), rebuilt only where terrain/water/buildings
    /// changed. Also builds the distant hill skirt and low-poly far forest beyond the playable square so the valley
    /// reads as a valley (GDD §4 "地图边缘以自然山脊遮挡").
    /// </summary>
    public sealed class DecorPresenter : MonoBehaviour
    {
        private sealed class Kind
        {
            public string Name;
            public string[] Models;
            public float MinScale, MaxScale;
            public bool Shadows;
            public float DrawDistance;
            public float Sink;
        }

        private sealed class Chunk
        {
            public int X, Z;
            public Bounds Bounds;
            public bool Dirty = true;
            public readonly Dictionary<string, InstancedBatch> Batches = new Dictionary<string, InstancedBatch>();
        }

        public const float ChunkSize = 125f;
        public float Density = 1f;

        private WorldState _w;
        private Chunk[] _chunks = new Chunk[0];
        private int _chunksPerSide;
        private int _navVersion = -1, _riverVersion = -1;
        private float _nextVersionCheck;
        private readonly Dictionary<string, (Mesh mesh, Material[] mats, float height)> _models = new Dictionary<string, (Mesh, Material[], float)>();
        private GameObject _skirt;
        private InstancedBatch _farTrees;
        private uint _seed;

        private static readonly Kind Rock = new Kind { Name = "rock", Models = new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" }, MinScale = 0.35f, MaxScale = 1.15f, Shadows = true, DrawDistance = 420f, Sink = 0.15f };
        private static readonly Kind Bush = new Kind { Name = "bush", Models = new[] { "Bush_Common", "Bush_Common_Flowers" }, MinScale = 0.7f, MaxScale = 1.25f, Shadows = true, DrawDistance = 300f, Sink = 0.05f };
        private static readonly Kind Pebble = new Kind { Name = "pebble", Models = new[] { "Pebble_Round_1", "Pebble_Round_2", "Pebble_Round_3", "Pebble_Square_1", "Pebble_Square_2" }, MinScale = 0.6f, MaxScale = 1.4f, Shadows = false, DrawDistance = 140f, Sink = 0.02f };
        private static readonly Kind Mushroom = new Kind { Name = "mushroom", Models = new[] { "Mushroom_Common", "Mushroom_Laetiporus" }, MinScale = 0.6f, MaxScale = 1.1f, Shadows = false, DrawDistance = 110f, Sink = 0f };
        private static readonly Kind Reed = new Kind { Name = "reed", Models = new[] { "Plant_7_Big", "Plant_7", "Plant_1_Big" }, MinScale = 0.7f, MaxScale = 1.2f, Shadows = false, DrawDistance = 200f, Sink = 0.05f };
        private static readonly Kind Log = new Kind { Name = "log", Models = new[] { "__log" }, MinScale = 0.8f, MaxScale = 1.3f, Shadows = true, DrawDistance = 220f, Sink = 0.08f };

        public void Bind(WorldState w)
        {
            _w = w;
            _seed = (uint)(w.Seed ^ (w.Seed >> 32));
            int n = Mathf.CeilToInt(w.Config.WorldSize / ChunkSize);
            _chunksPerSide = n;
            _chunks = new Chunk[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var c = new Chunk { X = x, Z = z };
                c.Bounds = new Bounds(new Vector3((x + 0.5f) * ChunkSize, w.Config.VerticalRange * 0.5f, (z + 0.5f) * ChunkSize), new Vector3(ChunkSize + 8f, w.Config.VerticalRange + 20f, ChunkSize + 8f));
                _chunks[z * n + x] = c;
            }
            _navVersion = w.Nav.Version; _riverVersion = w.RiverVersion;
            BuildSkirt();
        }

        public void MarkDirty(Rect r)
        {
            foreach (var c in _chunks)
            {
                var cr = new Rect(c.X * ChunkSize, c.Z * ChunkSize, ChunkSize, ChunkSize);
                if (cr.Overlaps(r)) c.Dirty = true;
            }
        }

        private void LateUpdate()
        {
            var gs = GameServices.Settings;
            float wanted = gs != null ? Mathf.Clamp(gs.GrassDensity, 0.2f, 1.5f) : Density;
            if (Mathf.Abs(wanted - Density) > 0.01f)
            {
                // "装饰密度" setting: rebuild the scatter at the new density.
                Density = wanted;
                foreach (var c in _chunks) c.Dirty = true;
            }
            if (_w == null) return;
            if (Time.unscaledTime > _nextVersionCheck)
            {
                _nextVersionCheck = Time.unscaledTime + 2f;
                if (_w.Nav.Version != _navVersion || _w.RiverVersion != _riverVersion)
                {
                    _navVersion = _w.Nav.Version; _riverVersion = _w.RiverVersion;
                    foreach (var c in _chunks) c.Dirty = true;
                }
            }
            // Rebuild at most one chunk per frame.
            foreach (var c in _chunks) if (c.Dirty) { Rebuild(c); break; }

            var cam = Camera.main;
            if (cam == null) return;
            var cp = cam.transform.position;
            foreach (var c in _chunks)
            {
                float d = Mathf.Sqrt(c.Bounds.SqrDistance(cp));
                foreach (var kv in c.Batches)
                {
                    var kind = KindOf(kv.Key);
                    if (d > kind.DrawDistance) continue;
                    kv.Value.Draw();
                }
            }
            _farTrees?.Draw();
        }

        private static Kind KindOf(string batchKey)
        {
            int bar = batchKey.IndexOf('|');
            string k = bar > 0 ? batchKey.Substring(0, bar) : batchKey;
            switch (k)
            {
                case "rock": return Rock;
                case "bush": return Bush;
                case "pebble": return Pebble;
                case "mushroom": return Mushroom;
                case "reed": return Reed;
                default: return Log;
            }
        }

        private bool TryModel(string key, out (Mesh mesh, Material[] mats, float height) m)
        {
            if (_models.TryGetValue(key, out m)) return m.mesh != null;
            m = (null, null, 1f);
            if (key == "__log")
            {
                m = (MeshKit.Log, new[] { MaterialLibrary.ForKey("Bark"), MaterialLibrary.ForKey("EndGrain") }, 1f);
                _models[key] = m;
                return true;
            }
            var prefab = GameServices.Art.Model(key);
            if (prefab != null)
            {
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                var mr = prefab.GetComponentInChildren<MeshRenderer>();
                if (mf != null && mr != null && mf.sharedMesh != null)
                {
                    // Bake the prefab's internal transform (FBX scale/rotation) into the instance matrix later.
                    m = (mf.sharedMesh, mr.sharedMaterials, 1f);
                }
            }
            _models[key] = m;
            return m.mesh != null;
        }

        private readonly Dictionary<string, Matrix4x4> _prefabLocal = new Dictionary<string, Matrix4x4>();

        /// <summary>Mesh-to-instance transform equivalent to instantiating the prefab (root rotation/scale + child offset).</summary>
        private Matrix4x4 PrefabLocal(string key)
        {
            if (_prefabLocal.TryGetValue(key, out var m)) return m;
            m = Matrix4x4.identity;
            var prefab = GameServices.Art.Model(key);
            var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            if (mf != null)
            {
                var root = prefab.transform;
                m = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * (root.worldToLocalMatrix * mf.transform.localToWorldMatrix);
            }
            _prefabLocal[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ scatter

        private void Rebuild(Chunk c)
        {
            c.Dirty = false;
            foreach (var b in c.Batches.Values) b.Matrices.Clear();
            float x0 = c.X * ChunkSize, z0 = c.Z * ChunkSize;
            float size = _w.Config.WorldSize;
            const float spacing = 3.2f;
            int n = Mathf.CeilToInt(ChunkSize / spacing);
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                int gx = c.X * n + ix, gz = c.Z * n + iz;
                float jx = Noise.Hash01(gx, gz, _seed + 1), jz = Noise.Hash01(gx, gz, _seed + 2);
                float x = x0 + (ix + jx) * spacing, z = z0 + (iz + jz) * spacing;
                if (x < 1f || z < 1f || x > size - 1f || z > size - 1f) continue;
                float roll = Noise.Hash01(gx, gz, _seed + 3);
                Kind kind = Choose(x, z, roll * (1f / Mathf.Max(0.2f, Density)));
                if (kind == null) continue;
                int variant = (int)(Noise.Hash(gx, gz, _seed + 4) % (uint)kind.Models.Length);
                string model = kind.Models[variant];
                if (!TryModel(model, out var m)) continue;
                string key = kind.Name + "|" + model;
                if (!c.Batches.TryGetValue(key, out var batch))
                {
                    batch = new InstancedBatch(m.mesh, m.mats) { Shadows = kind.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off };
                    c.Batches[key] = batch;
                }
                float s = Mathf.Lerp(kind.MinScale, kind.MaxScale, Noise.Hash01(gx, gz, _seed + 5));
                float y = _w.Height.SampleHeight(x, z) - kind.Sink * s;
                float yaw = Noise.Hash01(gx, gz, _seed + 6) * 360f;
                // Tilt rocks/logs to the slope a little so they sit into the hill.
                var rot = Quaternion.Euler(0, yaw, 0);
                if (kind == Rock || kind == Log)
                {
                    var nrm = Normal(x, z);
                    rot = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, nrm), 0.7f) * rot;
                }
                Matrix4x4 local = kind == Log ? Matrix4x4.TRS(Vector3.up * 0.16f * s, Quaternion.identity, new Vector3(0.32f, 0.32f, 2.4f)) : PrefabLocal(model);
                batch.Matrices.Add(Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one * s) * local);
            }
            foreach (var b in c.Batches.Values) b.MarkDirty();
        }

        private Vector3 Normal(float x, float z)
        {
            float e = 1.5f;
            float hx = _w.Height.SampleHeight(x + e, z) - _w.Height.SampleHeight(x - e, z);
            float hz = _w.Height.SampleHeight(x, z + e) - _w.Height.SampleHeight(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        private Kind Choose(float x, float z, float roll)
        {
            var nav = _w.Nav;
            var cell = nav.WorldToCell(x, z);
            if (!nav.InBounds(cell.X, cell.Z)) return null;
            if (nav.IsWater(cell.X, cell.Z) || nav.IsBuilding(cell.X, cell.Z) || nav.IsBridge(cell.X, cell.Z)) return null;
            if (nav.Roadness(cell.X, cell.Z) > 0.15f) return null;
            var camp = _w.Camp;
            if (camp != null && (camp.X - x) * (camp.X - x) + (camp.Z - z) * (camp.Z - z) < 12f * 12f) return null;
            foreach (var b in _w.Buildings) if (b.Contains(x, z, 2.5f)) return null;
            foreach (var t in _w.Trees) if ((t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < 1.8f * 1.8f) return null;

            float slope = _w.Height.SampleSlopeDegrees(x, z);
            float water = _w.Water.DistanceToWater(x, z);
            float forest = 0f;
            foreach (var t in _w.Trees)
            {
                float d2 = (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z);
                if (d2 < 64f) { forest += 1f - d2 / 64f; if (forest > 1.5f) break; }
            }
            float clump = Noise.Fbm(x * 0.03f, z * 0.03f, 2, _seed + 9);

            if (slope > 24f) return roll < 0.16f + (slope - 24f) * 0.01f ? Rock : null;
            if (water < 4f && water > 0.4f)
            {
                if (roll < 0.11f) return Pebble;
                if (roll < 0.2f) return Reed;
                return null;
            }
            if (forest > 0.3f)
            {
                if (roll < 0.05f * forest) return Mushroom;
                if (roll < 0.05f * forest + 0.06f) return Bush;
                if (roll < 0.05f * forest + 0.075f) return Log;
                return null;
            }
            if (roll < 0.012f + 0.02f * clump) return Rock;
            if (roll < 0.03f + 0.035f * clump) return Bush;
            if (roll < 0.04f + 0.035f * clump) return Pebble;
            return null;
        }

        // ------------------------------------------------------------------ horizon

        private void BuildSkirt()
        {
            if (_skirt != null) Destroy(_skirt);
            float size = _w.Config.WorldSize;
            var centre = new Vector2(size * 0.5f, size * 0.5f);
            const int seg = 192, rings = 26;
            const float outer = 1700f;
            var mb = new MeshBuilder { UseColors = true };
            var treeMatrices = new List<Matrix4x4>();
            var grass = Palette.Grass * 0.85f; grass.a = 1;
            var forest = Palette.CanopyB * 0.75f; forest.a = 1;
            var rock = Palette.Rock * 0.95f; rock.a = 1;
            var far = new Color(0.52f, 0.6f, 0.62f, 1f);
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // Ray from the centre to the square boundary.
                float tx = Mathf.Abs(dir.x) > 1e-4f ? (size * 0.5f) / Mathf.Abs(dir.x) : float.MaxValue;
                float tz = Mathf.Abs(dir.y) > 1e-4f ? (size * 0.5f) / Mathf.Abs(dir.y) : float.MaxValue;
                float tEdge = Mathf.Min(tx, tz);
                var edge = centre + dir * tEdge;
                float ex = Mathf.Clamp(edge.x, 0f, size), ez = Mathf.Clamp(edge.y, 0f, size);
                float hEdge = _w.Height.SampleHeight(ex, ez);
                float ridge = 55f + 85f * Noise.Fbm(Mathf.Cos(a) * 2.2f + 11f, Mathf.Sin(a) * 2.2f + 7f, 3, _seed + 21);
                for (int r = 0; r < rings; r++)
                {
                    float t = r / (float)(rings - 1);
                    float tt = Mathf.Pow(t, 1.7f);
                    var p2 = Vector2.Lerp(new Vector2(ex, ez) + dir * -1.5f, centre + dir * outer, tt);
                    float distOut = (p2 - new Vector2(ex, ez)).magnitude;
                    float rise = Noise.SmoothStep(0f, 260f, distOut);
                    float fall = Noise.SmoothStep(700f, 1500f, distOut);
                    float detail = (Noise.Fbm(p2.x * 0.006f, p2.y * 0.006f, 4, _seed + 31) - 0.5f) * 70f * rise;
                    float h = Mathf.Lerp(hEdge - 0.6f, hEdge + ridge + detail, rise) - fall * ridge * 0.55f;
                    if (r == 0) h = hEdge - 1.2f;
                    float hn = Mathf.Clamp01((h - hEdge) / (ridge + 40f));
                    float slopeish = Mathf.Abs(detail) / 70f;
                    var col = Color.Lerp(grass, forest, Noise.SmoothStep(0.05f, 0.3f, hn));
                    col = Color.Lerp(col, rock, Noise.SmoothStep(0.62f, 0.9f, hn + slopeish * 0.3f));
                    col = Color.Lerp(col, far, Noise.SmoothStep(500f, 1500f, distOut) * 0.6f);
                    col.a = 1f;
                    mb.Add(new Vector3(p2.x, h, p2.y), Vector3.up, new Vector2(s / (float)seg, t), col);

                    // Far forest on the lower/mid slopes.
                    if (r > 1 && r < rings - 3 && hn > 0.06f && hn < 0.62f && distOut > 25f)
                    {
                        int count = distOut < 300f ? 3 : 2;
                        for (int k = 0; k < count; k++)
                        {
                            float j1 = Noise.Hash01(s * 31 + k, r, _seed + 41), j2 = Noise.Hash01(s * 17 + k, r, _seed + 43);
                            if (Noise.Fbm(p2.x * 0.01f, p2.y * 0.01f, 2, _seed + 51) < 0.42f) continue;
                            float aa = a + (j1 - 0.5f) * (Mathf.PI * 2f / seg);
                            var pp = Vector2.Lerp(new Vector2(ex, ez), centre + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * outer, Mathf.Clamp01(tt + (j2 - 0.5f) * 0.03f));
                            float sc = Mathf.Lerp(7f, 13f, Noise.Hash01(s, r * 7 + k, _seed + 47)) * (1f + distOut / 900f);
                            treeMatrices.Add(Matrix4x4.TRS(new Vector3(pp.x, h - 1.5f, pp.y), Quaternion.Euler(0, j1 * 360f, 0), new Vector3(sc * 0.55f, sc, sc * 0.55f)));
                        }
                    }
                }
            }
            int stride = rings;
            for (int s = 0; s < seg; s++)
            for (int r = 0; r < rings - 1; r++)
            {
                int a0 = s * stride + r, b0 = (s + 1) * stride + r;
                mb.Quad(0, a0, b0, b0 + 1, a0 + 1);
            }
            var mesh = mb.ToMesh("HorizonSkirt", true);
            _skirt = new GameObject("HorizonSkirt");
            _skirt.transform.SetParent(transform, false);
            _skirt.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _skirt.AddComponent<MeshRenderer>();
            var detailTex = GameServices.Art.Layer(GroundLayer.Meadow) != null ? GameServices.Art.Layer(GroundLayer.Meadow).diffuseTexture : null;
            mr.sharedMaterial = MaterialLibrary.VertexLit(Color.white, detailTex, 0.03f, 0.3f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            _farTrees = new InstancedBatch(FarTreeMesh(), new[] { MaterialLibrary.VertexLit(Color.white) }) { Shadows = ShadowCastingMode.Off };
            _farTrees.Matrices.AddRange(treeMatrices);
            _farTrees.MarkDirty();
        }

        private static Mesh _farTree;
        private static Mesh FarTreeMesh()
        {
            if (_farTree != null) return _farTree;
            var mb = new MeshBuilder { UseColors = true };
            var trunk = new Color(0.32f, 0.24f, 0.18f, 1f);
            var leafA = new Color(0.27f, 0.38f, 0.3f, 1f);
            var leafB = new Color(0.33f, 0.45f, 0.33f, 1f);
            void ConeAt(float y0, float h, float r, Color c, int seg)
            {
                int apex = mb.Add(new Vector3(0, y0 + h, 0), Vector3.up, Vector2.zero, c * 1.15f);
                int first = mb.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(a), 0.6f, Mathf.Sin(a)).normalized;
                    mb.Add(new Vector3(Mathf.Cos(a) * r, y0, Mathf.Sin(a) * r), n, Vector2.zero, c * 0.85f);
                }
                for (int i = 0; i < seg; i++) mb.Tri(0, apex, first + i + 1, first + i);
            }
            ConeAt(0f, 0.25f, 0.06f, trunk, 5);
            ConeAt(0.15f, 0.55f, 0.42f, leafA, 7);
            ConeAt(0.42f, 0.5f, 0.32f, leafB, 7);
            ConeAt(0.66f, 0.34f, 0.2f, leafB, 6);
            return _farTree = mb.ToMesh("FarTree", false);
        }
    }
}
