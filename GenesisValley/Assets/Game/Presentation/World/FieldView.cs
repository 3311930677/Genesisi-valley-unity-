using System.Collections.Generic;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Procedural farm plot: furrowed soil that follows the terrain, a split-rail fence with a gate gap facing the
    /// door side, an optional scarecrow, and instanced crop rows whose height/colour follow CropGrowth (green →
    /// gold). One draw call per field for the crops.
    /// </summary>
    public sealed class FieldView
    {
        private readonly WorldState _w;
        private readonly Transform _root;
        private readonly GameObject _soilGo, _fenceGo, _stakesGo;
        private readonly Mesh _soil;
        private InstancedBatch _crops;
        private Material _cropMat;
        private readonly List<Vector3> _cropLocal = new List<Vector3>();
        private readonly List<float> _cropSeed = new List<float>();
        private readonly List<Transform> _fenceParts = new List<Transform>();
        private float _shownGrowth = -1f;
        private CropState _shownCrop = (CropState)(-1);
        private float _cropScale = 1f, _cropAnim = 1f;
        private bool _site;
        private float _progressShown = -1f;
        private float _cropModelHeight = 1f;
        private static readonly int DrynessId = Shader.PropertyToID("_Dryness");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly int _id;

        public FieldView(WorldState w, BuildingEntity b, Transform root)
        {
            _w = w; _root = root; _id = b.Id.Value;
            _soil = new Mesh { name = "FieldSoil_" + b.Id.Value };
            _soilGo = new GameObject("Soil");
            _soilGo.transform.SetParent(root, false);
            _soilGo.AddComponent<MeshFilter>().sharedMesh = _soil;
            var mr = _soilGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SoilMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            _fenceGo = new GameObject("Fence");
            _fenceGo.transform.SetParent(root, false);
            _stakesGo = new GameObject("Stakes");
            _stakesGo.transform.SetParent(root, false);
            BuildCropBatch();
            Rebuild(b);
        }

        private static Material _soilMat;
        private static Material SoilMaterial()
        {
            if (_soilMat != null) return _soilMat;
            var layer = GameServices.Art.Layer(GroundLayer.Soil);
            if (layer != null && layer.diffuseTexture != null)
            {
                _soilMat = MaterialLibrary.LitTextured(layer.diffuseTexture, new Color(0.86f, 0.78f, 0.7f), 0.05f);
                if (layer.normalMapTexture != null && _soilMat.HasProperty("_BumpMap"))
                {
                    _soilMat.SetTexture("_BumpMap", layer.normalMapTexture);
                    _soilMat.EnableKeyword("_NORMALMAP");
                }
            }
            else _soilMat = MaterialLibrary.Lit(new Color(0.42f, 0.31f, 0.22f), 0.05f);
            return _soilMat;
        }

        private void BuildCropBatch()
        {
            var prefab = GameServices.Art.Model("Grass_Wispy_Tall") ?? GameServices.Art.Model("Grass_Common_Tall");
            Mesh mesh = null; Material src = null;
            if (prefab != null)
            {
                var mf = prefab.GetComponentInChildren<MeshFilter>();
                var mr = prefab.GetComponentInChildren<MeshRenderer>();
                if (mf != null && mr != null) { mesh = mf.sharedMesh; src = mr.sharedMaterial; }
                var bnd = ModelBounds.Of(prefab);
                _cropModelHeight = Mathf.Max(0.1f, bnd.size.y);
            }
            if (mesh == null) { mesh = MeshKit.Cone; _cropModelHeight = 1f; }
            _cropMat = src != null ? new Material(src) { name = "Crop_" + _id } : new Material(MaterialLibrary.FoliageShader) { name = "Crop_" + _id };
            if (_cropMat.HasProperty("_DryColor")) _cropMat.SetColor("_DryColor", new Color(0.86f, 0.68f, 0.3f));
            if (_cropMat.HasProperty(BaseColorId)) _cropMat.SetColor(BaseColorId, new Color(0.62f, 0.78f, 0.42f));
            if (_cropMat.HasProperty("_WindStrength")) _cropMat.SetFloat("_WindStrength", 0.55f);
            _crops = new InstancedBatch(mesh, new[] { _cropMat }) { Shadows = ShadowCastingMode.On };
        }

        public void Rebuild(BuildingEntity b)
        {
            BuildSoil(b);
            BuildFence(b);
            BuildStakes(b);
            LayoutCrops(b);
            _shownGrowth = -1f;
            UpdateCropMatrices(b);
        }

        private float LocalGround(BuildingEntity b, float lx, float lz)
        {
            b.ToWorld(lx, lz, out float x, out float z);
            return _w.Height.SampleHeight(x, z) - _root.position.y;
        }

        private const float Ridge = 0.62f;

        private void BuildSoil(BuildingEntity b)
        {
            float hx = b.SizeX * 0.5f, hz = b.SizeZ * 0.5f;
            int nx = Mathf.CeilToInt(b.SizeX / 0.1f) + 1, nz = Mathf.CeilToInt(b.SizeZ / 0.5f) + 1;
            var mb = new MeshBuilder { UseColors = true };
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float lx = -hx + b.SizeX * ix / (nx - 1);
                float lz = -hz + b.SizeZ * iz / (nz - 1);
                float edge = Mathf.Min(Mathf.Min(lx + hx, hx - lx), Mathf.Min(lz + hz, hz - lz));
                float rim = Mathf.Clamp01(edge / 0.35f);
                float furrow = 0.5f + 0.5f * Mathf.Cos((lx + hx) / Ridge * Mathf.PI * 2f);
                float y = LocalGround(b, lx, lz) + 0.03f + rim * (0.02f + 0.09f * furrow);
                float shade = Mathf.Lerp(0.72f, 1.05f, furrow) * (0.92f + 0.12f * Noise.Value(lx * 1.7f + _id, lz * 1.7f));
                mb.Add(new Vector3(lx, y, lz), Vector3.up, new Vector2((lx + hx) * 0.5f, (lz + hz) * 0.5f), new Color(shade, shade, shade, 1));
            }
            for (int iz = 0; iz < nz - 1; iz++)
            for (int ix = 0; ix < nx - 1; ix++)
            {
                int a = iz * nx + ix;
                mb.Quad(0, a, a + nx, a + nx + 1, a + 1);
            }
            _soil.Clear();
            var m = mb.ToMesh(_soil.name, true);
            _soil.SetVertices(m.vertices); _soil.SetNormals(m.normals); _soil.SetUVs(0, m.uv); _soil.SetColors(m.colors);
            _soil.SetTriangles(m.triangles, 0);
            _soil.RecalculateBounds();
            Object.Destroy(m);
        }

        private void BuildFence(BuildingEntity b)
        {
            foreach (Transform c in _fenceGo.transform) Object.Destroy(c.gameObject);
            _fenceParts.Clear();
            var wood = MaterialLibrary.ForKey("Wood");
            var dark = MaterialLibrary.ForKey("DarkWood");
            float hx = b.SizeX * 0.5f + 0.4f, hz = b.SizeZ * 0.5f + 0.4f;
            var corners = new[] { new Vector2(-hx, hz), new Vector2(hx, hz), new Vector2(hx, -hz), new Vector2(-hx, -hz) };
            var posts = new List<Vector3>();
            for (int side = 0; side < 4; side++)
            {
                var a = corners[side]; var c = corners[(side + 1) % 4];
                float len = (c - a).magnitude;
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
                for (int k = 0; k < n; k++)
                {
                    float t0 = k / (float)n, t1 = (k + 1) / (float)n;
                    var p0 = Vector2.Lerp(a, c, t0); var p1 = Vector2.Lerp(a, c, t1);
                    // Gate gap: middle segment of the front side (local +Z, the side facing the village).
                    bool gate = side == 0 && k == n / 2;
                    var w0 = new Vector3(p0.x, LocalGround(b, p0.x, p0.y), p0.y);
                    var w1 = new Vector3(p1.x, LocalGround(b, p1.x, p1.y), p1.y);
                    posts.Add(w0);
                    if (gate) continue;
                    foreach (float h in new[] { 0.42f, 0.82f })
                    {
                        var m0 = w0 + Vector3.up * h; var m1 = w1 + Vector3.up * h;
                        var mid = (m0 + m1) * 0.5f;
                        var dir = m1 - m0;
                        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0, 0, Noise.Hash01(_id + side * 31, k) * 20f);
                        _fenceParts.Add(MeshKit.Spawn(MeshKit.Box, wood, _fenceGo.transform, mid, rot, new Vector3(0.07f, 0.07f, dir.magnitude + 0.12f), true, "Rail"));
                    }
                }
            }
            foreach (var p in posts)
            {
                float lean = (Noise.Hash01((int)(p.x * 10) + _id, (int)(p.z * 10)) - 0.5f) * 6f;
                _fenceParts.Add(MeshKit.Spawn(MeshKit.Cylinder, new[] { dark, MaterialLibrary.ForKey("EndGrain") }, _fenceGo.transform, p + Vector3.up * 0.5f, Quaternion.Euler(lean, 0, lean * 0.5f), new Vector3(0.12f, 1.05f, 0.12f), true, "Post"));
            }
            if (b.Variant % 2 == 0) BuildScarecrow(b);
        }

        private void BuildScarecrow(BuildingEntity b)
        {
            var root = new GameObject("Scarecrow").transform;
            root.SetParent(_fenceGo.transform, false);
            float lx = b.SizeX * 0.5f - 0.7f, lz = -b.SizeZ * 0.5f + 1.1f;
            root.localPosition = new Vector3(lx, LocalGround(b, lx, lz), lz);
            root.localRotation = Quaternion.Euler(0, 160f + Noise.Hash01(_id, 9) * 40f, 0);
            var wood = MaterialLibrary.ForKey("DarkWood");
            var straw = MaterialLibrary.ForKey("Straw");
            var cloth = MaterialLibrary.Lit(new Color(0.55f, 0.32f, 0.25f), 0.05f);
            var hat = MaterialLibrary.Lit(new Color(0.5f, 0.4f, 0.24f), 0.05f);
            MeshKit.Spawn(MeshKit.Cylinder, wood, root, new Vector3(0, 0.95f, 0), Quaternion.identity, new Vector3(0.08f, 1.9f, 0.08f), true, "Pole");
            MeshKit.Spawn(MeshKit.Cylinder, wood, root, new Vector3(0, 1.45f, 0), Quaternion.Euler(0, 0, 90), new Vector3(0.06f, 1.5f, 0.06f), true, "Arms");
            MeshKit.Spawn(MeshKit.Box, cloth, root, new Vector3(0, 1.3f, 0), Quaternion.identity, new Vector3(0.5f, 0.6f, 0.25f), true, "Shirt");
            MeshKit.Spawn(MeshKit.Sphere, straw, root, new Vector3(0, 1.86f, 0), Quaternion.identity, new Vector3(0.34f, 0.36f, 0.32f), true, "Head");
            MeshKit.Spawn(MeshKit.Cylinder, hat, root, new Vector3(0, 2.02f, 0), Quaternion.identity, new Vector3(0.62f, 0.03f, 0.62f), true, "Brim");
            MeshKit.Spawn(MeshKit.Cone, hat, root, new Vector3(0, 2.04f, 0), Quaternion.identity, new Vector3(0.3f, 0.22f, 0.3f), true, "Crown");
            MeshKit.Spawn(MeshKit.Box, straw, root, new Vector3(-0.78f, 1.43f, 0), Quaternion.Euler(0, 0, -25), new Vector3(0.12f, 0.16f, 0.1f), false, "StrawL");
            MeshKit.Spawn(MeshKit.Box, straw, root, new Vector3(0.78f, 1.43f, 0), Quaternion.Euler(0, 0, 25), new Vector3(0.12f, 0.16f, 0.1f), false, "StrawR");
        }

        private void BuildStakes(BuildingEntity b)
        {
            foreach (Transform c in _stakesGo.transform) Object.Destroy(c.gameObject);
            var wood = MaterialLibrary.ForKey("Wood");
            var rope = MaterialLibrary.ForKey("Rope");
            float hx = b.SizeX * 0.5f + 0.3f, hz = b.SizeZ * 0.5f + 0.3f;
            var c4 = new[] { new Vector2(-hx, hz), new Vector2(hx, hz), new Vector2(hx, -hz), new Vector2(-hx, -hz) };
            for (int i = 0; i < 4; i++)
            {
                var a = c4[i]; var c = c4[(i + 1) % 4];
                var pa = new Vector3(a.x, LocalGround(b, a.x, a.y), a.y);
                var pc = new Vector3(c.x, LocalGround(b, c.x, c.y), c.y);
                MeshKit.Spawn(MeshKit.Box, wood, _stakesGo.transform, pa + Vector3.up * 0.35f, Quaternion.identity, new Vector3(0.08f, 0.7f, 0.08f), true, "Stake");
                var m = (pa + pc) * 0.5f + Vector3.up * 0.55f;
                var d = pc - pa;
                MeshKit.Spawn(MeshKit.Box, rope, _stakesGo.transform, m, Quaternion.LookRotation(d.normalized), new Vector3(0.02f, 0.02f, d.magnitude), false, "String");
            }
        }

        private void LayoutCrops(BuildingEntity b)
        {
            _cropLocal.Clear(); _cropSeed.Clear();
            float hx = b.SizeX * 0.5f, hz = b.SizeZ * 0.5f;
            int seed = _id * 977;
            for (float x = -hx + Ridge * 0.5f; x < hx - 0.1f; x += Ridge)
            for (float z = -hz + 0.3f; z < hz - 0.2f; z += 0.38f)
            {
                int i = _cropLocal.Count;
                float jx = (Noise.Hash01(seed, i) - 0.5f) * 0.12f, jz = (Noise.Hash01(seed + 1, i) - 0.5f) * 0.12f;
                float lx = x + jx, lz = z + jz;
                _cropLocal.Add(new Vector3(lx, LocalGround(b, lx, lz) + 0.1f, lz));
                _cropSeed.Add(Noise.Hash01(seed + 2, i));
            }
        }

        public void Refresh(BuildingEntity b, bool initial)
        {
            bool site = b.State == BuildingState.Site;
            float progress = b.IsComplete ? 1f : b.Progress;
            if (site != _site || Mathf.Abs(progress - _progressShown) > 0.01f)
            {
                _site = site; _progressShown = progress;
                _stakesGo.SetActive(site);
                _soilGo.SetActive(!site || progress > 0.15f);
                // Fence posts/rails appear progressively while the plot is being prepared.
                int shown = site ? Mathf.FloorToInt(_fenceParts.Count * Mathf.InverseLerp(0.3f, 1f, progress)) : _fenceParts.Count;
                for (int i = 0; i < _fenceParts.Count; i++)
                {
                    bool on = i < shown;
                    if (_fenceParts[i] != null && _fenceParts[i].gameObject.activeSelf != on) _fenceParts[i].gameObject.SetActive(on);
                }
                if (!site && !initial && b.CompletedTick >= 0 && GameServices.Fx != null && _shownCrop == (CropState)(-1))
                    GameServices.Fx.Play(FxKind.Dust, _root.position, 12, 2f);
            }
            if (b.Crop != _shownCrop)
            {
                if (!initial && _shownCrop == CropState.Ripe && b.Crop == CropState.Fallow)
                {
                    GameServices.Fx?.Play(FxKind.Harvest, _root.position + Vector3.up * 0.6f, 30, 2.4f);
                    GameServices.Audio?.PlayAt("harvest", _root.position, 1f, 1f);
                }
                if (!initial && b.Crop == CropState.Growing && _shownCrop == CropState.Fallow) _cropAnim = 0f;
                _shownCrop = b.Crop;
                _shownGrowth = -1f;
            }
            float g = b.Crop == CropState.Fallow ? 0f : (b.Crop == CropState.Ripe ? 1f : Mathf.Clamp01(b.CropGrowth));
            if (Mathf.Abs(g - _shownGrowth) > 0.01f) { _shownGrowth = g; UpdateCropMatrices(b); }
        }

        private void UpdateCropMatrices(BuildingEntity b)
        {
            _crops.Matrices.Clear();
            if (b.State == BuildingState.Site || b.Crop == CropState.Fallow) { _crops.MarkDirty(); return; }
            float g = Mathf.Max(0f, _shownGrowth);
            float targetH = Mathf.Lerp(0.12f, 1.05f, Ease.OutCubic(g));
            _cropScale = targetH / _cropModelHeight;
            var rootM = _root.localToWorldMatrix;
            for (int i = 0; i < _cropLocal.Count; i++)
            {
                float s = _cropScale * Mathf.Lerp(0.8f, 1.15f, _cropSeed[i]) * Mathf.Lerp(0.2f, 1f, Ease.OutBack(_cropAnim));
                var m = rootM * Matrix4x4.TRS(_cropLocal[i], Quaternion.Euler(0, _cropSeed[i] * 360f, 0), new Vector3(s * 0.75f, s, s * 0.75f));
                _crops.Matrices.Add(m);
            }
            _crops.MarkDirty();
            // Colour: fresh green → golden as it ripens.
            float dry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, g));
            if (_cropMat.HasProperty(DrynessId)) _cropMat.SetFloat(DrynessId, dry);
            else MaterialLibrary.SetColor(_cropMat, Color.Lerp(new Color(0.5f, 0.72f, 0.36f), new Color(0.88f, 0.72f, 0.34f), dry));
        }

        public void Animate(float dt)
        {
            if (_cropAnim < 1f)
            {
                _cropAnim = Mathf.Min(1f, _cropAnim + dt * 1.2f);
                var b = _w.FindBuilding(new Core.EntityId(_id));
                if (b != null) UpdateCropMatrices(b);
            }
            _crops.Draw();
        }

        public void Dispose()
        {
            if (_soil != null) Object.Destroy(_soil);
            if (_cropMat != null) Object.Destroy(_cropMat);
        }
    }
}
