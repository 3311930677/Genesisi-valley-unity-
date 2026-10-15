using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Presentation.Blueprints;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Buildings, construction sites and bridges assembled from <see cref="BuildingBlueprints"/>. Pieces appear as the
    /// sim's WorkDone advances (foundation → walls → roof, GDD §13), with a small pop + dust + hammer knock. Completed,
    /// occupied homes get warm windows, a door lantern and chimney smoke at night (GDD §7.3).
    /// </summary>
    public sealed class BuildingPresenter : MonoBehaviour
    {
        internal sealed class Piece
        {
            public GameObject Go;
            public BuildPhase Phase;
            public float RevealAt;
            public bool Shown;
            public float Pop = 1f;
            public Vector3 BasePos;
            public Vector3 BaseScale;
        }

        public sealed class BuildingView
        {
            public EntityId Id;
            public BuildingKind Kind;
            public GameObject Root;
            public Blueprint Bp;
            internal readonly List<Piece> Pieces = new List<Piece>();
            public GameObject WoodStack;
            public int ShownWood = -1;
            public readonly List<GameObject> GlowPanes = new List<GameObject>();
            public readonly List<Renderer> GlassRenderers = new List<Renderer>();
            public GameObject Lantern;
            public Light LanternLight;
            public ParticleSystem Smoke;
            public bool WasComplete;
            public float Bounce = 1f;
            public float LastKnock;
            public FieldView Field;
            public float GlowLevel;
            public float Height;
            public bool Fallback;
        }

        private WorldState _w;
        private readonly Dictionary<EntityId, BuildingView> _views = new Dictionary<EntityId, BuildingView>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly List<EntityId> _remove = new List<EntityId>();
        private readonly List<BuildingView> _byDistance = new List<BuildingView>();
        private bool _initial;
        private Material _glowMat, _glassNight, _lanternMat;
        public int MaxLanternLights = 8;

        public IReadOnlyDictionary<EntityId, BuildingView> Views => _views;

        public void Bind(WorldState w)
        {
            _w = w;
            foreach (var v in _views.Values) DestroyView(v);
            _views.Clear();
            _initial = true;
            Sync();
            _initial = false;
        }

        public bool TryGetView(EntityId id, out BuildingView v) => _views.TryGetValue(id, out v);

        private void DestroyView(BuildingView v)
        {
            if (v.Field != null) v.Field.Dispose();
            if (v.Root != null) Destroy(v.Root);
        }

        private void LateUpdate()
        {
            if (_w == null) return;
            Sync();
            float dt = Time.deltaTime;
            float night = GameServices.Sky != null ? GameServices.Sky.NightFactor : 0f;
            foreach (var v in _views.Values) Animate(v, dt, night);
            UpdateLanternLights(night);
        }

        private void Sync()
        {
            _seen.Clear();
            foreach (var b in _w.Buildings)
            {
                _seen.Add(b.Id);
                if (!_views.TryGetValue(b.Id, out var v))
                {
                    v = Create(b);
                    _views[b.Id] = v;
                    if (!_initial && GameServices.Fx != null) GameServices.Fx.Play(FxKind.Poof, v.Root.transform.position + Vector3.up * 0.3f, 10, 1.4f);
                }
                Refresh(v, b);
            }
            _remove.Clear();
            foreach (var kv in _views) if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var id in _remove)
            {
                var v = _views[id];
                if (GameServices.Fx != null && v.Root != null) GameServices.Fx.Play(FxKind.Dust, v.Root.transform.position, 20, 2f);
                DestroyView(v);
                _views.Remove(id);
            }
        }

        // ------------------------------------------------------------------ creation

        private BuildingView Create(BuildingEntity b)
        {
            var v = new BuildingView { Id = b.Id, Kind = b.Kind };
            v.Root = new GameObject(BuildingName(b));
            v.Root.transform.SetParent(transform, false);
            PlaceRoot(v, b);

            switch (b.Kind)
            {
                case BuildingKind.House: v.Bp = BuildingBlueprints.House(b.Variant); break;
                case BuildingKind.Storehouse: v.Bp = BuildingBlueprints.Storehouse(b.Variant); break;
                case BuildingKind.FishingHut: v.Bp = BuildingBlueprints.FishingHut(b.Variant, JettyLength(b)); break;
                case BuildingKind.Bridge: v.Bp = BuildingBlueprints.Bridge(BridgeLength(b), BridgeRise(b)); break;
                case BuildingKind.Field: v.Field = new FieldView(_w, b, v.Root.transform); break;
            }
            if (v.Bp != null)
            {
                int missing = 0, models = 0;
                foreach (var p in v.Bp.Pieces) if (!string.IsNullOrEmpty(p.Model)) { models++; if (!GameServices.Art.HasModel(p.Model)) missing++; }
                v.Fallback = models > 0 && missing > models / 2;
                if (v.Fallback) BuildFallback(v, b);
                else foreach (var p in v.Bp.Pieces) AddPiece(v, p);
                v.Height = v.Bp.Height;
                if (v.Bp.HasSmoke && GameServices.Fx != null)
                {
                    v.Smoke = GameServices.Fx.CreateChimneySmoke(v.Root.transform, new Vector3(v.Bp.SmokeX, v.Bp.SmokeY, v.Bp.SmokeZ));
                    if (v.Smoke != null) v.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (b.Kind == BuildingKind.House || b.Kind == BuildingKind.FishingHut) AddLantern(v, b);
            }
            else v.Height = 1.2f;
            v.WasComplete = b.IsComplete;
            return v;
        }

        private static string BuildingName(BuildingEntity b) => b.Kind + "_" + b.Id.Value;

        private void PlaceRoot(BuildingView v, BuildingEntity b)
        {
            if (b.Kind == BuildingKind.Bridge)
            {
                float len = BridgeLength(b);
                float pitch = -Mathf.Atan2(b.BY - b.AY, Mathf.Max(0.1f, len)) * Mathf.Rad2Deg;
                v.Root.transform.position = new Vector3((b.AX + b.BX) * 0.5f, (b.AY + b.BY) * 0.5f + 0.18f, (b.AZ + b.BZ) * 0.5f);
                v.Root.transform.rotation = Quaternion.Euler(pitch, b.Yaw, 0);
            }
            else if (b.Kind == BuildingKind.Field)
            {
                v.Root.transform.position = new Vector3(b.X, _w.Height.SampleHeight(b.X, b.Z), b.Z);
                v.Root.transform.rotation = Quaternion.Euler(0, b.Yaw, 0);
            }
            else
            {
                v.Root.transform.position = new Vector3(b.X, b.PadHeight, b.Z);
                v.Root.transform.rotation = Quaternion.Euler(0, b.Yaw, 0);
            }
        }

        public static float BridgeLength(BuildingEntity b) => Mathf.Sqrt((b.BX - b.AX) * (b.BX - b.AX) + (b.BZ - b.AZ) * (b.BZ - b.AZ));
        public static float BridgeRise(BuildingEntity b) => Mathf.Clamp(BridgeLength(b) * 0.06f, 0.25f, 0.85f);

        private float JettyLength(BuildingEntity b)
        {
            // March from the back wall (local −Z) until we are ~1.5 m over water.
            float back = b.SizeZ * 0.5f + 0.2f;
            for (float s = 0.5f; s < 8f; s += 0.5f)
            {
                b.ToWorld(0, -back - s, out float x, out float z);
                var cell = _w.Nav.WorldToCell(x, z);
                if (_w.Nav.IsWater(cell.X, cell.Z))
                    return Mathf.Clamp(s + 1.6f, 2.5f, 7f);
            }
            return 3.5f;
        }

        private void AddPiece(BuildingView v, PiecePlacement p)
        {
            GameObject go = null;
            var rot = Quaternion.Euler(p.Pitch, p.Yaw, p.Roll);
            var pos = new Vector3(p.X, p.Y, p.Z);
            if (!string.IsNullOrEmpty(p.Model))
            {
                var prefab = GameServices.Art.Model(p.Model);
                if (prefab == null) return;
                go = Instantiate(prefab, v.Root.transform, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = rot * prefab.transform.localRotation;
                go.transform.localScale = prefab.transform.localScale * (p.SX > 0 ? p.SX : 1f);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
                if (p.NightGlow) RegisterWindow(v, go, p);
            }
            else if (p.Shape != PrimitiveShape.None)
            {
                var mat = MaterialLibrary.ForKey(string.IsNullOrEmpty(p.Material) ? "Wood" : p.Material);
                var scale = new Vector3(p.SX, p.SY, p.SZ);
                if (p.Shape == PrimitiveShape.Box) go = MeshKit.Spawn(MeshKit.Box, mat, v.Root.transform, pos, rot, scale, true, "Box").gameObject;
                else go = MeshKit.Spawn(MeshKit.Cylinder, new[] { mat, MaterialLibrary.ForKey("EndGrain") }, v.Root.transform, pos, rot, scale, true, "Pole").gameObject;
            }
            if (go == null) return;
            var piece = new Piece { Go = go, Phase = p.Phase, RevealAt = p.RevealAt, BasePos = go.transform.localPosition, BaseScale = go.transform.localScale };
            go.SetActive(false);
            v.Pieces.Add(piece);
        }

        private void RegisterWindow(BuildingView v, GameObject window, PiecePlacement p)
        {
            foreach (var r in window.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) { v.GlassRenderers.Add(r); break; }
            }
            // Warm pane just inside the wall: seen through the (transparent) glass at night.
            bool wide = p.Model != null && p.Model.Contains("Wide");
            var rot = Quaternion.Euler(0, p.Yaw, 0);
            var local = new Vector3(p.X, p.Y + 1.8f, p.Z) + rot * new Vector3(0, 0, -0.45f);
            var pane = MeshKit.Spawn(MeshKit.Box, GlowMaterial, v.Root.transform, local, rot, new Vector3(wide ? 1.5f : 0.85f, 1.55f, 0.02f), false, "GlowPane");
            pane.GetComponent<Renderer>().receiveShadows = false;
            pane.gameObject.SetActive(false);
            v.GlowPanes.Add(pane.gameObject);
        }

        private Material GlowMaterial => _glowMat != null ? _glowMat : (_glowMat = MaterialLibrary.Emissive(new Color(0.25f, 0.16f, 0.06f), Palette.NightLamp * 2.6f));
        private Material LanternMaterial => _lanternMat != null ? _lanternMat : (_lanternMat = MaterialLibrary.Emissive(new Color(0.3f, 0.22f, 0.1f), Palette.NightLamp * 3.2f));

        private void AddLantern(BuildingView v, BuildingEntity b)
        {
            float dz = b.SizeZ * 0.5f + 0.35f;
            float dx = b.Kind == BuildingKind.House ? -1.05f : -1.75f;
            var root = new GameObject("Lantern").transform;
            root.SetParent(v.Root.transform, false);
            root.localPosition = new Vector3(dx, 2.35f, dz);
            var wood = MaterialLibrary.ForKey("DarkWood");
            MeshKit.Spawn(MeshKit.Box, wood, root, new Vector3(0, 0.22f, -0.2f), Quaternion.identity, new Vector3(0.05f, 0.05f, 0.45f), true, "Bracket");
            MeshKit.Spawn(MeshKit.Box, wood, root, new Vector3(0, 0.12f, 0), Quaternion.identity, new Vector3(0.2f, 0.04f, 0.2f), true, "Cap");
            var glow = MeshKit.Spawn(MeshKit.Box, LanternMaterial, root, new Vector3(0, -0.05f, 0), Quaternion.identity, new Vector3(0.14f, 0.24f, 0.14f), false, "Glow");
            v.Lantern = glow.gameObject;
            v.Lantern.SetActive(false);
            var lgo = new GameObject("Light");
            lgo.transform.SetParent(root, false);
            lgo.transform.localPosition = new Vector3(0, -0.1f, 0.25f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Palette.NightLamp;
            l.range = 9f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            v.LanternLight = l;
            root.gameObject.SetActive(false);
        }

        /// <summary>Simple massing used only when the art catalog lacks the modular kit (fresh clone before setup).</summary>
        private void BuildFallback(BuildingView v, BuildingEntity b)
        {
            v.Pieces.Clear();
            float w = v.Bp.Width, d = v.Bp.Depth;
            if (b.Kind == BuildingKind.Bridge)
            {
                foreach (var p in v.Bp.Pieces) if (string.IsNullOrEmpty(p.Model)) AddPiece(v, p);
                return;
            }
            foreach (var p in v.Bp.Pieces) if (string.IsNullOrEmpty(p.Model)) AddPiece(v, p); // stakes, scaffold, jetty
            var wall = MaterialLibrary.Lit(b.Kind == BuildingKind.Storehouse ? new Color(0.72f, 0.56f, 0.45f) : Palette.Wall, 0.1f);
            var roof = MaterialLibrary.Lit(Palette.Roof, 0.2f);
            var wood = MaterialLibrary.ForKey("DarkWood");
            float ww = Mathf.Min(w, 6f), dd = Mathf.Min(d, 8f);
            float wh = b.Kind == BuildingKind.House && b.Variant % 3 == 2 ? 5.6f : 3.0f;
            if (b.Kind == BuildingKind.FishingHut) { ww = 4f; dd = 4f; wh = 2.8f; }
            AddFallbackPiece(v, MeshKit.Box, wood, new Vector3(0, 0.1f, 0), Quaternion.identity, new Vector3(ww + 0.3f, 0.2f, dd + 0.3f), BuildPhase.Foundation, 0.1f);
            AddFallbackPiece(v, MeshKit.Box, wall, new Vector3(0, wh / 2, 0), Quaternion.identity, new Vector3(ww, wh, dd), BuildPhase.Walls, 0.4f);
            AddFallbackPiece(v, MeshKit.Box, wood, new Vector3(0, 1.05f, dd / 2 + 0.03f), Quaternion.identity, new Vector3(1.1f, 2.1f, 0.08f), BuildPhase.Walls, 0.7f);
            float rh = ww * 0.42f;
            float slope = Mathf.Atan2(rh, ww / 2) * Mathf.Rad2Deg;
            float slab = Mathf.Sqrt(rh * rh + ww * ww / 4) + 0.35f;
            AddFallbackPiece(v, MeshKit.Box, roof, new Vector3(-ww / 4, wh + rh / 2, 0), Quaternion.Euler(0, 0, slope), new Vector3(slab, 0.2f, dd + 0.6f), BuildPhase.Roof, 0.85f);
            AddFallbackPiece(v, MeshKit.Box, roof, new Vector3(ww / 4, wh + rh / 2, 0), Quaternion.Euler(0, 0, -slope), new Vector3(slab, 0.2f, dd + 0.6f), BuildPhase.Roof, 0.9f);
            v.Bp.Height = wh + rh;
        }

        private void AddFallbackPiece(BuildingView v, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, BuildPhase phase, float reveal)
        {
            var go = MeshKit.Spawn(mesh, mat, v.Root.transform, pos, rot, scale, true, phase.ToString()).gameObject;
            go.SetActive(false);
            v.Pieces.Add(new Piece { Go = go, Phase = phase, RevealAt = reveal, BasePos = pos, BaseScale = scale });
        }

        // ------------------------------------------------------------------ per-frame state

        private void Refresh(BuildingView v, BuildingEntity b)
        {
            if (v.Field != null) { v.Field.Refresh(b, _initial); return; }
            bool site = b.State == BuildingState.Site;
            float progress = b.IsComplete ? 1f : b.Progress;
            for (int i = 0; i < v.Pieces.Count; i++)
            {
                var p = v.Pieces[i];
                bool want;
                switch (p.Phase)
                {
                    case BuildPhase.Site: want = site; break;
                    case BuildPhase.Scaffold: want = site && progress >= p.RevealAt; break;
                    default: want = !site || progress >= p.RevealAt - 1e-4f; break;
                }
                if (want == p.Shown) continue;
                p.Shown = want;
                p.Go.SetActive(want);
                if (want && !_initial)
                {
                    p.Pop = 0f;
                    if (site && Time.time - v.LastKnock > 0.22f)
                    {
                        v.LastKnock = Time.time;
                        var wp = p.Go.transform.position;
                        GameServices.Audio?.PlayAt(p.Phase == BuildPhase.Roof ? "build_tile" : "build_knock", wp, 0.8f, Random.Range(0.9f, 1.1f));
                        if (GameServices.Fx != null && Random.value < 0.5f) GameServices.Fx.Play(FxKind.Dust, wp, 3, 0.6f);
                    }
                }
                else { p.Pop = 1f; ResetPiece(p); }
            }
            // Site wood pile ∝ delivered-but-unused wood.
            int wood = site && b.Site != null ? b.Site.Get(ItemType.Wood) : 0;
            if (wood != v.ShownWood) { v.ShownWood = wood; RebuildWoodStack(v, b, wood); }

            if (b.IsComplete && !v.WasComplete)
            {
                v.WasComplete = true;
                if (!_initial)
                {
                    v.Bounce = 0f;
                    var top = v.Root.transform.position + Vector3.up * (v.Height * 0.6f);
                    if (GameServices.Fx != null)
                    {
                        GameServices.Fx.Play(FxKind.Sparkle, top, 40, Mathf.Max(2f, v.Bp != null ? v.Bp.Width * 0.5f : 2f));
                        GameServices.Fx.Play(FxKind.Poof, v.Root.transform.position + Vector3.up * 0.5f, 18, 2.5f);
                    }
                    GameServices.Audio?.PlayAt("build_complete", top, 1f, 1f);
                }
            }
            else if (!b.IsComplete) v.WasComplete = false;
        }

        private static void ResetPiece(Piece p)
        {
            p.Go.transform.localPosition = p.BasePos;
            p.Go.transform.localScale = p.BaseScale;
        }

        private void Animate(BuildingView v, float dt, float night)
        {
            if (v.Field != null) { v.Field.Animate(dt); return; }
            foreach (var p in v.Pieces)
            {
                if (p.Pop >= 1f || !p.Shown) continue;
                p.Pop = Mathf.Min(1f, p.Pop + dt * 3.2f);
                float s = Ease.OutBack(p.Pop, 2.4f);
                p.Go.transform.localScale = p.BaseScale * Mathf.Max(0.01f, s);
                p.Go.transform.localPosition = p.BasePos + Vector3.up * (1f - Ease.OutCubic(p.Pop)) * 0.45f;
            }
            if (v.Bounce < 1f)
            {
                v.Bounce = Mathf.Min(1f, v.Bounce + dt * 1.4f);
                float k = Mathf.Sin(v.Bounce * Mathf.PI * 3f) * (1f - v.Bounce) * 0.06f;
                v.Root.transform.localScale = new Vector3(1f - k * 0.5f, 1f + k, 1f - k * 0.5f);
            }

            var b = _w.FindBuilding(v.Id);
            if (b == null) return;
            bool lived = b.IsComplete && (b.Residents.Count > 0 || b.Kind == BuildingKind.FishingHut);
            bool anyoneHome = false;
            if (lived) foreach (var r in b.Residents) { var vill = _w.FindVillager(r); if (vill != null && vill.InsideHome) { anyoneHome = true; break; } }
            // Windows glow when it is dark and people live here; brighter when someone is inside.
            float targetGlow = lived ? night * (anyoneHome ? 1f : 0.55f) : 0f;
            if (lived && night < 0.05f && b.Kind == BuildingKind.House && _w.Hour > 4.5f && _w.Hour < 6.5f && anyoneHome) targetGlow = 0.6f;
            v.GlowLevel = Mathf.MoveTowards(v.GlowLevel, targetGlow, dt * 0.6f);
            bool glowOn = v.GlowLevel > 0.08f;
            foreach (var g in v.GlowPanes) if (g.activeSelf != glowOn) g.SetActive(glowOn);
            if (v.Lantern != null)
            {
                var root = v.Lantern.transform.parent.gameObject;
                if (b.IsComplete != root.activeSelf) root.SetActive(b.IsComplete);
                bool lit = lived && night > 0.25f;
                if (v.Lantern.activeSelf != lit) v.Lantern.SetActive(lit);
            }
            if (v.Smoke != null)
            {
                bool smoke = lived && (anyoneHome || night > 0.3f || (_w.Hour > 6f && _w.Hour < 8f) || (_w.Hour > 17.5f && _w.Hour < 20f));
                if (smoke && !v.Smoke.isEmitting) v.Smoke.Play(true);
                else if (!smoke && v.Smoke.isEmitting) v.Smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void UpdateLanternLights(float night)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _byDistance.Clear();
            foreach (var v in _views.Values) if (v.LanternLight != null) _byDistance.Add(v);
            var cp = cam.transform.position;
            _byDistance.Sort((a, c) => (a.Root.transform.position - cp).sqrMagnitude.CompareTo((c.Root.transform.position - cp).sqrMagnitude));
            for (int i = 0; i < _byDistance.Count; i++)
            {
                var v = _byDistance[i];
                bool lit = v.Lantern != null && v.Lantern.activeInHierarchy;
                bool on = lit && i < MaxLanternLights;
                float flicker = 1f + 0.06f * Mathf.Sin(Time.time * 7.3f + v.Id.Value) + 0.04f * Mathf.Sin(Time.time * 13.1f + v.Id.Value * 1.7f);
                float target = on ? 1.6f * Mathf.Clamp01(night * 1.4f) * flicker : 0f;
                v.LanternLight.intensity = Mathf.MoveTowards(v.LanternLight.intensity, target, Time.deltaTime * 3f);
                v.LanternLight.enabled = v.LanternLight.intensity > 0.01f;
            }
        }

        private void RebuildWoodStack(BuildingView v, BuildingEntity b, int wood)
        {
            if (v.WoodStack != null) Destroy(v.WoodStack);
            v.WoodStack = null;
            if (wood <= 0) return;
            var stack = new GameObject("SiteWood");
            stack.transform.SetParent(v.Root.transform, false);
            // Front-right corner outside the footprint (bridges: at the A bank end).
            if (b.Kind == BuildingKind.Bridge) stack.transform.localPosition = new Vector3(1.9f, -0.15f, -BridgeLength(b) * 0.5f - 0.6f);
            else stack.transform.localPosition = new Vector3(b.SizeX * 0.5f + 1.5f, 0f, b.SizeZ * 0.5f + 0.6f);
            int logs = Mathf.Clamp(Mathf.CeilToInt(wood / 2f), 1, 15);
            int i = 0;
            for (int row = 0; i < logs; row++)
            {
                int inRow = Mathf.Max(1, 5 - row);
                for (int k = 0; k < inRow && i < logs; k++, i++)
                {
                    float x = (k - (inRow - 1) * 0.5f) * 0.3f;
                    float y = 0.14f + row * 0.25f;
                    MeshKit.SpawnLog(stack.transform, new Vector3(x, y, 0), Quaternion.Euler(0, Noise.Hash01(b.Id.Value, i) * 8f - 4f, 0), 1.8f, 0.27f);
                }
            }
            v.WoodStack = stack;
        }

        /// <summary>Terrain under fields changed (or a bridge bank) – rebuild ground-conforming parts.</summary>
        public void Resnap(Rect r)
        {
            if (_w == null) return;
            foreach (var b in _w.Buildings)
            {
                if (!_views.TryGetValue(b.Id, out var v)) continue;
                float rad = b.Kind == BuildingKind.Bridge ? BridgeLength(b) * 0.5f + 2f : b.BoundingRadius + 2f;
                var rr = Rect.MinMaxRect(b.X - rad, b.Z - rad, b.X + rad, b.Z + rad);
                if (!rr.Overlaps(r)) continue;
                if (b.Kind == BuildingKind.Field) { PlaceRoot(v, b); v.Field.Rebuild(b); }
            }
        }

        public Vector3 LabelAnchor(BuildingEntity b)
        {
            if (_views.TryGetValue(b.Id, out var v) && v.Root != null)
            {
                float h = b.IsComplete ? v.Height : Mathf.Max(2.2f, v.Height * Mathf.Max(0.35f, b.Progress));
                return v.Root.transform.position + Vector3.up * (h + 0.6f);
            }
            return new Vector3(b.X, _w.Height.SampleHeight(b.X, b.Z) + 4f, b.Z);
        }
    }
}
