using GenesisValley.Simulation;
using GenesisValley.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Tab overlay (GDD §11.2 "湿润度"): drapes a coloured grid over the valley showing where water reaches – blue near
    /// rivers, green on fertile ground good for fields, warm where the land is dry or too steep. Rebuilt lazily when the
    /// water map or heights change while visible.
    /// </summary>
    public sealed class WetnessOverlay : MonoBehaviour
    {
        public const int Resolution = 160;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private int _waterVersion = -1, _riverVersion = -1;
        private float _nextCheck;
        private bool _visible;
        private float _alpha;
        private Material _mat;
        private Color32[] _colors;
        private Vector3[] _verts;

        public bool Visible
        {
            get => _visible;
            set { _visible = value; if (value) _waterVersion = -1; }
        }

        private void Awake()
        {
            var mf = gameObject.AddComponent<MeshFilter>();
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mat = new Material(MaterialLibrary.Overlay(Color.white)) { name = "GV_WetnessOverlay" };
            _mr.sharedMaterial = _mat;
            _mesh = new Mesh { name = "WetnessOverlay", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            mf.sharedMesh = _mesh;
            _mr.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_mat != null) Destroy(_mat);
        }

        private void LateUpdate()
        {
            _alpha = Mathf.MoveTowards(_alpha, _visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            _mr.enabled = _alpha > 0.01f;
            if (!_mr.enabled) return;
            MaterialLibrary.SetColor(_mat, new Color(1, 1, 1, _alpha));
            var w = GameServices.World;
            if (w == null) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            int version = w.Water.Version * 7919 + w.Nav.Version;
            if (version == _waterVersion && w.RiverVersion == _riverVersion) return;
            _waterVersion = version;
            _riverVersion = w.RiverVersion;
            Rebuild(w);
        }

        private void Rebuild(WorldState w)
        {
            int n = Resolution;
            float size = w.Config.WorldSize;
            float step = size / n;
            int vc = (n + 1) * (n + 1);
            if (_verts == null || _verts.Length != vc)
            {
                _verts = new Vector3[vc];
                _colors = new Color32[vc];
                var tris = new int[n * n * 6];
                int t = 0;
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b;
                }
                _mesh.Clear();
                _mesh.vertices = _verts;
                _mesh.triangles = tris;
            }
            float maxSlope = w.Config.MaxWalkableSlopeDegrees;
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float wx = Mathf.Min(x * step, size - 0.01f), wz = Mathf.Min(z * step, size - 0.01f);
                int i = z * (n + 1) + x;
                float h = w.Height.SampleHeight(wx, wz);
                _verts[i] = new Vector3(wx, h + 0.25f, wz);
                var cell = w.Nav.WorldToCell(wx, wz);
                bool water = w.Nav.IsWater(cell.X, cell.Z);
                float wet = water ? 1f : w.Water.Wetness(wx, wz);
                float fert = WaterMap.Fertility(w.Height, wx, wz, maxSlope);
                Color c;
                if (water) c = new Color(0.25f, 0.55f, 1f, 0.55f);
                else
                {
                    var dry = new Color(0.95f, 0.62f, 0.32f, 0.32f);
                    var fertile = new Color(0.45f, 0.95f, 0.35f, 0.42f);
                    var wetC = new Color(0.3f, 0.75f, 1f, 0.5f);
                    c = Color.Lerp(dry, fertile, Mathf.Clamp01(fert * wet * 1.6f));
                    c = Color.Lerp(c, wetC, Mathf.Clamp01(wet - 0.55f) * 1.6f);
                }
                _colors[i] = c;
            }
            _mesh.vertices = _verts;
            _mesh.colors32 = _colors;
            _mesh.RecalculateBounds();
        }
    }
}
