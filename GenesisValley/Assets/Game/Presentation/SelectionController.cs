using System;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    public enum SelectionKind { None, Villager, Building, Tree, Camp, Pile }

    public struct Selectable : IEquatable<Selectable>
    {
        public SelectionKind Kind;
        public EntityId Id;
        public static readonly Selectable None = default;
        public bool IsNone => Kind == SelectionKind.None;
        public Selectable(SelectionKind k, EntityId id) { Kind = k; Id = id; }
        public bool Equals(Selectable o) => Kind == o.Kind && Id.Equals(o.Id);
        public override bool Equals(object obj) => obj is Selectable o && Equals(o);
        public override int GetHashCode() => ((int)Kind * 397) ^ Id.GetHashCode();
    }

    /// <summary>
    /// Observe-tool picking (GDD §8 inspector): hover highlight and click selection of villagers, buildings, trees,
    /// the camp and loose piles; double-click follows a villager / flies to anything else. Drawn as soft rings on the
    /// ground so the scene is never cluttered with outlines.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class SelectionController : MonoBehaviour
    {
        public Selectable Selected { get; private set; }
        public Selectable Hovered { get; private set; }
        public event Action<Selectable> SelectionChanged;
        public bool IsFollowing => _following && GameServices.CameraRig != null && GameServices.CameraRig.IsFollowing;

        private GroundDecal _ring, _hover;
        private Material _ringMat, _hoverMat;
        private Vector3 _downPos;
        private bool _down;
        private float _lastClickTime;
        private Selectable _lastClick;
        private bool _following;
        private float _ringRadius = 1f;

        private void Awake() { GameServices.Selection = this; }

        private void OnDestroy()
        {
            if (GameServices.Selection == this) GameServices.Selection = null;
            if (_ringMat != null) Destroy(_ringMat);
            if (_hoverMat != null) Destroy(_hoverMat);
        }

        private void Start()
        {
            var tex = GameServices.Art.Texture("UI_SelectRing") ?? GameServices.Art.Texture("UI_Brush");
            _ringMat = new Material(MaterialLibrary.Overlay(new Color(1f, 0.88f, 0.5f, 0.9f), tex, false, 0.35f)) { name = "GV_SelectRing" };
            _hoverMat = new Material(MaterialLibrary.Overlay(new Color(1f, 1f, 1f, 0.45f), tex, false, 0f)) { name = "GV_HoverRing" };
            Func<float, float, float> h = (x, z) => GameServices.Presenter != null ? GameServices.Presenter.HeightAt(x, z) : 0f;
            _ring = new GroundDecal("SelectionRing", transform, _ringMat, h) { Lift = 0.1f };
            _hover = new GroundDecal("HoverRing", transform, _hoverMat, h) { Lift = 0.09f };
        }

        public void Clear() => Select(Selectable.None);

        /// <summary>Esc: drop the selection first. Returns false when nothing was selected.</summary>
        public bool CancelOrClear()
        {
            if (Selected.IsNone) return false;
            Clear();
            return true;
        }

        public void Select(Selectable s)
        {
            if (s.Equals(Selected)) return;
            Selected = s;
            if (_following) { GameServices.CameraRig?.StopFollow(); _following = false; }
            if (!s.IsNone) GameServices.Audio?.PlayUI("select", 0.55f, s.Kind == SelectionKind.Villager ? 1.1f : 0.95f);
            SelectionChanged?.Invoke(s);
        }

        public void Follow(Selectable s)
        {
            var rig = GameServices.CameraRig;
            var presenter = GameServices.Presenter;
            if (rig == null || presenter == null || s.IsNone) return;
            if (s.Kind == SelectionKind.Villager)
            {
                var id = s.Id;
                rig.FlyTo(Anchor(s) ?? rig.FocusPoint, Mathf.Min(rig.Distance, 38f), 0.8f, null, () =>
                    rig.Follow(() => presenter.Villagers.TryGetPosition(id, out var p) ? p : (Vector3?)null));
                _following = true;
            }
            else
            {
                var a = Anchor(s);
                if (a.HasValue) rig.FlyTo(a.Value, Mathf.Min(rig.Distance, 55f), 0.9f);
            }
        }

        public void StopFollow()
        {
            GameServices.CameraRig?.StopFollow();
            _following = false;
        }

        public Vector3? Anchor(Selectable s)
        {
            var presenter = GameServices.Presenter;
            if (presenter == null || s.IsNone) return null;
            return presenter.TryGetAnchor(s.Id, out var p) ? p : (Vector3?)null;
        }

        // ================================================================== update

        private void Update()
        {
            var game = GameServices.Game;
            var w = GameServices.World;
            if (_ring == null) return;
            bool playing = game != null && w != null && (game.Phase == GamePhase.Playing);
            if (!playing)
            {
                _hover.Visible = false;
                if (game != null && game.Phase != GamePhase.Playing && !Selected.IsNone && game.Phase != GamePhase.Intro) Clear();
                _ring.Visible = false;
                Hovered = Selectable.None;
                return;
            }

            if (!Selected.IsNone && !Exists(w, Selected)) Clear();

            var tools = GameServices.Tools;
            bool observe = tools == null || tools.Current == ToolKind.Observe;
            bool overUI = PointerBlockers.IsPointerOverUI();
            Hovered = observe && !overUI && !_down ? Pick(w) : (observe && _down ? Hovered : Selectable.None);

            if (observe)
            {
                if (Input.GetMouseButtonDown(0) && !overUI) { _down = true; _downPos = Input.mousePosition; }
                if (_down && Input.GetMouseButtonUp(0))
                {
                    _down = false;
                    if ((Input.mousePosition - _downPos).sqrMagnitude < 8f * 8f)
                    {
                        var hit = Pick(w);
                        bool dbl = Time.unscaledTime - _lastClickTime < 0.33f && hit.Equals(_lastClick) && !hit.IsNone;
                        _lastClickTime = Time.unscaledTime;
                        _lastClick = hit;
                        Select(hit);
                        if (dbl) Follow(hit);
                    }
                }
            }
            else _down = false;

            if (_following && GameServices.CameraRig != null && !GameServices.CameraRig.IsFollowing && !GameServices.CameraRig.IsFlying) _following = false;

            DrawRing(_ring, Selected, true);
            if (!Hovered.IsNone && !Hovered.Equals(Selected)) DrawRing(_hover, Hovered, false);
            else _hover.Visible = false;
        }

        private void DrawRing(GroundDecal decal, Selectable s, bool selected)
        {
            if (s.IsNone) { decal.Visible = false; return; }
            var w = GameServices.World;
            var presenter = GameServices.Presenter;
            Vector3 c; float r;
            switch (s.Kind)
            {
                case SelectionKind.Villager:
                    if (!presenter.Villagers.TryGetPosition(s.Id, out c)) { decal.Visible = false; return; }
                    if (presenter.Villagers.TryGetView(s.Id, out var vv) && !vv.Visible) { decal.Visible = false; return; }
                    r = 0.85f; break;
                case SelectionKind.Building:
                {
                    var b = w.FindBuilding(s.Id);
                    if (b == null) { decal.Visible = false; return; }
                    c = presenter.Ground(b.X, b.Z);
                    r = b.Kind == BuildingKind.Bridge ? BuildingPresenter.BridgeLength(b) * 0.5f + 1f : b.BoundingRadius + 0.9f;
                    break;
                }
                case SelectionKind.Tree:
                {
                    var t = w.FindTree(s.Id);
                    if (t == null) { decal.Visible = false; return; }
                    c = presenter.Ground(t.X, t.Z); r = 1.4f * Mathf.Max(0.5f, t.Scale); break;
                }
                case SelectionKind.Camp:
                {
                    var camp = w.FindStorage(s.Id);
                    if (camp == null) { decal.Visible = false; return; }
                    c = presenter.Ground(camp.X, camp.Z); r = 7f; break;
                }
                case SelectionKind.Pile:
                {
                    var p = w.FindPile(s.Id);
                    if (p == null) { decal.Visible = false; return; }
                    c = presenter.Ground(p.X, p.Z); r = 1.3f; break;
                }
                default: decal.Visible = false; return;
            }
            if (selected)
            {
                // Pop-in when the selection changes, gentle breathing afterwards.
                _ringRadius = Mathf.Lerp(_ringRadius, r, Ease.Damp(14f, Time.unscaledDeltaTime));
                r = _ringRadius * (1f + 0.03f * Mathf.Sin(Time.unscaledTime * 3f));
            }
            int res = r > 6f ? 32 : 16;
            decal.Disc(c, r, selected ? Time.unscaledTime * 12f % 360f : 0f, res);
        }

        private static bool Exists(WorldState w, Selectable s)
        {
            switch (s.Kind)
            {
                case SelectionKind.Villager: return w.FindVillager(s.Id) != null;
                case SelectionKind.Building: return w.FindBuilding(s.Id) != null;
                case SelectionKind.Tree: return w.FindTree(s.Id) != null;
                case SelectionKind.Camp: return w.FindStorage(s.Id) != null;
                case SelectionKind.Pile: return w.FindPile(s.Id) != null;
            }
            return false;
        }

        // ================================================================== picking

        private Selectable Pick(WorldState w)
        {
            var rig = GameServices.CameraRig;
            var presenter = GameServices.Presenter;
            if (rig == null || presenter == null) return Selectable.None;
            var cam = rig.Cam;
            Vector2 mouse = Input.mousePosition;

            // 1) villagers in screen space (small targets deserve a generous radius)
            float best = float.MaxValue;
            EntityId bestId = EntityId.None;
            foreach (var kv in presenter.Villagers.Views)
            {
                var v = kv.Value;
                if (!v.Visible) continue;
                var sp = cam.WorldToScreenPoint(v.Position + Vector3.up * 0.95f);
                if (sp.z <= 0.5f) continue;
                float radius = Mathf.Clamp(900f / sp.z, 14f, 60f);
                float d = ((Vector2)sp - mouse).magnitude;
                if (d < radius && d < best) { best = d; bestId = kv.Key; }
            }
            if (bestId.IsValid) return new Selectable(SelectionKind.Villager, bestId);

            var ray = cam.ScreenPointToRay(mouse);
            bool ground = rig.TryGround(mouse, out var g);
            float groundDist = ground ? (g - ray.origin).magnitude : float.MaxValue;

            // 2) buildings: ray vs. footprint box
            EntityId bBest = EntityId.None; float bDist = float.MaxValue;
            foreach (var b in w.Buildings)
            {
                if (b.Kind == BuildingKind.Bridge)
                {
                    if (!ground) continue;
                    float d = BridgePlanner.SegmentDistance(g.x, g.z, g.x, g.z, b.AX, b.AZ, b.BX, b.BZ);
                    if (d < 1.6f && groundDist < bDist) { bDist = groundDist - 0.1f; bBest = b.Id; }
                    continue;
                }
                float h = 3f;
                if (presenter.Buildings.TryGetView(b.Id, out var view)) h = Mathf.Max(0.6f, b.IsComplete ? view.Height : view.Height * Mathf.Max(0.3f, b.Progress));
                if (b.Kind == BuildingKind.Field) h = 0.8f;
                float y = presenter.HeightAt(b.X, b.Z);
                var rot = Quaternion.Euler(0, b.Yaw, 0);
                var local = new Ray(Quaternion.Inverse(rot) * (ray.origin - new Vector3(b.X, y + h * 0.5f, b.Z)), Quaternion.Inverse(rot) * ray.direction);
                var box = new Bounds(Vector3.zero, new Vector3(b.SizeX + 0.6f, h + 0.4f, b.SizeZ + 0.6f));
                if (box.IntersectRay(local, out float dist) && dist < bDist && dist < groundDist + 2f) { bDist = dist; bBest = b.Id; }
            }
            if (bBest.IsValid) return new Selectable(SelectionKind.Building, bBest);
            if (!ground) return Selectable.None;

            // 3) camp, piles, trees by ground proximity
            var camp = w.Camp;
            if (camp != null && (new Vector2(camp.X - g.x, camp.Z - g.z)).sqrMagnitude < 5.5f * 5.5f) return new Selectable(SelectionKind.Camp, camp.Id);
            foreach (var p in w.Piles)
                if ((new Vector2(p.X - g.x, p.Z - g.z)).sqrMagnitude < 1.5f * 1.5f) return new Selectable(SelectionKind.Pile, p.Id);

            EntityId tBest = EntityId.None; float tD = 2.4f * 2.4f;
            foreach (var t in w.Trees)
            {
                float dx = t.X - g.x, dz = t.Z - g.z;
                if (Mathf.Abs(dx) > 3f || Mathf.Abs(dz) > 3f) continue;
                float d2 = dx * dx + dz * dz;
                if (d2 < tD) { tD = d2; tBest = t.Id; }
            }
            // Trees are tall: also accept clicks on the canopy by testing a few points up the trunk.
            if (!tBest.IsValid)
            {
                float bestScreen = 30f;
                foreach (var t in w.Trees)
                {
                    float dx = t.X - g.x, dz = t.Z - g.z;
                    if (dx * dx + dz * dz > 18f * 18f) continue;
                    var top = presenter.Ground(t.X, t.Z) + Vector3.up * (presenter.Trees.CanopyHeight(t.Id) * 0.65f);
                    var sp = cam.WorldToScreenPoint(top);
                    if (sp.z <= 0) continue;
                    float d = ((Vector2)sp - mouse).magnitude;
                    if (d < bestScreen) { bestScreen = d; tBest = t.Id; }
                }
            }
            if (tBest.IsValid) return new Selectable(SelectionKind.Tree, tBest);
            return Selectable.None;
        }
    }
}
