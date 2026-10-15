using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>Accumulates vertices for procedural meshes (multiple sub-meshes, optional colours).</summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<Vector4> T = new List<Vector4>();
        public readonly List<List<int>> Sub = new List<List<int>> { new List<int>() };
        public bool UseColors, UseTangents;

        public int Count => V.Count;

        public List<int> Tris(int sub)
        {
            while (Sub.Count <= sub) Sub.Add(new List<int>());
            return Sub[sub];
        }

        public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); UV.Add(uv); if (UseColors) C.Add(Color.white); if (UseTangents) T.Add(new Vector4(1, 0, 0, 1)); return V.Count - 1; }
        public int Add(Vector3 p, Vector3 n, Vector2 uv, Color c) { V.Add(p); N.Add(n); UV.Add(uv); C.Add(c); if (UseTangents) T.Add(new Vector4(1, 0, 0, 1)); return V.Count - 1; }
        public void Tri(int sub, int a, int b, int c) { var t = Tris(sub); t.Add(a); t.Add(b); t.Add(c); }
        public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }

        public Mesh ToMesh(string name, bool recalcNormals = false)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetUVs(0, UV);
            if (UseColors && C.Count == V.Count) m.SetColors(C);
            m.subMeshCount = Sub.Count;
            for (int i = 0; i < Sub.Count; i++) m.SetTriangles(Sub[i], i, false);
            if (recalcNormals || N.Count != V.Count) m.RecalculateNormals(); else m.SetNormals(N);
            if (UseTangents && T.Count == V.Count) m.SetTangents(T); else if (UseTangents) m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public void Clear() { V.Clear(); N.Clear(); UV.Clear(); C.Clear(); T.Clear(); foreach (var s in Sub) s.Clear(); }
    }

    /// <summary>Cached procedural meshes: unit box/cylinder (centred), quads, discs, rings, tents, logs.</summary>
    public static class MeshKit
    {
        private static Mesh _box, _cyl, _log, _quad, _disc, _ring, _tent, _sphere, _cone;

        /// <summary>1 × 1 × 1 cube centred on the origin, UVs per face.</summary>
        public static Mesh Box
        {
            get
            {
                if (_box != null) return _box;
                var b = new MeshBuilder();
                void Face(Vector3 n, Vector3 u, Vector3 v)
                {
                    Vector3 c = n * 0.5f;
                    int i0 = b.Add(c - u * 0.5f - v * 0.5f, n, new Vector2(0, 0));
                    int i1 = b.Add(c - u * 0.5f + v * 0.5f, n, new Vector2(0, 1));
                    int i2 = b.Add(c + u * 0.5f + v * 0.5f, n, new Vector2(1, 1));
                    int i3 = b.Add(c + u * 0.5f - v * 0.5f, n, new Vector2(1, 0));
                    b.Quad(0, i0, i1, i2, i3);
                }
                Face(Vector3.forward, Vector3.left, Vector3.up);
                Face(Vector3.back, Vector3.right, Vector3.up);
                Face(Vector3.right, Vector3.forward, Vector3.up);
                Face(Vector3.left, Vector3.back, Vector3.up);
                Face(Vector3.up, Vector3.right, Vector3.forward);
                Face(Vector3.down, Vector3.left, Vector3.forward);
                return _box = b.ToMesh("GV_Box");
            }
        }

        /// <summary>Cylinder of diameter 1 and height 1 along Y, centred; sub-mesh 0 = side, 1 = caps.</summary>
        public static Mesh Cylinder => _cyl != null ? _cyl : (_cyl = BuildCylinder("GV_Cylinder", 14, 1f));

        /// <summary>Log: like <see cref="Cylinder"/> but along Z with slightly irregular radius. Sub-mesh 0 bark, 1 end grain.</summary>
        public static Mesh Log
        {
            get
            {
                if (_log != null) return _log;
                var m = BuildCylinder("GV_Log", 10, 1f, 0.06f);
                var v = m.vertices; var n = m.normals;
                var rot = Quaternion.Euler(90, 0, 0);
                for (int i = 0; i < v.Length; i++) { v[i] = rot * v[i]; n[i] = rot * n[i]; }
                m.vertices = v; m.normals = n; m.RecalculateBounds();
                return _log = m;
            }
        }

        private static Mesh BuildCylinder(string name, int seg, float h, float wobble = 0f)
        {
            var b = new MeshBuilder();
            float hy = h * 0.5f;
            var rnd = new System.Random(seg * 31 + 7);
            var radii = new float[seg];
            for (int i = 0; i < seg; i++) radii[i] = 0.5f * (1f + (float)(rnd.NextDouble() * 2 - 1) * wobble);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                float r = radii[i % seg];
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Add(dir * r + Vector3.down * hy, dir, new Vector2(i / (float)seg * 2f, 0));
                b.Add(dir * r + Vector3.up * hy, dir, new Vector2(i / (float)seg * 2f, h));
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                b.Quad(0, a0, a1, b1, b0);
            }
            foreach (int s in new[] { 1, -1 })
            {
                int c = b.Add(Vector3.up * hy * s, Vector3.up * s, new Vector2(0.5f, 0.5f));
                int first = b.Count;
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float r = radii[i];
                    var p = new Vector3(Mathf.Cos(a) * r, hy * s, Mathf.Sin(a) * r);
                    b.Add(p, Vector3.up * s, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < seg; i++)
                {
                    int p0 = first + i, p1 = first + (i + 1) % seg;
                    if (s > 0) b.Tri(1, c, p1, p0); else b.Tri(1, c, p0, p1);
                }
            }
            return b.ToMesh(name);
        }

        /// <summary>1 × 1 quad in the XZ plane facing up, centred.</summary>
        public static Mesh QuadXZ
        {
            get
            {
                if (_quad != null) return _quad;
                var b = new MeshBuilder();
                int a = b.Add(new Vector3(-0.5f, 0, -0.5f), Vector3.up, new Vector2(0, 0));
                int c = b.Add(new Vector3(-0.5f, 0, 0.5f), Vector3.up, new Vector2(0, 1));
                int d = b.Add(new Vector3(0.5f, 0, 0.5f), Vector3.up, new Vector2(1, 1));
                int e = b.Add(new Vector3(0.5f, 0, -0.5f), Vector3.up, new Vector2(1, 0));
                b.Quad(0, a, c, d, e);
                return _quad = b.ToMesh("GV_QuadXZ");
            }
        }

        /// <summary>Low sphere (radius 0.5).</summary>
        public static Mesh Sphere
        {
            get
            {
                if (_sphere != null) return _sphere;
                var b = new MeshBuilder();
                int rings = 8, seg = 12;
                for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= seg; x++)
                {
                    float v = y / (float)rings, u = x / (float)seg;
                    float th = v * Mathf.PI, ph = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    b.Add(n * 0.5f, n, new Vector2(u, 1 - v));
                }
                for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int i0 = y * (seg + 1) + x, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    b.Tri(0, i0, i1, i2); b.Tri(0, i1, i3, i2);
                }
                return _sphere = b.ToMesh("GV_Sphere");
            }
        }

        /// <summary>Cone: base radius 0.5 at y=0, apex at y=1.</summary>
        public static Mesh Cone
        {
            get
            {
                if (_cone != null) return _cone;
                var b = new MeshBuilder();
                int seg = 12;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                    var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                    var apex = Vector3.up;
                    var n = Vector3.Cross(p1 - p0, apex - p0).normalized * -1f;
                    int i0 = b.Add(p0, n, new Vector2(0, 0)), i1 = b.Add(apex, n, new Vector2(0.5f, 1)), i2 = b.Add(p1, n, new Vector2(1, 0));
                    b.Tri(0, i0, i1, i2);
                }
                return _cone = b.ToMesh("GV_Cone", true);
            }
        }

        /// <summary>Disc of radius 1 in XZ as a polar grid (rings × segments) so it can be draped over terrain.</summary>
        public static Mesh NewDisc(int rings, int segments, string name = "GV_Disc")
        {
            var b = new MeshBuilder { UseColors = true };
            b.Add(Vector3.zero, Vector3.up, new Vector2(0.5f, 0.5f), Color.white);
            for (int r = 1; r <= rings; r++)
            {
                float rad = r / (float)rings;
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2f / segments;
                    var p = new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad);
                    b.Add(p, Vector3.up, new Vector2(0.5f + p.x * 0.5f, 0.5f + p.z * 0.5f), Color.white);
                }
            }
            for (int s = 0; s < segments; s++) b.Tri(0, 0, 1 + (s + 1) % segments, 1 + s);
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * segments, outer = 1 + r * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    b.Quad(0, inner + s, inner + s1, outer + s1, outer + s);
                }
            }
            var m = b.ToMesh(name);
            m.MarkDynamic();
            return m;
        }

        /// <summary>Flat ring (annulus) between radius 0.8 and 1 in XZ.</summary>
        public static Mesh Ring
        {
            get
            {
                if (_ring != null) return _ring;
                var b = new MeshBuilder();
                int seg = 48;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    b.Add(d * 0.8f, Vector3.up, new Vector2(s / (float)seg, 0));
                    b.Add(d, Vector3.up, new Vector2(s / (float)seg, 1));
                }
                for (int s = 0; s < seg; s++) b.Quad(0, s * 2, s * 2 + 1, s * 2 + 3, s * 2 + 2);
                return _ring = b.ToMesh("GV_Ring");
            }
        }

        /// <summary>A-frame tent 1 m wide (X), 1 m deep (Z), 1 m tall; sub-mesh 0 cloth, 1 poles are separate objects.</summary>
        public static Mesh Tent
        {
            get
            {
                if (_tent != null) return _tent;
                var b = new MeshBuilder();
                Vector3 l0 = new Vector3(-0.5f, 0, -0.5f), l1 = new Vector3(-0.5f, 0, 0.5f);
                Vector3 r0 = new Vector3(0.5f, 0, -0.5f), r1 = new Vector3(0.5f, 0, 0.5f);
                Vector3 t0 = new Vector3(0, 1, -0.5f), t1 = new Vector3(0, 1, 0.5f);
                void Panel(Vector3 a, Vector3 c, Vector3 d, Vector3 e)
                {
                    var n = Vector3.Cross(c - a, e - a).normalized;
                    int i0 = b.Add(a, n, new Vector2(0, 0)), i1 = b.Add(c, n, new Vector2(0, 1)), i2 = b.Add(d, n, new Vector2(1, 1)), i3 = b.Add(e, n, new Vector2(1, 0));
                    b.Quad(0, i0, i1, i2, i3);
                    b.Quad(0, i3, i2, i1, i0);
                }
                Panel(l0, t0, t1, l1);
                Panel(r1, t1, t0, r0);
                // Back wall + a half-open front flap.
                int ba = b.Add(l0, Vector3.back, new Vector2(0, 0)), bb = b.Add(t0, Vector3.back, new Vector2(0.5f, 1)), bc = b.Add(r0, Vector3.back, new Vector2(1, 0));
                b.Tri(0, ba, bb, bc); b.Tri(0, bc, bb, ba);
                var fm = new Vector3(-0.1f, 0, 0.5f);
                int fa = b.Add(l1, Vector3.forward, new Vector2(0, 0)), fb = b.Add(t1, Vector3.forward, new Vector2(0.5f, 1)), fc = b.Add(fm, Vector3.forward, new Vector2(0.4f, 0));
                b.Tri(0, fa, fb, fc); b.Tri(0, fc, fb, fa);
                return _tent = b.ToMesh("GV_Tent", true);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Adds a child with mesh + materials; returns its transform.</summary>
        public static Transform Spawn(Mesh mesh, Material[] mats, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 localScale, bool shadows = true, string name = null)
        {
            var go = new GameObject(name ?? mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        public static Transform Spawn(Mesh mesh, Material mat, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 localScale, bool shadows = true, string name = null)
            => Spawn(mesh, new[] { mat }, parent, localPos, localRot, localScale, shadows, name);

        /// <summary>Log with bark + end grain materials, length along local Z.</summary>
        public static Transform SpawnLog(Transform parent, Vector3 pos, Quaternion rot, float length, float diameter, bool shadows = true)
            => Spawn(Log, new[] { MaterialLibrary.ForKey("Bark"), MaterialLibrary.ForKey("EndGrain") }, parent, pos, rot, new Vector3(diameter, diameter, length), shadows, "Log");
    }

    /// <summary>Bounds of model prefabs in their own root space (cached; computed without instantiating).</summary>
    public static class ModelBounds
    {
        private static readonly Dictionary<int, Bounds> Cache = new Dictionary<int, Bounds>();

        public static Bounds Of(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
            int id = prefab.GetInstanceID();
            if (Cache.TryGetValue(id, out var b)) return b;
            bool any = false;
            var root = prefab.transform.worldToLocalMatrix;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = root * mf.transform.localToWorldMatrix;
                Encapsulate(ref b, ref any, mf.sharedMesh.bounds, m);
            }
            foreach (var sm in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (sm.sharedMesh == null) continue;
                var m = root * sm.transform.localToWorldMatrix;
                Encapsulate(ref b, ref any, sm.sharedMesh.bounds, m);
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one);
            Cache[id] = b;
            return b;
        }

        private static void Encapsulate(ref Bounds b, ref bool any, Bounds local, Matrix4x4 m)
        {
            var c = local.center; var e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = m.MultiplyPoint3x4(c + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
    }
}
