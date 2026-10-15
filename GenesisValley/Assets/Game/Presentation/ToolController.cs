using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Simulation;
using GenesisValley.World;
using UnityEngine;

namespace GenesisValley.Presentation
{
    public enum ToolKind { Observe = 0, Raise = 1, Lower = 2, Flatten = 3, River = 4, Forest = 5, Summon = 6, Bridge = 7 }

    /// <summary>
    /// God tools (GDD §4): terrain raise/lower/flatten brushes, river drawing with live validation (pale cyan = valid,
    /// red = invalid + reason), forest stamping, summoning villagers and bridge intent. One press → release is one
    /// transaction so undo works per stroke. Every edit goes through the authoritative command processor.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class ToolController : MonoBehaviour
    {
        public static readonly Color ValidColor = new Color(0.62f, 0.93f, 1f, 0.55f);
        public static readonly Color InvalidColor = new Color(1f, 0.36f, 0.3f, 0.62f);
        private static readonly Color RaiseColor = new Color(1f, 0.86f, 0.55f, 0.7f);
        private static readonly Color LowerColor = new Color(0.6f, 0.78f, 1f, 0.7f);
        private static readonly Color FlattenColor = new Color(0.95f, 0.95f, 0.92f, 0.7f);
        private static readonly Color ForestColor = new Color(0.55f, 0.95f, 0.5f, 0.65f);
        private static readonly Color SummonColor = new Color(1f, 0.85f, 0.45f, 0.75f);

        public ToolKind Current { get; private set; } = ToolKind.Observe;
        public event Action<ToolKind> ToolChanged;

        [Range(0.1f, 1f)] public float Strength = 0.6f;
        public int SummonCount = 2;
        public float RiverWidth = 5f;
        public float CommandInterval = 0.05f;

        // ---- read by the UI (cursor tooltip, tool panel)
        public bool HasCursor { get; private set; }
        public Vector3 CursorPoint { get; private set; }
        public string PreviewText { get; private set; }
        public string PreviewReason { get; private set; }
        public bool PreviewValid { get; private set; } = true;
        public bool IsStroking => _stroke;
        public bool IsDrawingRiver => _riverDrawing;
        public bool HasBridgeAnchor => _bridgeAnchor.HasValue;

        private readonly Dictionary<ToolKind, float> _radius = new Dictionary<ToolKind, float>();

        private GroundDecal _brush, _marker, _riverPreview, _bridgePreview;
        private Material _brushMat, _validMat, _invalidMat, _bridgeValidMat, _bridgeInvalidMat;

        // stroke state
        private bool _stroke;
        private int _txn;
        private float _sendTimer;
        private float _flattenTarget;
        private Vector3 _lastStamp;
        private bool _strokeRejected;
        private string _lastRejectReason;
        private float _rumbleTimer;

        // river state
        private bool _riverDrawing;
        private readonly List<float> _riverPoints = new List<float>();
        private RiverPlan _riverPlan;
        private float _riverPreviewTimer;
        private bool _riverDirty;
        private float _riverFade;
        private readonly List<Vector3> _strip = new List<Vector3>();

        // bridge state
        private Vector3? _bridgeAnchor;
        private BridgePlan _bridgePlan;

        private void Awake()
        {
            GameServices.Tools = this;
        }

        private void Start()
        {
            var art = GameServices.Art;
            var brushTex = art.Texture("UI_Brush");
            var dash = art.Texture("UI_Dash");
            _brushMat = MaterialLibrary.Overlay(Color.white, brushTex, false, 0.25f);
            _brushMat = new Material(_brushMat) { name = "GV_BrushRuntime" };
            _validMat = MaterialLibrary.Overlay(ValidColor);
            _invalidMat = MaterialLibrary.Overlay(InvalidColor, null, false, 0.35f);
            _bridgeValidMat = MaterialLibrary.Overlay(new Color(0.75f, 0.95f, 1f, 0.8f), dash, true, 0f, 0.6f);
            _bridgeInvalidMat = MaterialLibrary.Overlay(InvalidColor, dash, true, 0.3f, 0.6f);
            Func<float, float, float> h = (x, z) => GameServices.Presenter != null ? GameServices.Presenter.HeightAt(x, z) : 0f;
            _brush = new GroundDecal("BrushDecal", transform, _brushMat, h) { Lift = 0.15f };
            _marker = new GroundDecal("MarkerDecal", transform, _brushMat, h) { Lift = 0.18f };
            _riverPreview = new GroundDecal("RiverPreview", transform, _validMat, h) { Lift = 0.12f };
            _bridgePreview = new GroundDecal("BridgePreview", transform, _bridgeValidMat, h) { Lift = 0.35f };
            ResetRadii();
        }

        private void OnDestroy()
        {
            if (GameServices.Tools == this) GameServices.Tools = null;
            if (_brushMat != null) Destroy(_brushMat);
        }

        private void ResetRadii()
        {
            var c = GameServices.World != null ? GameServices.World.Config : SimConfig.CreateDefault();
            _radius[ToolKind.Raise] = c.BrushDefaultRadius;
            _radius[ToolKind.Lower] = c.BrushDefaultRadius;
            _radius[ToolKind.Flatten] = c.FlattenDefaultRadius;
            _radius[ToolKind.Forest] = c.ForestDefaultRadius;
            RiverWidth = c.RiverDefaultWidth;
        }

        public float Radius(ToolKind k) => _radius.TryGetValue(k, out var r) ? r : 8f;

        public void SetRadius(ToolKind k, float r)
        {
            var c = Config;
            if (k == ToolKind.River) { RiverWidth = Mathf.Clamp(r, c.RiverMinWidth, c.RiverMaxWidth); _riverDirty = true; return; }
            float min = k == ToolKind.Forest ? 5f : c.BrushMinRadius;
            float max = k == ToolKind.Forest ? 25f : c.BrushMaxRadius;
            _radius[k] = Mathf.Clamp(r, min, max);
        }

        private SimConfig Config => GameServices.World != null ? GameServices.World.Config : SimConfig.CreateDefault();

        /// <summary>Called when a new world is installed.</summary>
        public void ResetState()
        {
            CancelActive();
            Current = ToolKind.Observe;
            ToolChanged?.Invoke(Current);
            HidePreviews();
        }

        public void SetTool(ToolKind k)
        {
            if (k == Current) return;
            CancelActive();
            Current = k;
            HidePreviews();
            GameServices.Audio?.PlayUI("tool_select", 0.6f);
            ToolChanged?.Invoke(k);
        }

        /// <summary>Esc: cancel the current gesture, then fall back to the observe tool. Returns false if nothing to do.</summary>
        public bool CancelOrExit()
        {
            if (_stroke || _riverDrawing || _bridgeAnchor.HasValue) { CancelActive(); return true; }
            if (Current != ToolKind.Observe) { SetTool(ToolKind.Observe); return true; }
            return false;
        }

        private void CancelActive()
        {
            if (_stroke) EndStroke();
            _riverDrawing = false;
            _riverPoints.Clear();
            _riverPlan = null;
            _bridgeAnchor = null;
            if (_riverPreview != null) _riverPreview.Visible = false;
            if (_bridgePreview != null) _bridgePreview.Visible = false;
        }

        private void HidePreviews()
        {
            if (_brush == null) return;
            _brush.Visible = false; _marker.Visible = false;
            if (!_riverDrawing && _riverFade <= 0f) _riverPreview.Visible = false;
            _bridgePreview.Visible = false;
            PreviewText = null; PreviewReason = null; PreviewValid = true;
        }

        // ================================================================== update

        private void Update()
        {
            var game = GameServices.Game;
            if (_brush == null) return;
            bool playing = game != null && game.Phase == GamePhase.Playing && game.World != null;
            if (!playing)
            {
                if (_stroke || _riverDrawing || _bridgeAnchor.HasValue) CancelActive();
                HidePreviews();
                HasCursor = false;
                return;
            }

            HandleShortcuts();

            var rig = GameServices.CameraRig;
            bool overUI = PointerBlockers.IsPointerOverUI();
            HasCursor = rig != null && !overUI && rig.TryGround(Input.mousePosition, out var hit) && InsideWorld(hit);
            if (HasCursor) { rig.TryGround(Input.mousePosition, out var p); CursorPoint = p; }
            PreviewText = null; PreviewReason = null; PreviewValid = true;

            switch (Current)
            {
                case ToolKind.Raise:
                case ToolKind.Lower:
                case ToolKind.Flatten: UpdateBrush(overUI); break;
                case ToolKind.Forest: UpdateForest(overUI); break;
                case ToolKind.River: UpdateRiver(overUI); break;
                case ToolKind.Summon: UpdateSummon(overUI); break;
                case ToolKind.Bridge: UpdateBridge(overUI); break;
                default: _brush.Visible = false; _marker.Visible = false; break;
            }

            if (_riverFade > 0f && !_riverDrawing)
            {
                _riverFade -= Time.unscaledDeltaTime;
                if (_riverFade <= 0f) _riverPreview.Visible = false;
            }
        }

        private bool InsideWorld(Vector3 p)
        {
            float size = Config.WorldSize;
            return p.x > 1f && p.z > 1f && p.x < size - 1f && p.z < size - 1f;
        }

        private void HandleShortcuts()
        {
            if (InputBindings.TextInputActive) return;
            if (InputBindings.Down(GameAction.ToolObserve)) SetTool(ToolKind.Observe);
            if (InputBindings.Down(GameAction.ToolRaise)) SetTool(ToolKind.Raise);
            if (InputBindings.Down(GameAction.ToolLower)) SetTool(ToolKind.Lower);
            if (InputBindings.Down(GameAction.ToolFlatten)) SetTool(ToolKind.Flatten);
            if (InputBindings.Down(GameAction.ToolRiver)) SetTool(ToolKind.River);
            if (InputBindings.Down(GameAction.ToolForest)) SetTool(ToolKind.Forest);
            if (InputBindings.Down(GameAction.ToolSummon)) SetTool(ToolKind.Summon);
            if (InputBindings.Down(GameAction.ToolBridge)) SetTool(ToolKind.Bridge);

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (InputBindings.Down(GameAction.Undo) || ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();

            float step = 0f;
            if (InputBindings.Down(GameAction.BrushSmaller)) step -= 1f;
            if (InputBindings.Down(GameAction.BrushLarger)) step += 1f;
            bool alt = Input.GetKey(KeyCode.LeftAlt);
            if ((ctrl || alt) && !PointerBlockers.IsPointerOverUI()) step += Input.mouseScrollDelta.y;
            if (Mathf.Abs(step) > 0.01f) ResizeBy(step);
        }

        public void ResizeBy(float step)
        {
            switch (Current)
            {
                case ToolKind.River: SetRadius(ToolKind.River, RiverWidth + step * 0.5f); break;
                case ToolKind.Summon: SummonCount = Mathf.Clamp(SummonCount + (int)Mathf.Sign(step), 1, Config.SummonMaxPerBatch); break;
                case ToolKind.Raise: case ToolKind.Lower: case ToolKind.Flatten: case ToolKind.Forest:
                    SetRadius(Current, Radius(Current) * Mathf.Pow(1.12f, step)); break;
            }
        }

        public void Undo()
        {
            var game = GameServices.Game;
            if (game == null) return;
            if (_stroke) EndStroke();
            var r = game.Submit(new UndoCommand());
            if (r.Accepted) GameServices.Audio?.PlayUI("undo", 0.7f);
        }

        // ================================================================== terrain brush

        private void UpdateBrush(bool overUI)
        {
            float radius = Radius(Current);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var mode = Current == ToolKind.Raise ? BrushMode.Raise : Current == ToolKind.Lower ? BrushMode.Lower : BrushMode.Flatten;
            // Holding Ctrl temporarily inverts raise/lower (classic god-game shortcut).
            if (ctrl && mode == BrushMode.Raise) mode = BrushMode.Lower;
            else if (ctrl && mode == BrushMode.Lower) mode = BrushMode.Raise;

            if (HasCursor || _stroke)
            {
                var c = mode == BrushMode.Raise ? RaiseColor : mode == BrushMode.Lower ? LowerColor : FlattenColor;
                if (_strokeRejected) c = InvalidColor;
                _brushMat.color = c;
                MaterialLibrary.SetColor(_brushMat, c);
                if (_brushMat.HasProperty("_Pulse")) _brushMat.SetFloat("_Pulse", _stroke ? 0.6f : 0.15f);
                _brush.Disc(CursorPoint, radius, 0f, 28);
                PreviewText = (mode == BrushMode.Raise ? "抬升" : mode == BrushMode.Lower ? "降低" : "平整") + " · 半径 " + radius.ToString("0") + " 米";
                if (mode == BrushMode.Flatten && _stroke) PreviewText += " · 目标高度 " + _flattenTarget.ToString("0.0") + " 米";
                if (_strokeRejected && !string.IsNullOrEmpty(_lastRejectReason)) { PreviewValid = false; PreviewReason = _lastRejectReason; }
            }
            else _brush.Visible = false;
            _marker.Visible = false;

            if (!_stroke && HasCursor && !overUI && Input.GetMouseButtonDown(0)) BeginStroke(mode);
            if (_stroke)
            {
                if (!Input.GetMouseButton(0)) { EndStroke(); return; }
                if (!HasCursor) return;
                _sendTimer += Time.unscaledDeltaTime;
                var game = GameServices.Game;
                // Brushes work while paused too (shaping the land is not a villager task).
                while (_sendTimer >= CommandInterval)
                {
                    _sendTimer -= CommandInterval;
                    var cfg = Config;
                    float rate = mode == BrushMode.Flatten ? 6f : 2.6f;
                    float strength = Mathf.Min(cfg.BrushMaxDeltaPerCommand, rate * CommandInterval * Mathf.Lerp(0.25f, 1.4f, Strength));
                    var cmd = new TerrainBrushCommand
                    {
                        TransactionId = _txn, Mode = mode, CenterX = CursorPoint.x, CenterZ = CursorPoint.z,
                        Radius = radius, Strength = strength, TargetHeight = _flattenTarget,
                    };
                    var r = game.Submit(cmd);
                    if (!r.Accepted) { _strokeRejected = true; _lastRejectReason = r.Reason; break; }
                    _strokeRejected = false;
                }
                _rumbleTimer -= Time.unscaledDeltaTime;
                if (_rumbleTimer <= 0f && !_strokeRejected)
                {
                    _rumbleTimer = 0.18f;
                    GameServices.Fx?.Play(FxKind.Rumble, CursorPoint, 2, Mathf.Max(1f, radius / 6f));
                    GameServices.CameraRig?.AddShake(0.05f, CursorPoint);
                }
                GameServices.Audio?.SetLoop("terrain_rumble", _strokeRejected ? 0f : 0.7f, CursorPoint, 0.15f, mode == BrushMode.Lower ? 0.85f : 1f);
            }
        }

        private void BeginStroke(BrushMode mode)
        {
            var game = GameServices.Game;
            _stroke = true;
            _txn = game.NextTransaction();
            _sendTimer = CommandInterval; // first command immediately
            _strokeRejected = false;
            _lastRejectReason = null;
            _flattenTarget = GameServices.World.Height.SampleHeight(CursorPoint.x, CursorPoint.z);
            _lastStamp = CursorPoint;
            var terrain = GameServices.Presenter != null ? GameServices.Presenter.Terrain : null;
            if (terrain != null && (Current == ToolKind.Raise || Current == ToolKind.Lower || Current == ToolKind.Flatten)) terrain.Stroking = true;
        }

        private void EndStroke()
        {
            if (!_stroke) return;
            _stroke = false;
            var game = GameServices.Game;
            if (game != null && game.Runner != null) game.Submit(new EndTransactionCommand { TransactionId = _txn });
            var terrain = GameServices.Presenter != null ? GameServices.Presenter.Terrain : null;
            if (terrain != null && terrain.Stroking) { terrain.Stroking = false; terrain.FinishStroke(); }
            GameServices.Audio?.SetLoop("terrain_rumble", 0f, null, 0.4f);
            if (_strokeRejected && !string.IsNullOrEmpty(_lastRejectReason)) GameServices.Game?.Notify(_lastRejectReason, ToastKind.Warning);
            _strokeRejected = false;
        }

        // ================================================================== forest

        private void UpdateForest(bool overUI)
        {
            float radius = Radius(ToolKind.Forest);
            if (HasCursor)
            {
                MaterialLibrary.SetColor(_brushMat, _strokeRejected ? InvalidColor : ForestColor);
                if (_brushMat.HasProperty("_Pulse")) _brushMat.SetFloat("_Pulse", _stroke ? 0.5f : 0.1f);
                _brush.Disc(CursorPoint, radius, 0f, 28);
                PreviewText = "种植森林 · 半径 " + radius.ToString("0") + " 米";
                if (_strokeRejected) { PreviewValid = false; PreviewReason = _lastRejectReason; }
            }
            else _brush.Visible = false;
            _marker.Visible = false;

            if (!_stroke && HasCursor && !overUI && Input.GetMouseButtonDown(0))
            {
                BeginStroke(BrushMode.Raise);
                Stamp(radius);
            }
            if (_stroke)
            {
                if (!Input.GetMouseButton(0)) { EndStroke(); return; }
                if (HasCursor && (CursorPoint - _lastStamp).sqrMagnitude > radius * radius * 0.8f) Stamp(radius);
            }
        }

        private void Stamp(float radius)
        {
            _lastStamp = CursorPoint;
            var r = GameServices.Game.Submit(new ForestCommand { TransactionId = _txn, CenterX = CursorPoint.x, CenterZ = CursorPoint.z, Radius = radius });
            _strokeRejected = !r.Accepted;
            _lastRejectReason = r.Reason;
            if (r.Accepted) GameServices.Audio?.PlayAt("forest_plant", CursorPoint, 0.8f, UnityEngine.Random.Range(0.95f, 1.05f));
        }

        // ================================================================== river

        private void UpdateRiver(bool overUI)
        {
            var cfg = Config;
            _brush.Visible = false;
            if (HasCursor && !_riverDrawing)
            {
                // Source marker sized to the channel width.
                MaterialLibrary.SetColor(_brushMat, ValidColor);
                _marker.Material = _brushMat;
                _marker.Disc(CursorPoint, RiverWidth * 0.75f, 0f, 16);
                PreviewText = "按住左键从高处向低处拖动 · 河宽 " + RiverWidth.ToString("0.#") + " 米";
            }
            else _marker.Visible = false;

            if (!_riverDrawing && HasCursor && !overUI && Input.GetMouseButtonDown(0))
            {
                _riverDrawing = true;
                _riverPoints.Clear();
                _riverPoints.Add(CursorPoint.x); _riverPoints.Add(CursorPoint.z);
                _riverPlan = null;
                _riverDirty = true;
                _riverFade = 0f;
                GameServices.Audio?.PlayUI("plan", 0.4f);
            }
            if (!_riverDrawing) return;

            if (HasCursor)
            {
                int n = _riverPoints.Count;
                float lx = _riverPoints[n - 2], lz = _riverPoints[n - 1];
                float d2 = (CursorPoint.x - lx) * (CursorPoint.x - lx) + (CursorPoint.z - lz) * (CursorPoint.z - lz);
                if (d2 > 4.5f * 4.5f && n < 400)
                {
                    _riverPoints.Add(CursorPoint.x); _riverPoints.Add(CursorPoint.z);
                    _riverDirty = true;
                }
            }

            _riverPreviewTimer -= Time.unscaledDeltaTime;
            if (_riverDirty && _riverPreviewTimer <= 0f)
            {
                _riverPreviewTimer = 0.08f;
                _riverDirty = false;
                var pts = new List<float>(_riverPoints);
                if (HasCursor) { pts.Add(CursorPoint.x); pts.Add(CursorPoint.z); }
                var v = GameServices.Game.Validator;
                _riverPlan = pts.Count >= 4 && v != null ? v.PreviewRiver(pts, RiverWidth) : null;
                DrawRiverPreview(pts);
            }

            if (_riverPlan != null)
            {
                PreviewValid = _riverPlan.Valid;
                PreviewReason = _riverPlan.Valid ? null : (_riverPlan.Reason ?? "河道不合法");
                PreviewText = "河流 · 长 " + _riverPlan.Length.ToString("0") + " 米 · 宽 " + _riverPlan.Width.ToString("0.#") + " 米" + (_riverPlan.Reversed ? " · 已按地势调整流向" : "");
            }
            else { PreviewText = "继续拖动来画出河道"; }

            if (Input.GetMouseButtonDown(1)) { CancelActive(); return; }
            if (!Input.GetMouseButton(0)) CommitRiver();
        }

        private void DrawRiverPreview(List<float> pts)
        {
            _strip.Clear();
            var presenter = GameServices.Presenter;
            if (_riverPlan != null && _riverPlan.Samples.Count >= 2)
            {
                float hw = 0f;
                foreach (var s in _riverPlan.Samples)
                {
                    _strip.Add(new Vector3(s.X, s.Surface + 0.25f, s.Z));
                    hw = Mathf.Max(hw, s.HalfWidth);
                }
                _riverPreview.Material = _riverPlan.Valid ? _validMat : _invalidMat;
                _riverPreview.Strip(_strip, Mathf.Max(1f, hw), true, 0.1f);
            }
            else
            {
                for (int i = 0; i + 1 < pts.Count; i += 2) _strip.Add(presenter.Ground(pts[i], pts[i + 1]));
                _riverPreview.Material = _validMat;
                _riverPreview.Strip(_strip, RiverWidth * 0.5f, false, 0.1f);
            }
        }

        private void CommitRiver()
        {
            _riverDrawing = false;
            var plan = _riverPlan;
            var pts = new List<float>(_riverPoints);
            _riverPoints.Clear();
            if (pts.Count < 4 || plan == null)
            {
                _riverPreview.Visible = false;
                GameServices.Game.Notify("河道太短了：按住左键拖得更远一些", ToastKind.Warning);
                return;
            }
            if (!plan.Valid)
            {
                _riverFade = 1.6f; // leave the red preview visible for a moment
                GameServices.Game.Notify(plan.Reason ?? "河道不合法", ToastKind.Warning);
                GameServices.Audio?.PlayUI("error", 0.6f);
                return;
            }
            var cmd = new RiverCommand { TransactionId = GameServices.Game.NextTransaction(), Width = RiverWidth };
            cmd.Points.AddRange(pts);
            var r = GameServices.Game.Submit(cmd);
            if (r.Accepted)
            {
                _riverPreview.Visible = false;
                foreach (var s in plan.Samples)
                    if (UnityEngine.Random.value < 0.08f) GameServices.Fx?.Play(FxKind.Dust, new Vector3(s.X, s.Surface + 1f, s.Z), 4, 1.5f);
                GameServices.CameraRig?.AddShake(0.25f, new Vector3(plan.Samples[0].X, plan.Samples[0].Surface, plan.Samples[0].Z));
            }
            else
            {
                _riverFade = 1.6f;
                _riverPreview.Material = _invalidMat;
            }
            _riverPlan = null;
        }

        // ================================================================== summon

        private void UpdateSummon(bool overUI)
        {
            _brush.Visible = false;
            var w = GameServices.World;
            if (!HasCursor) { _marker.Visible = false; return; }
            int room = w.Config.PopulationCap - w.Population;
            var cell = w.Nav.WorldToCell(CursorPoint.x, CursorPoint.z);
            bool ok = room > 0 && w.Nav.InBounds(cell.X, cell.Z) && w.Nav.IsPassable(cell.X, cell.Z);
            MaterialLibrary.SetColor(_brushMat, ok ? SummonColor : InvalidColor);
            if (_brushMat.HasProperty("_Pulse")) _brushMat.SetFloat("_Pulse", 0.4f);
            _marker.Material = _brushMat;
            _marker.Disc(CursorPoint, 2.5f + SummonCount * 0.35f, Time.unscaledTime * 40f % 360f, 16);
            PreviewText = "召唤 " + Mathf.Min(SummonCount, Mathf.Max(0, room)) + " 位村民 · 人口 " + w.Population + "/" + w.Config.PopulationCap;
            if (!ok) { PreviewValid = false; PreviewReason = room <= 0 ? "山谷人口已达上限" : "这里无法站人"; }
            if (!overUI && Input.GetMouseButtonDown(0))
            {
                var r = GameServices.Game.Submit(new SummonCommand { X = CursorPoint.x, Z = CursorPoint.z, Count = SummonCount });
                if (!r.Accepted) GameServices.Game.Notify(r.Reason, ToastKind.Warning);
            }
        }

        // ================================================================== bridge

        private void UpdateBridge(bool overUI)
        {
            _brush.Visible = false;
            var presenter = GameServices.Presenter;
            if (!_bridgeAnchor.HasValue)
            {
                _bridgePreview.Visible = false;
                if (HasCursor)
                {
                    MaterialLibrary.SetColor(_brushMat, ValidColor);
                    _marker.Material = _brushMat;
                    _marker.Disc(CursorPoint, 1.6f, 0f, 12);
                    PreviewText = "点击河岸一侧，再点击对岸";
                }
                else _marker.Visible = false;
                if (HasCursor && !overUI && Input.GetMouseButtonDown(0)) { _bridgeAnchor = CursorPoint; GameServices.Audio?.PlayUI("plan", 0.4f); }
                return;
            }

            var a = _bridgeAnchor.Value;
            if (Input.GetMouseButtonDown(1)) { CancelActive(); return; }
            if (!HasCursor) return;
            var v = GameServices.Game.Validator;
            _bridgePlan = v != null ? v.PreviewBridge(a.x, a.z, CursorPoint.x, CursorPoint.z) : default;
            float ax = _bridgePlan.Length > 0 ? _bridgePlan.AX : a.x, az = _bridgePlan.Length > 0 ? _bridgePlan.AZ : a.z;
            float bx = _bridgePlan.Length > 0 ? _bridgePlan.BX : CursorPoint.x, bz = _bridgePlan.Length > 0 ? _bridgePlan.BZ : CursorPoint.z;
            float ya = presenter.HeightAt(ax, az), yb = presenter.HeightAt(bx, bz);
            _strip.Clear();
            float len = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int seg = Mathf.Clamp(Mathf.CeilToInt(len / 1f), 2, 40);
            float rise = Mathf.Clamp(len * 0.06f, 0.25f, 0.85f);
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                _strip.Add(new Vector3(Mathf.Lerp(ax, bx, t), Mathf.Lerp(ya, yb, t) + rise * 4f * t * (1 - t) + 0.3f, Mathf.Lerp(az, bz, t)));
            }
            _bridgePreview.Material = _bridgePlan.Valid ? _bridgeValidMat : _bridgeInvalidMat;
            _bridgePreview.Strip(_strip, 1.1f, true, 0.5f);
            _marker.Material = _brushMat;
            MaterialLibrary.SetColor(_brushMat, _bridgePlan.Valid ? ValidColor : InvalidColor);
            _marker.Disc(new Vector3(ax, ya, az), 1.6f, 0f, 12);
            PreviewValid = _bridgePlan.Valid;
            PreviewReason = _bridgePlan.Valid ? null : _bridgePlan.Reason;
            PreviewText = "桥梁 · 跨度 " + len.ToString("0.0") + " 米" + (_bridgePlan.Wood > 0 ? " · 需要木材 " + _bridgePlan.Wood : "");

            if (!overUI && Input.GetMouseButtonDown(0))
            {
                if (!_bridgePlan.Valid)
                {
                    GameServices.Game.Notify(_bridgePlan.Reason ?? "这里无法架桥", ToastKind.Warning);
                    GameServices.Audio?.PlayUI("error", 0.6f);
                    return;
                }
                var r = GameServices.Game.Submit(new BridgeCommand { AX = a.x, AZ = a.z, BX = CursorPoint.x, BZ = CursorPoint.z });
                if (r.Accepted) _bridgeAnchor = null;
                else GameServices.Game.Notify(r.Reason, ToastKind.Warning);
            }
        }

        // ================================================================== info for UI

        public static string ToolName(ToolKind k)
        {
            switch (k)
            {
                case ToolKind.Observe: return "观察";
                case ToolKind.Raise: return "抬升";
                case ToolKind.Lower: return "降低";
                case ToolKind.Flatten: return "平整";
                case ToolKind.River: return "河流";
                case ToolKind.Forest: return "森林";
                case ToolKind.Summon: return "召唤村民";
                case ToolKind.Bridge: return "桥梁意向";
            }
            return k.ToString();
        }

        public static string ToolHint(ToolKind k)
        {
            switch (k)
            {
                case ToolKind.Observe: return "点击村民、建筑或树木查看详情；双击跟随。";
                case ToolKind.Raise: return "按住左键抬升地面；按住 Ctrl 反向。营地与房屋会被保护。";
                case ToolKind.Lower: return "按住左键降低地面；按住 Ctrl 反向。";
                case ToolKind.Flatten: return "按下时锁定目标高度，拖动把周围抹平。适合给村民腾出建房的平地。";
                case ToolKind.River: return "从高处按住左键向低处拖动，松开即开挖。淡青色可行，红色不可行。";
                case ToolKind.Forest: return "点击或拖动种下树苗，长大后才能砍伐。";
                case ToolKind.Summon: return "点击地面召唤新的村民（需要食物与床位才能留住他们）。";
                case ToolKind.Bridge: return "依次点击河两岸，村民会运来木材把桥建起来。";
            }
            return "";
        }

        public static GameAction ToolAction(ToolKind k) => (GameAction)((int)GameAction.ToolObserve + (int)k);
    }
}
