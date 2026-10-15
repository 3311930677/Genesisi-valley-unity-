using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Terrain-conforming overlay meshes for tool previews and selection: textured discs/rings that drape over the
    /// heightfield, flat-topped strips along polylines (bridge spans, paths) and rectangles (building footprints).
    /// Rebuilt only when the shape or position changes noticeably, so hovering costs almost nothing.
    /// </summary>
    public sealed class GroundDecal
    {
        public readonly GameObject Go;
        private readonly MeshFilter _mf;
        private readonly MeshRenderer _mr;
        private readonly Mesh _mesh;
        private Vector3 _lastCentre = new Vector3(float.NaN, 0, 0);
        private float _lastRadius = -1f, _lastYaw;
        private int _lastShape = -1;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _i = new List<int>();

        public float Lift = 0.12f;
        public Func<float, float, float> Height;

        public GroundDecal(string name, Transform parent, Material material, Func<float, float, float> height)
        {
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            _mf = Go.AddComponent<MeshFilter>();
            _mr = Go.AddComponent<MeshRenderer>();
            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mr.lightProbeUsage = LightProbeUsage.Off;
            _mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _mr.sharedMaterial = material;
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
            Height = height;
            Go.SetActive(false);
        }

        public bool Visible { get => Go.activeSelf; set { if (Go.activeSelf != value) Go.SetActive(value); } }
        public Material Material { get => _mr.sharedMaterial; set => _mr.sharedMaterial = value; }
        public MeshRenderer Renderer => _mr;

        private float H(float x, float z) => Height != null ? Height(x, z) : 0f;

        /// <summary>Square texture laid over a radius (uv 0..1 across the diameter) – brush rings, selection rings.</summary>
        public void Disc(Vector3 centre, float radius, float yaw = 0f, int resolution = 24)
        {
            Visible = true;
            if (_lastShape == 0 && Mathf.Abs(radius - _lastRadius) < 0.02f && (centre - _lastCentre).sqrMagnitude < 0.0025f && Mathf.Abs(yaw - _lastYaw) < 0.5f) return;
            _lastShape = 0; _lastRadius = radius; _lastCentre = centre; _lastYaw = yaw;
            Clear();
            int n = Mathf.Clamp(resolution, 6, 64);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float u = x / (float)n, v = z / (float)n;
                var local = rot * new Vector3((u - 0.5f) * 2f * radius, 0, (v - 0.5f) * 2f * radius);
                float wx = centre.x + local.x, wz = centre.z + local.z;
                _v.Add(new Vector3(local.x, H(wx, wz) + Lift - centre.y, local.z));
                _uv.Add(new Vector2(u, v));
                _c.Add(Color.white);
            }
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                _i.Add(a); _i.Add(c); _i.Add(d); _i.Add(a); _i.Add(d); _i.Add(b);
            }
            Go.transform.position = centre;
            Upload();
        }

        /// <summary>Rotated rectangle outline/fill (u across, v along). <paramref name="border"/> 0 = filled.</summary>
        public void Rect(Vector3 centre, float sizeX, float sizeZ, float yaw, int resolution = 12)
        {
            Visible = true;
            float key = sizeX * 1000f + sizeZ;
            if (_lastShape == 1 && Mathf.Abs(key - _lastRadius) < 0.01f && (centre - _lastCentre).sqrMagnitude < 0.0025f && Mathf.Abs(yaw - _lastYaw) < 0.5f) return;
            _lastShape = 1; _lastRadius = key; _lastCentre = centre; _lastYaw = yaw;
            Clear();
            int nx = Mathf.Clamp(Mathf.CeilToInt(sizeX / 1.5f), 2, 40), nz = Mathf.Clamp(Mathf.CeilToInt(sizeZ / 1.5f), 2, 40);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, v = z / (float)nz;
                var local = rot * new Vector3((u - 0.5f) * sizeX, 0, (v - 0.5f) * sizeZ);
                float wx = centre.x + local.x, wz = centre.z + local.z;
                _v.Add(new Vector3(local.x, H(wx, wz) + Lift - centre.y, local.z));
                _uv.Add(new Vector2(u, v));
                _c.Add(Color.white);
            }
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                _i.Add(a); _i.Add(c); _i.Add(d); _i.Add(a); _i.Add(d); _i.Add(b);
            }
            Go.transform.position = centre;
            Upload();
        }

        /// <summary>
        /// Strip along a polyline. When <paramref name="flatHeights"/> is given, heights are taken from it (bridge
        /// decks, water surfaces) instead of the ground. v is measured in metres so dashed textures tile evenly.
        /// </summary>
        public void Strip(IList<Vector3> points, float halfWidth, bool flatHeights, float vScale = 0.25f, Color? tint = null)
        {
            Visible = points.Count >= 2;
            if (!Visible) return;
            _lastShape = 2;
            Clear();
            var origin = points[0];
            float along = 0f;
            var col = tint ?? Color.white;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (i > 0) along += Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(points[i - 1].x, 0, points[i - 1].z));
                var a = points[Mathf.Max(0, i - 1)];
                var b = points[Mathf.Min(points.Count - 1, i + 1)];
                var dir = new Vector3(b.x - a.x, 0, b.z - a.z);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();
                var side = new Vector3(dir.z, 0, -dir.x);
                for (int s = 0; s < 2; s++)
                {
                    var q = p + side * (s == 0 ? -halfWidth : halfWidth);
                    float y = flatHeights ? p.y : H(q.x, q.z);
                    _v.Add(new Vector3(q.x - origin.x, y + Lift - origin.y, q.z - origin.z));
                    _uv.Add(new Vector2(s, along * vScale));
                    _c.Add(col);
                }
                if (i > 0)
                {
                    int k = i * 2;
                    _i.Add(k - 2); _i.Add(k); _i.Add(k + 1); _i.Add(k - 2); _i.Add(k + 1); _i.Add(k - 1);
                }
            }
            Go.transform.position = origin;
            Upload();
        }

        private void Clear() { _v.Clear(); _uv.Clear(); _c.Clear(); _i.Clear(); }

        private void Upload()
        {
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_i, 0, true);
            _mesh.RecalculateNormals();
        }

        public void Invalidate() { _lastShape = -1; }

        public void Destroy()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (Go != null) UnityEngine.Object.Destroy(Go);
        }
    }
}
