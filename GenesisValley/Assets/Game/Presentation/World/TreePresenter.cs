using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Trees: one GameObject per sim tree (V1 counts are a few hundred), Quaternius models normalised to GDD heights
    /// (6–12 m), LODGroup with a cheap blob canopy for distance, sapling growth, chop shake synced to the woodcutter's
    /// swing, a physical-feeling fall, and a stump that stays until the sim removes it.
    /// </summary>
    public sealed class TreePresenter : MonoBehaviour
    {
        public enum TreeVisual { Standing, Falling, Stump }

        public sealed class TreeView
        {
            public EntityId Id;
            public GameObject Root;
            public Transform Pivot;        // rotates for shake/fall, at the trunk base
            public Transform Model;
            public GameObject Stump;
            public TreeVisual Visual;
            public float Height;           // full-grown target height (m)
            public float ShownGrowth = -1f;
            public float Shake;            // 0..1 decaying impulse
            public Vector3 ShakeAxis;
            public float FallT;
            public Vector3 FallAxis;
            public float FallDelay;
            public float Spawn = 1f;       // pop-in 0..1
            public bool ImpactDone;
            public TreeSpecies Species;
            public float Radius;
        }

        private WorldState _w;
        private readonly Dictionary<EntityId, TreeView> _views = new Dictionary<EntityId, TreeView>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly List<EntityId> _remove = new List<EntityId>();
        private readonly List<TreeView> _dying = new List<TreeView>();
        private bool _initial;

        public static readonly float[] SpeciesHeight = { 8.2f, 9.5f, 7.2f };
        private static readonly string[] SpeciesPrefix = { "CommonTree_", "Pine_", "TwistedTree_" };

        public IReadOnlyDictionary<EntityId, TreeView> Views => _views;

        public void Bind(WorldState w)
        {
            _w = w;
            foreach (var v in _views.Values) if (v.Root != null) Destroy(v.Root);
            foreach (var v in _dying) if (v.Root != null) Destroy(v.Root);
            _views.Clear(); _dying.Clear();
            _initial = true;
            Sync();
            _initial = false;
        }

        public bool TryGetView(EntityId id, out TreeView v) => _views.TryGetValue(id, out v);

        private void LateUpdate()
        {
            if (_w == null) return;
            Sync();
            Animate(Time.deltaTime);
        }

        private void Sync()
        {
            _seen.Clear();
            foreach (var t in _w.Trees)
            {
                _seen.Add(t.Id);
                if (!_views.TryGetValue(t.Id, out var v))
                {
                    v = Create(t);
                    _views[t.Id] = v;
                    if (!_initial) v.Spawn = 0f;
                }
                // Growth (saplings) – only touch transforms when it visibly changed.
                if (v.Visual == TreeVisual.Standing)
                {
                    if (t.IsStump) BeginFall(v, t);
                    else if (Mathf.Abs(t.Growth - v.ShownGrowth) > 0.004f) { v.ShownGrowth = t.Growth; ApplyGrowth(v); }
                }
            }
            _remove.Clear();
            foreach (var kv in _views) if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var id in _remove)
            {
                var v = _views[id];
                _views.Remove(id);
                // Undo of a planted forest or a rotted stump: shrink away instead of popping.
                v.Spawn = Mathf.Min(v.Spawn, 1f);
                v.FallDelay = -1f;
                _dying.Add(v);
            }
        }

        private void Animate(float dt)
        {
            foreach (var v in _views.Values)
            {
                if (v.Spawn < 1f)
                {
                    v.Spawn = Mathf.Min(1f, v.Spawn + dt * 1.6f);
                    ApplyGrowth(v);
                }
                if (v.Shake > 0f)
                {
                    v.Shake = Mathf.Max(0f, v.Shake - dt * 2.2f);
                    float wob = Mathf.Sin(Time.time * 38f) * v.Shake * v.Shake * 2.4f;
                    if (v.Visual == TreeVisual.Standing) v.Pivot.localRotation = Quaternion.AngleAxis(wob, v.ShakeAxis);
                }
                if (v.Visual == TreeVisual.Falling) AnimateFall(v, dt);
            }
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var v = _dying[i];
                v.Spawn -= dt * 1.4f;
                if (v.Root == null || v.Spawn <= 0f)
                {
                    if (v.Root != null) Destroy(v.Root);
                    _dying.RemoveAt(i);
                    continue;
                }
                float s = Ease.OutCubic(v.Spawn);
                v.Root.transform.localScale = new Vector3(s, s, s);
            }
        }

        // ------------------------------------------------------------------ creation

        private TreeView Create(TreeEntity t)
        {
            var v = new TreeView { Id = t.Id, Species = t.Species };
            v.Root = new GameObject("Tree_" + t.Id.Value);
            v.Root.transform.SetParent(transform, false);
            v.Root.transform.position = Ground(t.X, t.Z);
            v.Root.transform.rotation = Quaternion.Euler(0, t.RotationDegrees, 0);
            v.Pivot = new GameObject("Pivot").transform;
            v.Pivot.SetParent(v.Root.transform, false);
            v.Height = SpeciesHeight[(int)t.Species] * t.Scale;
            v.Radius = 0.22f + 0.04f * v.Height;

            var prefab = GameServices.Art.Model(SpeciesPrefix[(int)t.Species] + (Mathf.Abs(t.Variant) % 5 + 1));
            if (prefab == null) prefab = GameServices.Art.Model(SpeciesPrefix[(int)t.Species] + "1");
            GameObject model;
            if (prefab != null)
            {
                model = Instantiate(prefab, v.Pivot, false);
                var b = ModelBounds.Of(prefab);
                float k = b.size.y > 0.01f ? v.Height / b.size.y : 1f;
                model.transform.localScale = prefab.transform.localScale * k;
                model.transform.localPosition = new Vector3(0, -b.min.y * k - 0.05f, 0);
                StripColliders(model);
                AddLod(v, model, t);
            }
            else model = BuildFallbackTree(v.Pivot, t, v.Height);
            model.name = "Model";
            v.Model = model.transform;
            v.ShownGrowth = t.Growth;
            if (t.IsStump)
            {
                v.Visual = TreeVisual.Stump;
                foreach (Transform c in v.Pivot) c.gameObject.SetActive(false);
                var lod = v.Root.GetComponent<LODGroup>();
                if (lod != null) lod.enabled = false;
                ShowStump(v);
            }
            ApplyGrowth(v);
            return v;
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        /// <summary>LOD1: a few lit blobs + trunk so a 150 m-distant forest costs ~200 triangles per tree.</summary>
        private void AddLod(TreeView v, GameObject model, TreeEntity t)
        {
            var lod1 = new GameObject("LOD1");
            lod1.transform.SetParent(v.Pivot, false);
            BuildBlobCanopy(lod1.transform, t, v.Height, false);
            var group = v.Root.AddComponent<LODGroup>();
            var r0 = model.GetComponentsInChildren<Renderer>();
            var r1 = lod1.GetComponentsInChildren<Renderer>();
            foreach (var r in r1) r.shadowCastingMode = ShadowCastingMode.On;
            group.SetLODs(new[]
            {
                new LOD(0.055f, r0),
                new LOD(0.0025f, r1),
            });
            group.RecalculateBounds();
        }

        private static readonly Color[] LeafTint = { Palette.CanopyA, Palette.CanopyB, new Color(0.56f, 0.55f, 0.36f) };

        private static void BuildBlobCanopy(Transform parent, TreeEntity t, float h, bool hiDetail)
        {
            var bark = MaterialLibrary.ForKey("Bark");
            int seed = t.Id.Value;
            var leafColor = Color.Lerp(LeafTint[(int)t.Species], Color.white, (Noise.Hash01(seed, 3) - 0.5f) * 0.12f);
            var leaf = MaterialLibrary.Lit(leafColor, 0.05f);
            float trunkH = t.Species == TreeSpecies.Pine ? h * 0.35f : h * 0.45f;
            float trunkD = 0.18f + 0.035f * h;
            MeshKit.Spawn(MeshKit.Cylinder, new[] { bark, bark }, parent, new Vector3(0, trunkH / 2, 0), Quaternion.identity, new Vector3(trunkD, trunkH, trunkD), true, "Trunk");
            if (t.Species == TreeSpecies.Pine)
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = h * (0.28f + i * 0.22f);
                    float r = h * (0.36f - i * 0.09f);
                    MeshKit.Spawn(MeshKit.Cone, leaf, parent, new Vector3(0, y, 0), Quaternion.Euler(0, i * 40f, 0), new Vector3(r * 2, h * 0.38f, r * 2), true, "Tier");
                }
            }
            else
            {
                int blobs = hiDetail ? 6 : 4;
                for (int i = 0; i < blobs; i++)
                {
                    float a = (i / (float)blobs) * Mathf.PI * 2f + Noise.Hash01(seed, i) * 0.8f;
                    float rr = i == 0 ? 0f : h * 0.17f;
                    float y = h * (i == 0 ? 0.72f : 0.6f + 0.1f * Noise.Hash01(seed, i + 7));
                    float s = h * (i == 0 ? 0.48f : 0.34f + 0.06f * Noise.Hash01(seed, i + 11));
                    var p = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                    MeshKit.Spawn(MeshKit.Sphere, leaf, parent, p, Quaternion.identity, new Vector3(s, s * 0.82f, s), true, "Blob");
                }
            }
        }

        private static GameObject BuildFallbackTree(Transform parent, TreeEntity t, float h)
        {
            var go = new GameObject("FallbackTree");
            go.transform.SetParent(parent, false);
            BuildBlobCanopy(go.transform, t, h, true);
            return go;
        }

        private void ApplyGrowth(TreeView v)
        {
            if (v.Root == null) return;
            float g = Mathf.Clamp01(v.ShownGrowth);
            float s = Mathf.Lerp(0.14f, 1f, Ease.OutCubic(g));
            float pop = v.Spawn < 1f ? Ease.OutBack(v.Spawn, 2.2f) : 1f;
            if (v.Visual == TreeVisual.Stump) { v.Root.transform.localScale = Vector3.one; return; }
            v.Pivot.localScale = new Vector3(s, s, s) * pop;
        }

        // ------------------------------------------------------------------ chopping

        /// <summary>Called by the woodcutter's view at the moment the axe connects.</summary>
        public void Hit(EntityId id, Vector3 from, float strength = 1f)
        {
            if (!_views.TryGetValue(id, out var v) || v.Visual != TreeVisual.Standing) return;
            var pos = v.Root.transform.position;
            var dir = pos - from; dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            v.ShakeAxis = Vector3.Cross(Vector3.up, dir);
            v.Shake = Mathf.Clamp01(v.Shake + 0.55f * strength);
            var hitPoint = pos - dir * v.Radius + Vector3.up * 0.9f;
            if (GameServices.Fx != null)
            {
                GameServices.Fx.Play(FxKind.Chips, hitPoint, 7);
                if (Random.value < 0.45f) GameServices.Fx.Play(FxKind.Leaves, pos + Vector3.up * v.Height * 0.7f, 3, 1.2f);
            }
            GameServices.Audio?.PlayAt("chop", hitPoint, 1f, Random.Range(0.92f, 1.08f));
        }

        private void BeginFall(TreeView v, TreeEntity t)
        {
            v.Visual = TreeVisual.Falling;
            v.FallT = 0f;
            v.FallDelay = 0.25f;
            v.ImpactDone = false;
            // Fall away from the woodcutter who felled it (if known), else a deterministic direction.
            Vector3 away = Quaternion.Euler(0, Noise.Hash01(t.Id.Value, 5) * 360f, 0) * Vector3.forward;
            foreach (var vill in _w.Villagers)
            {
                if (vill.TargetId == t.Id && vill.Action == VillagerAction.Chop)
                {
                    var d = new Vector3(t.X - vill.X, 0, t.Z - vill.Z);
                    if (d.sqrMagnitude > 0.01f) away = d.normalized;
                    break;
                }
            }
            v.FallAxis = Vector3.Cross(Vector3.up, away);
            ShowStump(v);
            GameServices.Audio?.PlayAt("tree_creak", v.Root.transform.position + Vector3.up * 2f, 1f, Random.Range(0.9f, 1.05f));
        }

        private void AnimateFall(TreeView v, float dt)
        {
            if (v.FallDelay > 0f) { v.FallDelay -= dt; v.Pivot.localRotation = Quaternion.AngleAxis(Mathf.Sin(Time.time * 30f) * 0.8f, v.FallAxis); return; }
            v.FallT += dt;
            // Angular acceleration like a hinged rod: slow start, fast finish, small bounce.
            const float fallTime = 1.9f;
            float angle;
            if (v.FallT < fallTime)
            {
                float t = v.FallT / fallTime;
                angle = 86f * t * t * t * (0.35f + 0.65f * t) / 1f;
            }
            else
            {
                if (!v.ImpactDone)
                {
                    v.ImpactDone = true;
                    var pos = v.Root.transform.position;
                    var fallDir = Vector3.Cross(v.FallAxis, Vector3.up).normalized;
                    var crown = pos + fallDir * v.Height * 0.65f;
                    if (GameServices.Fx != null)
                    {
                        GameServices.Fx.Play(FxKind.Dust, crown, 14, 1.6f);
                        GameServices.Fx.Play(FxKind.Leaves, crown + Vector3.up, 16, 1.6f);
                    }
                    GameServices.Audio?.PlayAt("tree_fall", crown, 1f, Random.Range(0.9f, 1.05f));
                    GameServices.CameraRig?.AddShake(0.12f, crown);
                }
                float bt = (v.FallT - fallTime) / 0.5f;
                angle = 86f - 4f * Mathf.Sin(Mathf.Clamp01(bt) * Mathf.PI) * (1f - Mathf.Clamp01(bt));
            }
            v.Pivot.localRotation = Quaternion.AngleAxis(angle, v.FallAxis);
            // Lie for a moment, then sink + shrink (the logs "become" carried wood).
            float rest = v.FallT - fallTime - 2.2f;
            if (rest > 0f)
            {
                float k = Mathf.Clamp01(rest / 1.4f);
                v.Pivot.localScale = Vector3.one * Mathf.Lerp(1f, 0.001f, Ease.InCubic(k));
                v.Pivot.localPosition = new Vector3(0, -0.6f * k, 0);
                if (k >= 1f)
                {
                    v.Model.gameObject.SetActive(false);
                    var lod = v.Root.GetComponent<LODGroup>();
                    if (lod != null) lod.enabled = false;
                    foreach (Transform c in v.Pivot) c.gameObject.SetActive(false);
                    v.Visual = TreeVisual.Stump;
                }
            }
        }

        private void ShowStump(TreeView v)
        {
            if (v.Stump != null) { v.Stump.SetActive(true); return; }
            var bark = MaterialLibrary.ForKey("Bark");
            var grain = MaterialLibrary.ForKey("EndGrain");
            float d = v.Radius * 2.1f;
            var tr = MeshKit.Spawn(MeshKit.Cylinder, new[] { bark, grain }, v.Root.transform, new Vector3(0, 0.22f, 0), Quaternion.Euler(Random.Range(-4f, 4f), 0, Random.Range(-4f, 4f)), new Vector3(d, 0.55f, d), true, "Stump");
            v.Stump = tr.gameObject;
            // A couple of chips lying around.
            var plank = MaterialLibrary.ForKey("Plank");
            for (int i = 0; i < 3; i++)
            {
                float a = Noise.Hash01(v.Id.Value, 20 + i) * Mathf.PI * 2f;
                float r = d * 0.8f + Noise.Hash01(v.Id.Value, 30 + i) * 0.5f;
                MeshKit.Spawn(MeshKit.Box, plank, v.Stump.transform, new Vector3(Mathf.Cos(a) * r / d, -0.38f, Mathf.Sin(a) * r / d), Quaternion.Euler(0, a * 57f, 0), new Vector3(0.12f / d, 0.03f / 0.55f, 0.06f / d), false, "Chip");
            }
        }

        // ------------------------------------------------------------------ ground

        private Vector3 Ground(float x, float z) => new Vector3(x, _w.Height.SampleHeight(x, z), z);

        /// <summary>Re-seat trees after terrain edits inside <paramref name="r"/>.</summary>
        public void Resnap(Rect r)
        {
            if (_w == null) return;
            foreach (var t in _w.Trees)
            {
                if (!r.Contains(new Vector2(t.X, t.Z))) continue;
                if (_views.TryGetValue(t.Id, out var v) && v.Root != null) v.Root.transform.position = Ground(t.X, t.Z);
            }
        }

        public float CanopyHeight(EntityId id) => _views.TryGetValue(id, out var v) ? v.Height * Mathf.Lerp(0.14f, 1f, Mathf.Clamp01(v.ShownGrowth)) : 6f;
    }
}
