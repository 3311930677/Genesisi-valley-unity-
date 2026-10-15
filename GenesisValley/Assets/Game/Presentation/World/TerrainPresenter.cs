using System.Collections.Generic;
using GenesisValley.Simulation;
using GenesisValley.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Mirrors the authoritative HeightField into a Unity Terrain (GDD §11.1) and paints it from world facts:
    /// cliffs on steep slopes, wet pebbles along water, trampled paths from traffic wear (roads emerge, GDD §13),
    /// soil under fields, leaf litter under forests, meadow/grass variation from noise. Grass, clover and flowers are
    /// terrain detail meshes (decoration only, GDD §11.3). Repaints are tiled and spread over frames.
    /// </summary>
    public sealed class TerrainPresenter : MonoBehaviour
    {
        public const int AlphaRes = 512;
        public const int DetailRes = 512;
        public const int TileTexels = 64;
        public const int TilesPerSide = AlphaRes / TileTexels;

        public Terrain Terrain { get; private set; }
        public TerrainCollider Collider { get; private set; }
        public float DetailDistance = 110f;

        private TerrainData _data;
        private WorldState _w;
        private IntRect _strokeDirty = IntRect.Empty;
        private bool _lodPending;
        private float _lastUpload;
        private readonly HashSet<int> _dirtyTiles = new HashSet<int>();
        private readonly Queue<int> _tileQueue = new Queue<int>();
        private int _layerCount;
        private readonly List<DetailSpec> _details = new List<DetailSpec>();
        private float[] _treeDensity;       // 4 m cells
        private int _treeGrid;
        private int _treeSignature;
        private float _nextTreeCheck, _nextRoadCheck;
        private float[] _roadSnapshot;
        private float[,,] _alphaTile;
        private int[][,] _detailTiles;
        private uint _seed;

        private struct DetailSpec
        {
            public string Model;
            public float MinW, MaxW, MinH, MaxH;
            public System.Func<Sample, float> Density; // instances per detail cell
        }

        /// <summary>Per-texel facts used by both splat and detail painting.</summary>
        public struct Sample
        {
            public float X, Z, Height, Slope, WaterDist, Road, Forest, Apron, Field, Camp, N1, N2, N3;
        }

        // ================================================================== binding

        public void Bind(WorldState world)
        {
            _w = world;
            _seed = (uint)(world.Seed & 0xFFFFFFFF);
            var h = world.Height;
            if (_data == null || _data.heightmapResolution != h.Resolution)
            {
                if (Terrain != null) Destroy(Terrain.gameObject);
                _data = new TerrainData { heightmapResolution = h.Resolution, alphamapResolution = AlphaRes, baseMapResolution = 1024 };
                _data.size = new Vector3(h.WorldSize, h.VerticalRange, h.WorldSize);
                _data.SetDetailResolution(DetailRes, 32);
#if UNITY_2022_2_OR_NEWER
                _data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
#endif
                _data.terrainLayers = BuildLayers();
                _layerCount = _data.terrainLayers.Length;
                BuildDetailPrototypes();

                var go = Terrain.CreateTerrainGameObject(_data);
                go.name = "Valley Terrain";
                go.layer = 0;
                go.transform.SetParent(transform, false);
                Terrain = go.GetComponent<Terrain>();
                Collider = go.GetComponent<TerrainCollider>();
                var mat = GameServices.Art.terrainMaterial;
                if (mat == null) { var rp = GraphicsSettings.currentRenderPipeline; if (rp != null) mat = rp.defaultTerrainMaterial; }
                if (mat != null) Terrain.materialTemplate = mat;
                Terrain.heightmapPixelError = 3;
                Terrain.basemapDistance = 650;
                Terrain.drawInstanced = true;
                Terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
                Terrain.detailObjectDistance = DetailDistance;
                Terrain.treeDistance = 0;
                Terrain.allowAutoConnect = false;
                Terrain.groupingID = 0;
            }
            ApplySettings();
            _alphaTile = new float[TileTexels, TileTexels, _layerCount];
            _detailTiles = new int[_details.Count][,];
            for (int i = 0; i < _details.Count; i++) _detailTiles[i] = new int[TileTexels, TileTexels];
            UploadHeightsAll();
            RebuildTreeDensity(true);
            SnapshotRoads();
            _dirtyTiles.Clear(); _tileQueue.Clear();
            for (int t = 0; t < TilesPerSide * TilesPerSide; t++) PaintTile(t);
            _w.PresentationHeightDirty = IntRect.Empty;
        }

        public void ApplySettings()
        {
            if (Terrain == null) return;
            var s = GameServices.Settings;
            float density = s != null ? s.GrassDensity : 1f;
            Terrain.detailObjectDensity = Mathf.Clamp01(density);
            Terrain.drawTreesAndFoliage = density > 0.01f;
            Terrain.detailObjectDistance = Mathf.Lerp(60f, DetailDistance, Mathf.Clamp01(density));
        }

        private TerrainLayer[] BuildLayers()
        {
            var art = GameServices.Art;
            var layers = new TerrainLayer[7];
            var fallbackColors = new[] { 0x6F8A48, 0x8C9A55, 0x5C4E36, 0x9C8466, 0x7F7D70, 0x857C6E, 0x5E4532 };
            var tiles = new[] { 9f, 12f, 8f, 5f, 16f, 5f, 4f };
            for (int i = 0; i < layers.Length; i++)
            {
                var l = art.Layer((GroundLayer)i);
                if (l == null)
                {
                    l = new TerrainLayer { name = ((GroundLayer)i).ToString() };
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "GV_Ground_" + i };
                    var c = Palette.Hex(fallbackColors[i]);
                    var px = new Color[16];
                    for (int k = 0; k < 16; k++) px[k] = c * (0.94f + 0.12f * Noise.Hash01(k, i));
                    tex.SetPixels(px); tex.Apply();
                    l.diffuseTexture = tex;
                    l.tileSize = new Vector2(tiles[i], tiles[i]);
                    l.smoothness = 0.05f;
                }
                layers[i] = l;
            }
            return layers;
        }

        private void BuildDetailPrototypes()
        {
            var art = GameServices.Art;
            _details.Clear();
            // Instances per ~1 m² detail cell (InstanceCountMode). Kept modest: grass reads as texture + tufts.
            _details.Add(new DetailSpec { Model = "Grass_Common_Short", MinW = 0.28f, MaxW = 0.42f, MinH = 0.22f, MaxH = 0.38f, Density = s => GrassAmount(s) * 2.2f });
            _details.Add(new DetailSpec { Model = "Grass_Wispy_Short", MinW = 0.3f, MaxW = 0.48f, MinH = 0.3f, MaxH = 0.55f, Density = s => GrassAmount(s) * (0.25f + 0.9f * Noise.SmoothStep(0.55f, 0.8f, s.N2)) + Wet(s) * 0.6f });
            _details.Add(new DetailSpec { Model = "Clover_1", MinW = 0.35f, MaxW = 0.55f, MinH = 0.3f, MaxH = 0.45f, Density = s => GrassAmount(s) * Noise.SmoothStep(0.62f, 0.8f, s.N3) * 0.8f });
            _details.Add(new DetailSpec { Model = "Flower_3_Group", MinW = 0.35f, MaxW = 0.5f, MinH = 0.32f, MaxH = 0.5f, Density = s => GrassAmount(s) * Noise.SmoothStep(0.74f, 0.9f, s.N1 * 0.5f + s.N3 * 0.5f) * 0.55f });
            _details.Add(new DetailSpec { Model = "Flower_4_Group", MinW = 0.3f, MaxW = 0.45f, MinH = 0.3f, MaxH = 0.45f, Density = s => GrassAmount(s) * Noise.SmoothStep(0.76f, 0.92f, s.N2 * 0.6f + (1 - s.N3) * 0.4f) * 0.45f });
            _details.Add(new DetailSpec { Model = "Plant_1", MinW = 0.45f, MaxW = 0.8f, MinH = 0.45f, MaxH = 0.8f, Density = s => (s.Forest * 0.35f + Wet(s) * 0.25f) * (1 - s.Road) * (1 - s.Apron) * Noise.SmoothStep(0.4f, 0.8f, s.N3) });
            var protos = new List<DetailPrototype>();
            var kept = new List<DetailSpec>();
            foreach (var d in _details)
            {
                var prefab = art.Model(d.Model);
                if (prefab == null) continue;
                var p = new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    useInstancing = true,
                    minWidth = d.MinW, maxWidth = d.MaxW, minHeight = d.MinH, maxHeight = d.MaxH,
                    noiseSpread = 0.4f,
                    healthyColor = Color.white,
                    dryColor = new Color(0.92f, 0.88f, 0.72f),
                };
                protos.Add(p);
                kept.Add(d);
            }
            _details.Clear();
            _details.AddRange(kept);
            _data.detailPrototypes = protos.ToArray();
        }

        private static float Wet(Sample s) => 1f - Noise.SmoothStep(2f, 7f, s.WaterDist);

        private static float GrassAmount(Sample s)
        {
            if (s.WaterDist < 1.2f) return 0f;
            float a = 1f - Noise.SmoothStep(22f, 32f, s.Slope);
            a *= 1f - Mathf.Clamp01(s.Road * 1.4f);
            a *= 1f - s.Apron;
            a *= 1f - s.Field;
            a *= 1f - s.Camp;
            a *= 1f - s.Forest * 0.55f;
            return Mathf.Clamp01(a);
        }

        // ================================================================== heights

        private void UploadHeightsAll()
        {
            var h = _w.Height;
            int n = h.Resolution;
            var arr = new float[n, n];
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) arr[z, x] = h.Heights[z * n + x];
            _data.SetHeights(0, 0, arr);
            _strokeDirty = IntRect.Empty;
            _lodPending = false;
        }

        private void UploadHeights(IntRect r, bool delayLod)
        {
            var h = _w.Height;
            int n = h.Resolution;
            int x0 = Mathf.Clamp(r.XMin, 0, n - 1), z0 = Mathf.Clamp(r.ZMin, 0, n - 1);
            int x1 = Mathf.Clamp(r.XMax, 0, n - 1), z1 = Mathf.Clamp(r.ZMax, 0, n - 1);
            int w = x1 - x0 + 1, d = z1 - z0 + 1;
            if (w <= 0 || d <= 0) return;
            var arr = new float[d, w];
            for (int z = 0; z < d; z++) for (int x = 0; x < w; x++) arr[z, x] = h.Heights[(z0 + z) * n + x0 + x];
            if (delayLod) { _data.SetHeightsDelayLOD(x0, z0, arr); _lodPending = true; }
            else _data.SetHeights(x0, z0, arr);
        }

        /// <summary>Called after a brush stroke (mouse up) or any discrete terrain change.</summary>
        public void FinishStroke()
        {
            if (_lodPending) { _data.SyncHeightmap(); _lodPending = false; }
            if (!_strokeDirty.IsEmpty) { MarkSampleRectDirty(_strokeDirty, 3); _strokeDirty = IntRect.Empty; }
        }

        public bool Stroking { get; set; }

        private void LateUpdate()
        {
            if (_w == null || _data == null) return;

            if (_w.PresentationFullRefresh)
            {
                _w.PresentationFullRefresh = false;
                _w.PresentationHeightDirty = IntRect.Empty;
                UploadHeightsAll();
                RebuildTreeDensity(true);
                for (int t = 0; t < TilesPerSide * TilesPerSide; t++) Enqueue(t);
                GameServices.Presenter?.OnTerrainRegionChanged(new Rect(0, 0, _w.Config.WorldSize, _w.Config.WorldSize));
            }
            else if (!_w.PresentationHeightDirty.IsEmpty)
            {
                var r = _w.PresentationHeightDirty;
                _w.PresentationHeightDirty = IntRect.Empty;
                UploadHeights(r, Stroking);
                _strokeDirty = _strokeDirty.Union(r);
                if (!Stroking) FinishStroke();
                float sp = _w.Height.Spacing;
                GameServices.Presenter?.OnTerrainRegionChanged(Rect.MinMaxRect(r.XMin * sp - 2, r.ZMin * sp - 2, r.XMax * sp + 2, r.ZMax * sp + 2));
            }

            float now = Time.unscaledTime;
            if (now >= _nextTreeCheck) { _nextTreeCheck = now + 6f; RebuildTreeDensity(false); }
            if (now >= _nextRoadCheck) { _nextRoadCheck = now + 4f; CheckRoads(); }

            // Spread tile repaints over frames (~1-2 ms each).
            int budget = Stroking ? 1 : 2;
            while (budget-- > 0 && _tileQueue.Count > 0)
            {
                int t = _tileQueue.Dequeue();
                _dirtyTiles.Remove(t);
                PaintTile(t);
            }
        }

        // ================================================================== dirty tracking

        private void Enqueue(int tile)
        {
            if (_dirtyTiles.Add(tile)) _tileQueue.Enqueue(tile);
        }

        /// <summary>Marks the terrain paint dirty in a world-space rectangle (buildings placed, fields sown …).</summary>
        public void MarkWorldRectDirty(Rect r)
        {
            float k = AlphaRes / _w.Config.WorldSize;
            int x0 = Mathf.Clamp((int)(r.xMin * k) / TileTexels, 0, TilesPerSide - 1);
            int x1 = Mathf.Clamp((int)(r.xMax * k) / TileTexels, 0, TilesPerSide - 1);
            int z0 = Mathf.Clamp((int)(r.yMin * k) / TileTexels, 0, TilesPerSide - 1);
            int z1 = Mathf.Clamp((int)(r.yMax * k) / TileTexels, 0, TilesPerSide - 1);
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) Enqueue(z * TilesPerSide + x);
        }

        private void MarkSampleRectDirty(IntRect r, int padSamples)
        {
            float sp = _w.Height.Spacing;
            MarkWorldRectDirty(Rect.MinMaxRect((r.XMin - padSamples) * sp, (r.ZMin - padSamples) * sp, (r.XMax + padSamples) * sp, (r.ZMax + padSamples) * sp));
        }

        private void SnapshotRoads()
        {
            var nav = _w.Nav;
            if (_roadSnapshot == null || _roadSnapshot.Length != nav.CellCount) _roadSnapshot = new float[nav.CellCount];
            for (int z = 0; z < nav.Width; z++) for (int x = 0; x < nav.Width; x++) _roadSnapshot[z * nav.Width + x] = nav.Roadness(x, z);
        }

        private void CheckRoads()
        {
            var nav = _w.Nav;
            if (_roadSnapshot == null || _roadSnapshot.Length != nav.CellCount) { SnapshotRoads(); return; }
            float cs = nav.CellSize;
            for (int z = 0; z < nav.Width; z++)
            for (int x = 0; x < nav.Width; x++)
            {
                int i = z * nav.Width + x;
                float r = nav.Roadness(x, z);
                if (Mathf.Abs(r - _roadSnapshot[i]) > 0.08f)
                {
                    _roadSnapshot[i] = r;
                    MarkWorldRectDirty(new Rect(x * cs - 1, z * cs - 1, cs + 2, cs + 2));
                }
            }
        }

        private void RebuildTreeDensity(bool force)
        {
            const float cell = 4f;
            int n = Mathf.CeilToInt(_w.Config.WorldSize / cell);
            int sig = _w.Trees.Count * 7919;
            foreach (var t in _w.Trees) if (t.Harvestable) sig = unchecked(sig * 31 + t.Id.Value);
            if (!force && sig == _treeSignature && _treeDensity != null) return;
            _treeSignature = sig;
            var raw = new float[n * n];
            foreach (var t in _w.Trees)
            {
                if (t.IsStump) continue;
                int cx = Mathf.Clamp((int)(t.X / cell), 0, n - 1), cz = Mathf.Clamp((int)(t.Z / cell), 0, n - 1);
                raw[cz * n + cx] += t.IsSapling ? 0.3f : 1f;
            }
            var blurred = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float s = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Mathf.Clamp(x + dx, 0, n - 1), zz = Mathf.Clamp(z + dz, 0, n - 1);
                    s += raw[zz * n + xx] * (dx == 0 && dz == 0 ? 0.36f : 0.08f);
                }
                blurred[z * n + x] = Mathf.Clamp01(s * 1.4f);
            }
            if (!force && _treeDensity != null && _treeDensity.Length == blurred.Length)
            {
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (Mathf.Abs(blurred[z * n + x] - _treeDensity[z * n + x]) > 0.15f)
                        MarkWorldRectDirty(new Rect(x * cell - 2, z * cell - 2, cell + 4, cell + 4));
            }
            _treeDensity = blurred;
            _treeGrid = n;
        }

        private float TreeDensity(float x, float z)
        {
            if (_treeDensity == null) return 0;
            float fx = x / 4f - 0.5f, fz = z / 4f - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, _treeGrid - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, _treeGrid - 1);
            int x1 = Mathf.Min(x0 + 1, _treeGrid - 1), z1 = Mathf.Min(z0 + 1, _treeGrid - 1);
            float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);
            float a = Mathf.Lerp(_treeDensity[z0 * _treeGrid + x0], _treeDensity[z0 * _treeGrid + x1], tx);
            float b = Mathf.Lerp(_treeDensity[z1 * _treeGrid + x0], _treeDensity[z1 * _treeGrid + x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        // ================================================================== painting

        private readonly List<BuildingEntity> _nearBuildings = new List<BuildingEntity>();

        private void PaintTile(int tile)
        {
            if (_data == null) return;
            int tx = tile % TilesPerSide, tz = tile / TilesPerSide;
            int ax0 = tx * TileTexels, az0 = tz * TileTexels;
            float size = _w.Config.WorldSize;
            float texel = size / AlphaRes;
            var nav = _w.Nav;
            var camp = _w.Camp;

            // Buildings that can influence this tile.
            _nearBuildings.Clear();
            var tileRect = new Rect(ax0 * texel - 8, az0 * texel - 8, TileTexels * texel + 16, TileTexels * texel + 16);
            foreach (var b in _w.Buildings)
            {
                float r = b.BoundingRadius + 3f;
                if (b.Kind == BuildingKind.Bridge) continue;
                if (b.X + r < tileRect.xMin || b.X - r > tileRect.xMax || b.Z + r < tileRect.yMin || b.Z - r > tileRect.yMax) continue;
                _nearBuildings.Add(b);
            }

            for (int z = 0; z < TileTexels; z++)
            for (int x = 0; x < TileTexels; x++)
            {
                float wx = (ax0 + x + 0.5f) * texel, wz = (az0 + z + 0.5f) * texel;
                var s = SampleAt(wx, wz, nav, camp);
                Weights(s, out float grass, out float meadow, out float forest, out float path, out float cliff, out float bank, out float soil);
                float sum = grass + meadow + forest + path + cliff + bank + soil;
                if (sum < 1e-4f) { grass = 1; sum = 1; }
                float inv = 1f / sum;
                if (_layerCount >= 7)
                {
                    _alphaTile[z, x, 0] = grass * inv;
                    _alphaTile[z, x, 1] = meadow * inv;
                    _alphaTile[z, x, 2] = forest * inv;
                    _alphaTile[z, x, 3] = path * inv;
                    _alphaTile[z, x, 4] = cliff * inv;
                    _alphaTile[z, x, 5] = bank * inv;
                    _alphaTile[z, x, 6] = soil * inv;
                }
                for (int d = 0; d < _details.Count; d++)
                {
                    float density = _details[d].Density(s);
                    // Stochastic rounding keeps sparse layers natural.
                    float jitter = Noise.Hash01(ax0 + x, az0 + z, (uint)(d * 7919 + 13) + _seed);
                    int count = Mathf.FloorToInt(density + jitter);
                    _detailTiles[d][z, x] = Mathf.Clamp(count, 0, 16);
                }
            }
            _data.SetAlphamaps(ax0, az0, _alphaTile);
            for (int d = 0; d < _details.Count; d++) _data.SetDetailLayer(ax0, az0, d, _detailTiles[d]);
        }

        private Sample SampleAt(float wx, float wz, PassabilityGrid nav, StorageEntity camp)
        {
            var s = new Sample { X = wx, Z = wz };
            s.Height = _w.Height.SampleHeight(wx, wz);
            s.Slope = _w.Height.SampleSlopeDegrees(wx, wz);
            s.WaterDist = _w.Water.DistanceToWater(wx, wz);
            var c = nav.WorldToCell(wx, wz);
            s.Road = nav.InBounds(c.X, c.Z) ? nav.Roadness(c.X, c.Z) : 0;
            // Soften the 2 m road grid with a little noise so paths have organic edges.
            s.N1 = Noise.Fbm(wx * 0.018f, wz * 0.018f, 3, _seed);
            s.N2 = Noise.Fbm(wx * 0.06f + 31f, wz * 0.06f - 17f, 3, _seed + 5);
            s.N3 = Noise.Value(wx * 0.22f, wz * 0.22f, _seed + 11);
            s.Road = Mathf.Clamp01(s.Road * (0.8f + 0.5f * s.N3) - 0.08f);
            s.Forest = TreeDensity(wx, wz);
            if (camp != null)
            {
                float d = Mathf.Sqrt((wx - camp.X) * (wx - camp.X) + (wz - camp.Z) * (wz - camp.Z));
                s.Camp = 1f - Noise.SmoothStep(6.5f, 10.5f + s.N2 * 3f, d);
            }
            foreach (var b in _nearBuildings)
            {
                float outside = b.DistanceOutside(wx, wz);
                if (b.Kind == BuildingKind.Field)
                {
                    if (outside <= 0.05f) s.Field = 1f;
                    else s.Apron = Mathf.Max(s.Apron, (1f - Noise.SmoothStep(0f, 1.2f, outside)) * 0.5f);
                }
                else
                {
                    float apron = 1f - Noise.SmoothStep(0.4f, 2.6f + s.N3 * 1.2f, outside);
                    if (!b.IsComplete) apron = Mathf.Max(apron, 1f - Noise.SmoothStep(1.5f, 4f, outside));
                    s.Apron = Mathf.Max(s.Apron, apron);
                }
            }
            return s;
        }

        private static void Weights(Sample s, out float grass, out float meadow, out float forest, out float path, out float cliff, out float bank, out float soil)
        {
            cliff = Noise.SmoothStep(25f, 36f, s.Slope + (s.N3 - 0.5f) * 6f);
            float open = 1f - cliff;
            bank = (1f - Noise.SmoothStep(1.2f, 4.5f + s.N2 * 2.5f, s.WaterDist)) * open;
            soil = s.Field * open;
            path = Mathf.Max(s.Road, Mathf.Max(s.Apron * 0.9f, s.Camp * 0.85f)) * open * (1f - soil);
            forest = s.Forest * 0.9f * open * (1f - path) * (1f - bank);
            float rest = Mathf.Clamp01(1f - cliff - bank - soil - path - forest);
            float m = Noise.SmoothStep(0.42f, 0.72f, s.N1 + (s.Height / 100f - 0.2f) * 0.25f - (1f - Noise.SmoothStep(3f, 14f, s.WaterDist)) * 0.3f);
            meadow = rest * m;
            grass = rest * (1f - m);
        }

        // ================================================================== queries

        public float HeightAt(float x, float z) => _w != null ? _w.Height.SampleHeight(x, z) : 0f;

        public bool Raycast(Ray ray, out Vector3 point)
        {
            point = default;
            if (Collider != null && Collider.Raycast(ray, out var hit, 3000f)) { point = hit.point; return true; }
            return false;
        }
    }
}
