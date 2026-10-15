using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Draws many copies of one mesh (all sub-meshes) with GPU instancing and no GameObjects — used for crops and
    /// other dense, non-interactive detail. Matrices are owned by the caller; call <see cref="Draw"/> every frame.
    /// </summary>
    public sealed class InstancedBatch
    {
        public readonly Mesh Mesh;
        public readonly Material[] Materials;
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
        public ShadowCastingMode Shadows = ShadowCastingMode.On;
        public bool ReceiveShadows = true;
        public int Layer;
        private Bounds _bounds;
        private bool _boundsDirty = true;
        private static readonly Matrix4x4[] Chunk = new Matrix4x4[1023];

        public InstancedBatch(Mesh mesh, Material[] materials)
        {
            Mesh = mesh;
            Materials = materials;
            foreach (var m in materials) if (m != null) m.enableInstancing = true;
        }

        public static InstancedBatch FromPrefab(GameObject prefab, Material overrideMaterial = null)
        {
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null || mr == null) return null;
            var mats = mr.sharedMaterials;
            if (overrideMaterial != null) { mats = new Material[mats.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = overrideMaterial; }
            return new InstancedBatch(mf.sharedMesh, mats);
        }

        public void MarkDirty() => _boundsDirty = true;

        public void Draw(Camera cam = null)
        {
            int n = Matrices.Count;
            if (n == 0 || Mesh == null) return;
            if (_boundsDirty)
            {
                _boundsDirty = false;
                var p0 = Matrices[0].GetColumn(3);
                _bounds = new Bounds(p0, Vector3.one);
                float maxScale = 0f;
                for (int i = 0; i < n; i++)
                {
                    var m = Matrices[i];
                    _bounds.Encapsulate(m.GetColumn(3));
                    float sc = Mathf.Max(((Vector3)m.GetColumn(0)).sqrMagnitude, Mathf.Max(((Vector3)m.GetColumn(1)).sqrMagnitude, ((Vector3)m.GetColumn(2)).sqrMagnitude));
                    if (sc > maxScale) maxScale = sc;
                }
                var mb = Mesh.bounds;
                _bounds.Expand((mb.extents.magnitude + mb.center.magnitude) * 2f * Mathf.Sqrt(maxScale) + 0.5f);
            }
            for (int sub = 0; sub < Mesh.subMeshCount && sub < Materials.Length; sub++)
            {
                var mat = Materials[sub];
                if (mat == null) continue;
                var rp = new RenderParams(mat)
                {
                    shadowCastingMode = Shadows,
                    receiveShadows = ReceiveShadows,
                    layer = Layer,
                    worldBounds = _bounds,
                    camera = cam,
                };
                for (int start = 0; start < n; start += Chunk.Length)
                {
                    int count = Mathf.Min(Chunk.Length, n - start);
                    Matrices.CopyTo(start, Chunk, 0, count);
                    Graphics.RenderMeshInstanced(rp, Mesh, sub, Chunk, count);
                }
            }
        }
    }
}
