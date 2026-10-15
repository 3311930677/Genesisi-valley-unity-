using System.Collections.Generic;
using GenesisValley.Simulation;
using GenesisValley.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// River ribbons + pond discs built from <see cref="RiverData.Samples"/> (GDD §11: water is geometry + shader,
    /// not a volume simulation). Rebuilds only when <see cref="WorldState.RiverVersion"/> changes; a new river "fills"
    /// from its source to the mouth over a couple of seconds so the player sees the water arrive.
    /// </summary>
    public sealed class WaterPresenter : MonoBehaviour
    {
        private sealed class RiverView
        {
            public int Id, Version;
            public GameObject Root;
            public Mesh Ribbon, Pond;
            public MeshRenderer RibbonRenderer, PondRenderer;
            public float Fill = 1f;      // 0..1 reveal along the length
            public float Length;
            public Vector3 Source;
            public float NextBubble;
            public int Rows;
        }

        private WorldState _w;
        private int _riverVersion = -1;
        private readonly Dictionary<int, RiverView> _views = new Dictionary<int, RiverView>();
        private Material _material;
        private static readonly int FillId = Shader.PropertyToID("_GV_Fill");
        private readonly List<int> _stale = new List<int>();

        public const float EdgeOverlap = 0.6f;

        public Material WaterMaterial
        {
            get
            {
                if (_material != null) return _material;
                var art = GameServices.Art;
                if (art.waterMaterial != null) _material = art.waterMaterial;
                else
                {
                    var sh = art.waterShader != null ? art.waterShader : Shader.Find("GenesisValley/Water");
                    if (sh == null) sh = MaterialLibrary.LitShader;
                    _material = new Material(sh) { name = "GV_Water_Runtime" };
                    var n = art.Texture("Water_Ripple_N");
                    if (n != null && _material.HasProperty("_NormalMap")) _material.SetTexture("_NormalMap", n);
                    if (!_material.HasProperty("_NormalMap")) MaterialLibrary.SetColor(_material, new Color(0.45f, 0.66f, 0.68f, 0.8f));
                }
                return _material;
            }
        }

        public void Bind(WorldState w)
        {
            _w = w;
            _riverVersion = -1;
            foreach (var v in _views.Values) Destroy(v);
            _views.Clear();
            Sync(true);
        }

        private void Destroy(RiverView v)
        {
            if (v.Root != null) Destroy(v.Root);
            if (v.Ribbon != null) Destroy(v.Ribbon);
            if (v.Pond != null) Destroy(v.Pond);
        }

        private void LateUpdate()
        {
            if (_w == null) return;
            if (_w.RiverVersion != _riverVersion) Sync(false);
            foreach (var v in _views.Values)
            {
                if (v.Fill < 1f)
                {
                    v.Fill = Mathf.MoveTowards(v.Fill, 1f, Time.deltaTime / Mathf.Clamp(v.Length / 70f, 1.2f, 4f));
                    ApplyFill(v);
                }
                // Gentle bubbling at the spring so the source reads as a source.
                if (Time.time > v.NextBubble && GameServices.Fx != null && IsNearCamera(v.Source, 120f))
                {
                    v.NextBubble = Time.time + Random.Range(0.6f, 1.4f);
                    GameServices.Fx.Play(FxKind.Water, v.Source + new Vector3(Random.Range(-0.6f, 0.6f), 0.05f, Random.Range(-0.6f, 0.6f)), 3, 0.6f);
                }
            }
        }

        private static bool IsNearCamera(Vector3 p, float range)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - p).sqrMagnitude < range * range * 4f;
        }

        public void Sync(bool instant)
        {
            _riverVersion = _w.RiverVersion;
            _stale.Clear();
            foreach (var id in _views.Keys) if (_w.FindRiver(id) == null) _stale.Add(id);
            foreach (var id in _stale) { Destroy(_views[id]); _views.Remove(id); }

            foreach (var r in _w.Rivers)
            {
                bool isNew = !_views.TryGetValue(r.Id, out var view);
                if (!isNew && view.Version == r.Version) continue;
                if (isNew)
                {
                    view = new RiverView { Id = r.Id, Root = new GameObject("River_" + r.Id) };
                    view.Root.transform.SetParent(transform, false);
                    _views[r.Id] = view;
                    view.Fill = instant ? 1f : 0f;
                }
                view.Version = r.Version;
                Build(view, r);
                ApplyFill(view);
            }
        }

        private void Build(RiverView view, RiverData r)
        {
            if (view.Ribbon == null) view.Ribbon = new Mesh { name = "RiverRibbon_" + r.Id };
            BuildRibbon(r.Samples, EdgeOverlap, view.Ribbon, true);
            view.Rows = Mathf.Max(0, r.Samples.Count - 1);
            view.Length = Mathf.Max(1f, r.Length);
            if (r.Samples.Count > 0) view.Source = new Vector3(r.Samples[0].X, r.Samples[0].Surface, r.Samples[0].Z);
            view.RibbonRenderer = EnsureRenderer(view.Root.transform, "Ribbon", view.Ribbon, view.RibbonRenderer);

            if (r.PondRadius > 0.5f && r.Samples.Count > 0)
            {
                var end = r.Samples[r.Samples.Count - 1];
                if (view.Pond == null) view.Pond = new Mesh { name = "RiverPond_" + r.Id };
                BuildPond(view.Pond, new Vector3(end.X, end.Surface, end.Z), r.PondRadius + EdgeOverlap, r.Id);
                view.PondRenderer = EnsureRenderer(view.Root.transform, "Pond", view.Pond, view.PondRenderer);
            }
            else if (view.PondRenderer != null) view.PondRenderer.enabled = false;
        }

        private MeshRenderer EnsureRenderer(Transform parent, string name, Mesh mesh, MeshRenderer existing)
        {
            if (existing == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>();
                existing = go.AddComponent<MeshRenderer>();
                existing.shadowCastingMode = ShadowCastingMode.Off;
                existing.receiveShadows = true;
                existing.sharedMaterial = WaterMaterial;
            }
            existing.GetComponent<MeshFilter>().sharedMesh = mesh;
            existing.enabled = true;
            return existing;
        }

        private static void ApplyFill(RiverView v)
        {
            // Reveal the ribbon by scaling vertex alpha via a property block (cheap, no rebuild).
            var mpb = new MaterialPropertyBlock();
            mpb.SetFloat(FillId, v.Fill * v.Length);
            if (v.RibbonRenderer != null)
            {
                v.RibbonRenderer.SetPropertyBlock(mpb);
                // Shader-independent fallback: shrink the visible index range while filling.
                var mesh = v.Ribbon;
                if (mesh != null && mesh.subMeshCount > 0)
                {
                    var desc = mesh.GetSubMesh(0);
                    int quadsPerRow = (Across - 1) * 6;
                    int rows = v.Rows;
                    int visibleRows = Mathf.Clamp(Mathf.CeilToInt(rows * v.Fill), 0, rows);
                    desc.indexCount = visibleRows * quadsPerRow;
                    mesh.SetSubMesh(0, desc, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
            }
            if (v.PondRenderer != null)
            {
                float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, v.Fill));
                v.PondRenderer.transform.localScale = new Vector3(Mathf.Max(0.001f, s), 1f, Mathf.Max(0.001f, s));
                var c = v.PondRenderer.transform;
                // Scale about the pond centre: the mesh is authored around its own pivot.
                c.localPosition = Vector3.zero;
            }
        }

        private const int Across = 5;
        private static readonly List<Vector3> V = new List<Vector3>();
        private static readonly List<Vector3> N = new List<Vector3>();
        private static readonly List<Vector4> T = new List<Vector4>();
        private static readonly List<Vector2> UV = new List<Vector2>();
        private static readonly List<Color> C = new List<Color>();
        private static readonly List<int> I = new List<int>();

        /// <summary>
        /// Builds a flat ribbon following the samples (u across 0..1 left→right of the flow, v = metres along).
        /// Used for the real river and for the tool preview (preview passes <paramref name="forWater"/> = false).
        /// </summary>
        public static void BuildRibbon(IList<RiverSample> samples, float extra, Mesh mesh, bool forWater, float lift = 0f)
        {
            V.Clear(); N.Clear(); T.Clear(); UV.Clear(); C.Clear(); I.Clear();
            int n = samples.Count;
            float along = 0f;
            int rows = 0;
            for (int i = 0; i < n; i++)
            {
                var s = samples[i];
                if (i > 0) along += Mathf.Sqrt((s.X - samples[i - 1].X) * (s.X - samples[i - 1].X) + (s.Z - samples[i - 1].Z) * (s.Z - samples[i - 1].Z));
                var a = samples[Mathf.Max(0, i - 1)];
                var b = samples[Mathf.Min(n - 1, i + 1)];
                var dir = new Vector3(b.X - a.X, 0, b.Z - a.Z);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();
                var left = new Vector3(-dir.z, 0, dir.x);
                float hw = s.HalfWidth + extra;
                // Fade the first metre (spring) and boost foam near the source and where the channel narrows.
                float startFade = Mathf.Clamp01(along / 1.5f);
                for (int k = 0; k < Across; k++)
                {
                    float u = k / (float)(Across - 1);
                    var p = new Vector3(s.X, s.Surface + lift, s.Z) + left * Mathf.Lerp(hw, -hw, u);
                    V.Add(p);
                    N.Add(Vector3.up);
                    T.Add(new Vector4(-left.x, 0, -left.z, -1f));
                    UV.Add(new Vector2(u, along));
                    float centre = 1f - Mathf.Pow(Mathf.Abs(2f * u - 1f), 2f);
                    if (forWater)
                    {
                        float flow = Mathf.Lerp(0.55f, 1f, centre);
                        float foam = along < 4f ? 1f - along / 4f : 0f;
                        C.Add(new Color(flow, foam, 0f, startFade));
                    }
                    else C.Add(s.Valid ? new Color(1, 1, 1, 1) : new Color(1, 0.25f, 0.2f, 1));
                }
                if (i > 0)
                {
                    int r0 = (rows - 1) * Across, r1 = rows * Across;
                    for (int k = 0; k < Across - 1; k++)
                    {
                        I.Add(r0 + k); I.Add(r1 + k); I.Add(r1 + k + 1);
                        I.Add(r0 + k); I.Add(r1 + k + 1); I.Add(r0 + k + 1);
                    }
                }
                rows++;
            }
            mesh.Clear();
            mesh.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetTangents(T); mesh.SetUVs(0, UV); mesh.SetColors(C);
            mesh.SetTriangles(I, 0, true);
        }

        private static void BuildPond(Mesh mesh, Vector3 centre, float radius, int seed)
        {
            V.Clear(); N.Clear(); T.Clear(); UV.Clear(); C.Clear(); I.Clear();
            const int seg = 48, rings = 4;
            V.Add(centre); N.Add(Vector3.up); T.Add(new Vector4(1, 0, 0, -1)); UV.Add(new Vector2(centre.x / 6f, centre.z)); C.Add(new Color(0.08f, 0f, 0f, 1f));
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * r / rings;
                for (int s = 0; s < seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    // Slightly organic outline so ponds are not perfect circles.
                    float wobble = r == rings ? 1f + 0.08f * (Noise.Value(Mathf.Cos(a) * 2.3f + seed, Mathf.Sin(a) * 2.3f) - 0.5f) * 2f : 1f;
                    var p = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rr * wobble;
                    V.Add(p); N.Add(Vector3.up); T.Add(new Vector4(1, 0, 0, -1)); UV.Add(new Vector2(p.x / 6f, p.z));
                    C.Add(new Color(0.08f, r == rings ? 0.5f : 0f, 0f, 1f));
                }
            }
            for (int s = 0; s < seg; s++) { I.Add(0); I.Add(1 + (s + 1) % seg); I.Add(1 + s); }
            for (int r = 1; r < rings; r++)
            {
                int a0 = 1 + (r - 1) * seg, b0 = 1 + r * seg;
                for (int s = 0; s < seg; s++)
                {
                    int s1 = (s + 1) % seg;
                    I.Add(a0 + s); I.Add(a0 + s1); I.Add(b0 + s1);
                    I.Add(a0 + s); I.Add(b0 + s1); I.Add(b0 + s);
                }
            }
            // Vertices are in world space; recentre around the pond so the fill animation scales from the middle.
            for (int i = 0; i < V.Count; i++) V[i] -= centre;
            mesh.Clear();
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetTangents(T); mesh.SetUVs(0, UV); mesh.SetColors(C);
            mesh.SetTriangles(I, 0, true);
            mesh.RecalculateBounds();
        }

        private void Update()
        {
            // Pond renderers sit at their centre (vertices were recentred in BuildPond).
            foreach (var v in _views.Values)
            {
                if (v.PondRenderer == null || v.Pond == null) continue;
                var r = _w != null ? _w.FindRiver(v.Id) : null;
                if (r == null || r.Samples.Count == 0) continue;
                var end = r.Samples[r.Samples.Count - 1];
                v.PondRenderer.transform.position = new Vector3(end.X, end.Surface, end.Z);
            }
        }

        /// <summary>Closest river surface point to <paramref name="p"/> (audio emitter, fishing bobbers).</summary>
        public bool NearestWater(Vector3 p, out Vector3 point, out float edgeDistance)
        {
            point = p; edgeDistance = float.MaxValue;
            if (_w == null) return false;
            foreach (var r in _w.Rivers)
            {
                if (!RiverBuilder.Nearest(r, p.x, p.z, out float d, out float surface, out float hw)) continue;
                float edge = Mathf.Max(0f, d - hw);
                if (edge >= edgeDistance) continue;
                edgeDistance = edge;
                RiverSample best = default; float bd = float.MaxValue;
                foreach (var s in r.Samples)
                {
                    float dd = (s.X - p.x) * (s.X - p.x) + (s.Z - p.z) * (s.Z - p.z);
                    if (dd < bd) { bd = dd; best = s; }
                }
                if (r.PondRadius > 0 && r.Samples.Count > 0)
                {
                    var e = r.Samples[r.Samples.Count - 1];
                    float de = (e.X - p.x) * (e.X - p.x) + (e.Z - p.z) * (e.Z - p.z);
                    if (de < bd) best = e;
                }
                point = new Vector3(best.X, surface, best.Z);
            }
            return edgeDistance < float.MaxValue;
        }
    }
}
