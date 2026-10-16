using System;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// God-game camera (GDD §4): WASD/arrows pan, Q/E rotate, wheel zooms toward the cursor, middle-drag grabs the
    /// ground, right-drag orbits; pitch stays within 25–75° and follows a zoom curve; smooth damping, terrain
    /// clearance, map bounds, follow / fly-to / intro / title orbit and small impact shakes.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Camera))]
    public sealed class GodCameraController : MonoBehaviour
    {
        public float MinDistance = 9f, MaxDistance = 340f;
        public float MinPitch = 25f, MaxPitch = 75f;
        public float Sharpness = 12f;
        public float EdgePanBorder = 6f;

        public Camera Cam { get; private set; }
        public Vector3 FocusPoint => new Vector3(_focus.x, _focusY, _focus.z);
        public float Distance => _distance;
        public float FocusDistance => (transform.position - FocusPoint).magnitude;
        public float Yaw => _yaw;
        public bool InputEnabled = true;
        public bool IsFlying => _fly != null;
        public event Action UserMoved;

        private Func<float, float, float> _height;
        private float _worldSize = 500f;

        // target state
        private Vector3 _targetFocus;
        private float _targetYaw, _targetDistance, _pitchOffset;
        // smoothed state
        private Vector3 _focus;
        private float _focusY, _yaw, _distance, _pitch, _lift;

        private Func<Vector3?> _follow;
        private Vector3 _grabPoint;
        private bool _grabbing;
        private float _shake;
        private Vector3 _lastMouse;

        private sealed class Flight
        {
            public Vector3 FromFocus, ToFocus;
            public float FromYaw, ToYaw, FromDist, ToDist, Duration, T, ArcHeight;
            public bool Skippable;
            public Action Done;
        }
        private Flight _fly;
        private bool _orbit;
        private Vector3 _orbitCentre;
        private float _orbitSpeed;

        private void Awake()
        {
            Cam = GetComponent<Camera>();
            GameServices.CameraRig = this;
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 2600f;
            Cam.fieldOfView = 38f;
            _targetDistance = _distance = 90f;
            _targetYaw = _yaw = 35f;
        }

        private void OnDestroy() { if (GameServices.CameraRig == this) GameServices.CameraRig = null; }

        public void Configure(Func<float, float, float> heightSampler, float worldSize, Vector3 focus)
        {
            _height = heightSampler;
            _worldSize = worldSize;
            _targetFocus = _focus = new Vector3(focus.x, 0, focus.z);
            _focusY = SampleHeight(focus.x, focus.z);
            _fly = null; _follow = null; _orbit = false;
            Apply(true);
        }

        private float SampleHeight(float x, float z) => _height != null ? _height(x, z) : 0f;

        // ------------------------------------------------------------------ public control

        public void Follow(Func<Vector3?> target) { _follow = target; }
        public void StopFollow() { _follow = null; }
        public bool IsFollowing => _follow != null;

        public void FlyTo(Vector3 point, float distance = -1f, float duration = 1.4f, float? yaw = null, Action done = null, bool skippable = true)
        {
            _orbit = false;
            _fly = new Flight
            {
                FromFocus = _focus, ToFocus = new Vector3(point.x, 0, point.z),
                FromYaw = _yaw, ToYaw = yaw ?? _yaw,
                FromDist = _distance, ToDist = distance > 0 ? Mathf.Clamp(distance, MinDistance, MaxDistance) : _distance,
                Duration = Mathf.Max(0.1f, duration), Done = done, Skippable = skippable,
            };
            float travel = (_fly.ToFocus - _fly.FromFocus).magnitude;
            _fly.ArcHeight = Mathf.Clamp(travel * 0.35f, 0f, 120f);
        }

        /// <summary>Opening shot (GDD §3: 6–8 s from a distant view to the central plain, skippable).</summary>
        public void PlayIntro(Vector3 centre, Action done)
        {
            _follow = null;
            _focus = _targetFocus = new Vector3(centre.x - 120f, 0, centre.z - 160f);
            _focusY = SampleHeight(_focus.x, _focus.z);
            _distance = _targetDistance = MaxDistance + 80f;
            _yaw = _targetYaw = 10f;
            Apply(true);
            FlyTo(centre, 85f, 7f, 40f, done, true);
        }

        public void SetTitleOrbit(Vector3 centre, float distance, float speedDegPerSec)
        {
            _orbit = true;
            _orbitCentre = centre;
            _orbitSpeed = speedDegPerSec;
            _targetFocus = _focus = new Vector3(centre.x, 0, centre.z);
            _targetDistance = _distance = distance;
            _pitchOffset = -6f;
            _fly = null;
            Apply(true);
        }

        /// <summary>Instantly places the camera (no flight, no smoothing). Used by fixed review shots.</summary>
        public void SnapTo(Vector3 focus, float distance, float yaw)
        {
            _orbit = false; _fly = null; _follow = null; _pitchOffset = 0f; _shake = 0f;
            _targetFocus = _focus = new Vector3(focus.x, 0, focus.z);
            _targetDistance = _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            _targetYaw = _yaw = yaw;
            Apply(true);
        }

        public void StopOrbit() { _orbit = false; _pitchOffset = 0f; }

        public void AddShake(float amount, Vector3 at)
        {
            float d = (at - FocusPoint).magnitude;
            float k = Mathf.Clamp01(1f - d / 80f) * Mathf.Clamp01(1f - _distance / 260f);
            _shake = Mathf.Min(0.6f, _shake + amount * k);
        }

        // ------------------------------------------------------------------ update

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_orbit)
            {
                _targetYaw += _orbitSpeed * dt;
                _yaw = _targetYaw;
                _focus = _targetFocus;
            }
            else if (_fly != null) UpdateFlight(dt);
            else
            {
                if (InputEnabled) HandleInput(dt);
                if (_follow != null)
                {
                    var p = _follow();
                    if (p.HasValue) _targetFocus = new Vector3(p.Value.x, 0, p.Value.z);
                    else _follow = null;
                }
                float k = Ease.Damp(Sharpness, dt);
                _focus = Vector3.Lerp(_focus, _targetFocus, k);
                _yaw = Mathf.LerpAngle(_yaw, _targetYaw, k);
                _distance = Mathf.Lerp(_distance, _targetDistance, Ease.Damp(Sharpness * 0.8f, dt));
            }
            Apply(false, dt);
        }

        private void UpdateFlight(float dt)
        {
            var f = _fly;
            bool skip = f.Skippable && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.anyKeyDown && !Input.GetKeyDown(KeyCode.LeftShift)) && f.T > 0.15f;
            f.T += dt / f.Duration;
            if (skip) f.T = 1f;
            float t = Ease.InOutCubic(f.T);
            _focus = Vector3.Lerp(f.FromFocus, f.ToFocus, t);
            _yaw = Mathf.LerpAngle(f.FromYaw, f.ToYaw, t);
            _distance = Mathf.Lerp(f.FromDist, f.ToDist, t) + Mathf.Sin(t * Mathf.PI) * f.ArcHeight;
            _targetFocus = _focus; _targetYaw = _yaw; _targetDistance = f.ToDist;
            if (f.T >= 1f)
            {
                _fly = null;
                _distance = _targetDistance;
                f.Done?.Invoke();
            }
        }

        private void HandleInput(float dt)
        {
            var settings = GameServices.Settings;
            float panMul = settings != null ? settings.CameraPanSpeed : 1f;
            float rotMul = settings != null ? settings.CameraRotateSpeed : 1f;
            bool overUI = PointerBlockers.IsPointerOverUI();
            bool moved = false;

            // Keyboard pan, scaled by zoom so it feels constant on screen.
            var fwd = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;
            float speed = Mathf.Lerp(14f, 190f, Mathf.InverseLerp(MinDistance, MaxDistance, _targetDistance)) * panMul;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= 2.2f;
            var pan = Vector3.zero;
            if (InputBindings.Held(GameAction.PanForward) || Input.GetKey(KeyCode.UpArrow)) pan += fwd;
            if (InputBindings.Held(GameAction.PanBack) || Input.GetKey(KeyCode.DownArrow)) pan -= fwd;
            if (InputBindings.Held(GameAction.PanRight) || Input.GetKey(KeyCode.RightArrow)) pan += right;
            if (InputBindings.Held(GameAction.PanLeft) || Input.GetKey(KeyCode.LeftArrow)) pan -= right;
            if (settings != null && settings.EdgePan && Application.isFocused && !overUI)
            {
                var m = Input.mousePosition;
                if (m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height)
                {
                    if (m.x < EdgePanBorder) pan -= right;
                    if (m.x > Screen.width - EdgePanBorder) pan += right;
                    if (m.y < EdgePanBorder) pan -= fwd;
                    if (m.y > Screen.height - EdgePanBorder) pan += fwd;
                }
            }
            if (pan.sqrMagnitude > 0f) { _targetFocus += pan.normalized * speed * dt; moved = true; }

            if (InputBindings.Held(GameAction.RotateLeft)) { _targetYaw += 90f * rotMul * dt; moved = true; }
            if (InputBindings.Held(GameAction.RotateRight)) { _targetYaw -= 90f * rotMul * dt; moved = true; }
            if (InputBindings.Held(GameAction.ZoomIn)) _targetDistance *= 1f - 1.2f * dt;
            if (InputBindings.Held(GameAction.ZoomOut)) _targetDistance *= 1f + 1.2f * dt;

            // Wheel zoom toward the cursor.
            bool modifier = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt);
            float wheel = overUI || modifier ? 0f : Input.mouseScrollDelta.y; // Ctrl/Alt + wheel = brush size
            if (settings != null && settings.InvertZoom) wheel = -wheel;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                float old = _targetDistance;
                _targetDistance = Mathf.Clamp(_targetDistance * Mathf.Pow(0.86f, wheel), MinDistance, MaxDistance);
                if (wheel > 0 && TryGround(Input.mousePosition, out var hit))
                {
                    float k = 1f - _targetDistance / old;
                    var h = new Vector3(hit.x, 0, hit.z);
                    _targetFocus += (h - _targetFocus) * k;
                }
                moved = true;
            }

            // Middle drag: grab the ground under the cursor.
            if (Input.GetMouseButtonDown(2) && !overUI && TryGround(Input.mousePosition, out var g)) { _grabbing = true; _grabPoint = g; }
            if (_grabbing && Input.GetMouseButton(2))
            {
                var ray = Cam.ScreenPointToRay(Input.mousePosition);
                var plane = new Plane(Vector3.up, _grabPoint);
                if (plane.Raycast(ray, out float enter))
                {
                    var now = ray.GetPoint(enter);
                    var delta = _grabPoint - now; delta.y = 0;
                    _targetFocus += delta;
                    _focus += delta; // grab is direct, not damped
                    moved = true;
                }
            }
            if (Input.GetMouseButtonUp(2)) _grabbing = false;

            // Right drag: orbit yaw + pitch offset.
            if (Input.GetMouseButtonDown(1)) _lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(1))
            {
                var d = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                if (d.sqrMagnitude > 0.01f && !overUI)
                {
                    _targetYaw += d.x * 0.25f * rotMul;
                    _pitchOffset = Mathf.Clamp(_pitchOffset - d.y * 0.15f * rotMul, -25f, 25f);
                    moved = true;
                }
            }

            if (moved)
            {
                if (_follow != null && pan.sqrMagnitude > 0) _follow = null;
                UserMoved?.Invoke();
            }
            float margin = 6f;
            _targetFocus.x = Mathf.Clamp(_targetFocus.x, margin, _worldSize - margin);
            _targetFocus.z = Mathf.Clamp(_targetFocus.z, margin, _worldSize - margin);
            _targetDistance = Mathf.Clamp(_targetDistance, MinDistance, MaxDistance);
        }

        /// <summary>Pitch rises as you zoom out (close: look across the village; far: map view).</summary>
        private float PitchFor(float distance)
        {
            float t = Mathf.InverseLerp(MinDistance, MaxDistance, distance);
            float basePitch = Mathf.Lerp(30f, 64f, Mathf.Pow(t, 0.7f));
            return Mathf.Clamp(basePitch + _pitchOffset, MinPitch, MaxPitch);
        }

        private void Apply(bool snap, float dt = 0f)
        {
            float targetFocusY = SampleHeight(_focus.x, _focus.z);
            // Average the ground a little so the camera does not jitter across small bumps.
            targetFocusY = (targetFocusY * 2f + SampleHeight(_focus.x + 6f, _focus.z) + SampleHeight(_focus.x - 6f, _focus.z) + SampleHeight(_focus.x, _focus.z + 6f) + SampleHeight(_focus.x, _focus.z - 6f)) / 6f;
            _focusY = snap ? targetFocusY : Mathf.Lerp(_focusY, targetFocusY, Ease.Damp(6f, dt));
            _pitch = snap ? PitchFor(_distance) : Mathf.Lerp(_pitch, PitchFor(_distance), Ease.Damp(10f, dt));
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            var focus = new Vector3(_focus.x, _focusY, _focus.z);
            var pos = focus - rot * Vector3.forward * _distance;
            // Terrain clearance along the whole sight line (not only under the lens), so hills between the
            // camera and the focus never cut into the view. Smoothed so the lift does not pop.
            float minY = SampleHeight(pos.x, pos.z) + 2.5f + _distance * 0.02f;
            for (int i = 1; i <= 5; i++)
            {
                float t = i / 6f;
                float gx = Mathf.Lerp(focus.x, pos.x, t), gz = Mathf.Lerp(focus.z, pos.z, t);
                float need = SampleHeight(gx, gz) + 1.8f + _distance * 0.01f;
                if (need > focus.y) minY = Mathf.Max(minY, focus.y + (need - focus.y) / t);
            }
            float lift = Mathf.Max(0f, minY - pos.y);
            _lift = snap ? lift : Mathf.Lerp(_lift, lift, Ease.Damp(lift > _lift ? 14f : 4f, dt));
            if (_lift > 0.01f)
            {
                pos.y += _lift;
                rot = Quaternion.LookRotation(focus - pos);
            }
            if (_shake > 0.001f)
            {
                float s = _shake * _shake;
                pos += new Vector3(Mathf.PerlinNoise(Time.time * 25f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, Time.time * 25f) - 0.5f, 0) * s * 1.2f;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 1.8f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        public bool TryGround(Vector3 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            var presenter = GameServices.Presenter;
            if (presenter != null && presenter.Terrain != null && presenter.Terrain.Raycast(ray, out point)) return true;
            var plane = new Plane(Vector3.up, new Vector3(0, _focusY, 0));
            if (plane.Raycast(ray, out float enter)) { point = ray.GetPoint(enter); return true; }
            point = default;
            return false;
        }
    }
}
