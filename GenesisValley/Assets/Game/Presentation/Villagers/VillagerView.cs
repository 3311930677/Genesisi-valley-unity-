using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Visual for one villager: interpolated between sim ticks, animated by sim state (never the other way round —
    /// GDD §5 "animation never decides production"), with props, footsteps and action-synced impacts.
    /// </summary>
    public sealed class VillagerView
    {
        public readonly EntityId Id;
        public readonly GameObject Root;
        public readonly VillagerRig Rig;
        public readonly Animator Anim;
        public float PrevX, PrevZ, CurX, CurZ;
        public Vector3 Position { get; private set; }
        public float Yaw { get; private set; }
        public bool Visible { get; private set; } = true;
        public float Speed { get; private set; }
        public string State => _state;
        public int SeatIndex = -1;

        private readonly VillagerPresenter _owner;
        private string _state;
        private float _stateTime, _lastPhase;
        private PropKind _rightKind, _leftKind, _carryKind;
        private GameObject _right, _left, _carry, _bedroll;
        private Transform _rodTip;
        private LineRenderer _line;
        private Vector3 _bobber;
        private float _stepTimer, _spawn = 1f, _celebrate, _idleVariety;
        private Vector3 _lastPos;
        private bool _hasLast;
        private readonly Transform _fbBody, _fbHead, _fbArmL, _fbArmR, _fbLegL, _fbLegR;
        private float _fbCycle;
        private Vector3 _baseScale = Vector3.one;
        private bool _spawnPending;

        public VillagerView(VillagerPresenter owner, Villager v, GameObject prefab, Transform parent)
        {
            _owner = owner;
            Id = v.Id;
            if (prefab != null)
            {
                Root = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
                Rig = Root.GetComponent<VillagerRig>();
                if (Rig == null) Rig = Root.AddComponent<VillagerRig>();
                Rig.CaptureBindPose();
                Rig.Apply(v.AppearanceSeed, v.Gender);
                Anim = Rig.animator != null ? Rig.animator : Root.GetComponentInChildren<Animator>();
                if (Anim != null)
                {
                    if (GameServices.Art.villagerAnimator != null && Anim.runtimeAnimatorController == null) Anim.runtimeAnimatorController = GameServices.Art.villagerAnimator;
                    Anim.applyRootMotion = false;
                    Anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    // De-synchronise idle cycles.
                    if (Anim.runtimeAnimatorController != null) Anim.Play(VillagerAnim.Idle, 0, Random.value);
                }
                foreach (var c in Root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                foreach (var smr in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.updateWhenOffscreen = false;
                    smr.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            else
            {
                Root = new GameObject("Villager");
                Root.transform.SetParent(parent, false);
                BuildFallback(v, out _fbBody, out _fbHead, out _fbArmL, out _fbArmR, out _fbLegL, out _fbLegR);
            }
            Root.name = "Villager_" + v.Id.Value + "_" + v.Name;
            PrevX = CurX = v.X; PrevZ = CurZ = v.Z;
            Yaw = v.FacingDegrees;
            Position = new Vector3(v.X, owner.GroundHeight(v.X, v.Z), v.Z);
            Root.transform.SetPositionAndRotation(Position, Quaternion.Euler(0, Yaw, 0));
            _baseScale = Root.transform.localScale;
            _idleVariety = Random.Range(4f, 12f);
        }

        public void PlaySpawn() { _spawn = 0f; _spawnPending = true; }
        public void Celebrate(float seconds) { _celebrate = Mathf.Max(_celebrate, seconds); }

        private void BuildFallback(Villager v, out Transform body, out Transform head, out Transform armL, out Transform armR, out Transform legL, out Transform legR)
        {
            var rng = new System.Random(v.AppearanceSeed);
            var shirt = MaterialLibrary.Lit(Color.HSVToRGB((float)rng.NextDouble(), 0.35f, 0.65f), 0.1f);
            var pants = MaterialLibrary.Lit(new Color(0.36f, 0.3f, 0.24f), 0.1f);
            var skin = MaterialLibrary.Lit(new Color(0.93f, 0.76f, 0.62f), 0.2f);
            var hair = MaterialLibrary.Lit(new Color(0.3f, 0.2f, 0.12f), 0.1f);
            var t = Root.transform;
            body = MeshKit.Spawn(MeshKit.Cylinder, new[] { shirt, shirt }, t, new Vector3(0, 1.12f, 0), Quaternion.identity, new Vector3(0.38f, 0.62f, 0.26f), true, "Body");
            head = MeshKit.Spawn(MeshKit.Sphere, skin, t, new Vector3(0, 1.62f, 0), Quaternion.identity, new Vector3(0.28f, 0.3f, 0.28f), true, "Head");
            MeshKit.Spawn(MeshKit.Sphere, hair, head, new Vector3(0, 0.18f, -0.05f), Quaternion.identity, new Vector3(1.05f, 0.75f, 1.05f), true, "Hair");
            armL = Limb(t, shirt, new Vector3(-0.25f, 1.4f, 0), 0.6f);
            armR = Limb(t, shirt, new Vector3(0.25f, 1.4f, 0), 0.6f);
            legL = Limb(t, pants, new Vector3(-0.1f, 0.82f, 0), 0.82f);
            legR = Limb(t, pants, new Vector3(0.1f, 0.82f, 0), 0.82f);
        }

        private static Transform Limb(Transform parent, Material m, Vector3 joint, float len)
        {
            var j = new GameObject("Joint").transform;
            j.SetParent(parent, false);
            j.localPosition = joint;
            MeshKit.Spawn(MeshKit.Cylinder, new[] { m, m }, j, new Vector3(0, -len / 2, 0), Quaternion.identity, new Vector3(0.12f, len, 0.12f), true, "Limb");
            return j;
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(WorldState w, Villager v, float alpha, float dt, float simSpeed, bool paused)
        {
            // Visibility: inside the house = not rendered (door sound when it changes near the camera).
            bool visible = !v.InsideHome;
            if (visible != Visible)
            {
                Visible = visible;
                Root.SetActive(visible);
                GameServices.Audio?.PlayAt(visible ? "door_open" : "door_close", Position + Vector3.up, 0.7f, Random.Range(0.95f, 1.05f));
                _hasLast = false;
            }
            if (!visible) return;

            // Interpolated position between the last two ticks.
            float x = Mathf.Lerp(PrevX, CurX, alpha), z = Mathf.Lerp(PrevZ, CurZ, alpha);
            var target = new Vector3(x, 0, z);
            var seat = SeatIndex >= 0 ? _owner.SeatPose(SeatIndex) : (Pose?)null;
            if (seat.HasValue && !v.IsMoving)
            {
                var sp = seat.Value.position;
                target = new Vector3(sp.x, 0, sp.z);
            }
            var p = Position;
            p.x = !v.IsMoving && seat.HasValue ? Ease.Damp(p.x, target.x, 6f, dt) : target.x;
            p.z = !v.IsMoving && seat.HasValue ? Ease.Damp(p.z, target.z, 6f, dt) : target.z;
            p.y = _owner.GroundHeight(p.x, p.z);
            if (seat.HasValue && !v.IsMoving) p.y = Mathf.Max(p.y, seat.Value.position.y);

            // Speed in "base" metres per sim-second so gait does not change with the game-speed multiplier.
            if (_hasLast && dt > 1e-5f)
            {
                var d = p - _lastPos; d.y = 0;
                float inst = d.magnitude / dt / Mathf.Max(1f, simSpeed);
                if (paused) inst = 0f;
                Speed = Ease.Damp(Speed, inst, 10f, dt);
            }
            _lastPos = p; _hasLast = true;
            Position = p;

            // Facing: movement direction while walking, otherwise the sim facing / seat orientation.
            float wantYaw = v.FacingDegrees;
            if (v.IsMoving)
            {
                var mv = new Vector3(CurX - PrevX, 0, CurZ - PrevZ);
                if (mv.sqrMagnitude > 1e-5f) wantYaw = Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg;
                else wantYaw = Yaw;
            }
            else if (seat.HasValue) wantYaw = seat.Value.rotation.eulerAngles.y;
            else if (v.TargetId.IsValid && w.TryGetPosition(v.TargetId, out float tx, out float tz) && v.State == VillagerState.Working)
            {
                var d = new Vector3(tx - p.x, 0, tz - p.z);
                if (d.sqrMagnitude > 0.04f) wantYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            if (!paused) Yaw = Mathf.MoveTowardsAngle(Yaw, wantYaw, Mathf.Max(200f, Mathf.Abs(Mathf.DeltaAngle(Yaw, wantYaw)) * 8f) * dt * Mathf.Max(1f, simSpeed * 0.5f));

            var rot = Quaternion.Euler(0, Yaw, 0);
            float spawnScale = 1f;
            if (_spawn < 1f) { _spawn = Mathf.Min(1f, _spawn + dt * 2f); spawnScale = Ease.OutBack(_spawn, 2f); }
            Root.transform.SetPositionAndRotation(p, rot);
            if (_spawn < 1f || _spawnPending)
            {
                _spawnPending = _spawn < 1f;
                Root.transform.localScale = _baseScale * Mathf.Max(0.01f, _spawn < 1f ? spawnScale : 1f);
            }

            string state = ChooseState(w, v, seat.HasValue);
            if (_celebrate > 0f) { _celebrate -= dt; if (!v.IsMoving && v.State != VillagerState.Sleeping) state = VillagerAnim.Cheer; }
            if (state != _state)
            {
                _state = state;
                _stateTime = 0f;
                _lastPhase = 0f;
                if (Anim != null && Anim.runtimeAnimatorController != null) Anim.CrossFadeInFixedTime(state, state == VillagerAnim.Locomotion ? 0.18f : 0.25f, 0);
            }
            _stateTime += dt;

            if (Anim != null && Anim.runtimeAnimatorController != null)
            {
                Anim.speed = paused ? 0f : Mathf.Clamp(simSpeed, 1f, 4f);
                if (state == VillagerAnim.Locomotion)
                {
                    float baseSpeed = Mathf.Max(0.4f, Speed);
                    float gait = VillagerAnim.SolveGait(Mathf.Min(baseSpeed, 3.2f));
                    Anim.SetFloat(VillagerAnim.GaitParam, gait, 0.15f, dt);
                    Anim.SetFloat(VillagerAnim.SpeedParam, Mathf.Clamp(baseSpeed / VillagerAnim.VisualSpeed(gait), 0.5f, 1.8f));
                }
                else Anim.SetFloat(VillagerAnim.SpeedParam, 1f);
            }
            else if (_fbBody != null) AnimateFallback(state, dt * (paused ? 0f : simSpeed));

            UpdateProps(w, v, state);
            SyncActionEvents(w, v, state, dt, paused);
            Footsteps(w, v, state, dt, simSpeed, paused);
        }

        private string ChooseState(WorldState w, Villager v, bool seated)
        {
            if (v.IsMoving && Speed > 0.08f) return VillagerAnim.Locomotion;
            if (v.IsMoving) return _state == VillagerAnim.Locomotion ? VillagerAnim.Locomotion : VillagerAnim.Idle;
            switch (v.Action)
            {
                case VillagerAction.Chop: return VillagerAnim.Chop;
                case VillagerAction.Build:
                {
                    var b = w.FindBuilding(v.TargetId);
                    return b != null && (b.Stage == ConstructionStage.Foundation || b.Kind == BuildingKind.Field || b.Kind == BuildingKind.Bridge) ? VillagerAnim.Kneel : VillagerAnim.Hammer;
                }
                case VillagerAction.Sow: return VillagerAnim.Kneel;
                case VillagerAction.Harvest: return VillagerAnim.Harvest;
                case VillagerAction.Fish: return VillagerAnim.Fish;
                case VillagerAction.DrawWater: return VillagerAnim.Interact;
                case VillagerAction.PickUp:
                case VillagerAction.DropOff: return VillagerAnim.PickUp;
                case VillagerAction.Eat: return seated ? VillagerAnim.Sit : VillagerAnim.Talk;
                case VillagerAction.Drink: return VillagerAnim.Interact;
                case VillagerAction.Sleep: return VillagerAnim.Lie;
                case VillagerAction.Sit: return seated ? (_owner.HasNeighbour(this, 3.5f) ? VillagerAnim.SitTalk : VillagerAnim.Sit) : VillagerAnim.Talk;
                case VillagerAction.Wander:
                case VillagerAction.Wait:
                case VillagerAction.None:
                default:
                    if (v.Weak) return VillagerAnim.Crouch;
                    if (v.State == VillagerState.Sleeping) return VillagerAnim.Lie;
                    if (w.IsEvening && _owner.HasNeighbour(this, 2.8f)) return VillagerAnim.Talk;
                    return VillagerAnim.Idle;
            }
        }

        // ------------------------------------------------------------------ props

        private void UpdateProps(WorldState w, Villager v, string state)
        {
            PropKind right = PropKind.None, left = PropKind.None, carry = PropKind.None;
            switch (state)
            {
                case VillagerAnim.Chop: right = PropKind.Axe; break;
                case VillagerAnim.Hammer: right = PropKind.Hammer; break;
                case VillagerAnim.Kneel: right = v.Action == VillagerAction.Sow ? PropKind.None : PropKind.Hammer; left = v.Action == VillagerAction.Sow ? PropKind.SeedBag : PropKind.None; break;
                case VillagerAnim.Harvest: right = PropKind.Sickle; left = PropKind.Basket; break;
                case VillagerAnim.Fish: right = PropKind.Rod; break;
                case VillagerAnim.Interact: if (v.Action == VillagerAction.DrawWater) right = PropKind.Bucket; break;
            }
            if (v.Carry != null && !v.Carry.IsEmpty && state != VillagerAnim.Chop)
            {
                int wood = v.Carry.Get(ItemType.Wood), food = v.Carry.Get(ItemType.Food), water = v.Carry.Get(ItemType.Water);
                if (wood > 0) carry = wood >= 3 ? PropKind.Logs : PropKind.Log;
                else if (food > 0) carry = PropKind.Sack;
                else if (water > 0 && right == PropKind.None) right = PropKind.WaterBucket;
            }
            else if (v.Role == JobRole.Woodcutter && state == VillagerAnim.Locomotion && v.Purpose == WalkPurpose.Task && right == PropKind.None) right = PropKind.Axe;
            else if (v.Role == JobRole.Builder && state == VillagerAnim.Locomotion && v.Purpose == WalkPurpose.Task && right == PropKind.None) right = PropKind.Hammer;

            SetHandProp(ref _right, ref _rightKind, right, true);
            SetHandProp(ref _left, ref _leftKind, left, false);
            if (carry != _carryKind)
            {
                if (_carry != null) Object.Destroy(_carry);
                _carry = carry != PropKind.None ? PropKit.Build(carry, Root.transform) : null;
                _carryKind = carry;
            }
            if (_carry != null) PlaceCarry();

            bool bedroll = state == VillagerAnim.Lie;
            if (bedroll && _bedroll == null)
            {
                _bedroll = new GameObject("Bedroll");
                _bedroll.transform.SetParent(Root.transform, false);
                MeshKit.Spawn(MeshKit.Box, MaterialLibrary.Lit(new Color(0.55f, 0.36f, 0.3f), 0.05f), _bedroll.transform, new Vector3(0, 0.04f, -0.6f), Quaternion.identity, new Vector3(0.8f, 0.07f, 1.9f), false, "Mat");
                MeshKit.Spawn(MeshKit.Cylinder, MaterialLibrary.ForKey("Cloth"), _bedroll.transform, new Vector3(0, 0.1f, 0.25f), Quaternion.Euler(0, 0, 90), new Vector3(0.22f, 0.7f, 0.22f), false, "Pillow");
            }
            if (_bedroll != null && _bedroll.activeSelf != bedroll) _bedroll.SetActive(bedroll);

            if (_rightKind == PropKind.Rod) UpdateFishingLine(w);
            else if (_line != null && _line.enabled) _line.enabled = false;
        }

        private void SetHandProp(ref GameObject go, ref PropKind current, PropKind want, bool rightHand)
        {
            if (want == current) return;
            if (go != null) Object.Destroy(go);
            go = null; current = want;
            if (want == PropKind.None) return;
            Transform hand = Rig != null ? (rightHand ? Rig.RightHand : Rig.LeftHand) : (rightHand ? _fbArmR : _fbArmL);
            if (hand == null) hand = Root.transform;
            go = PropKit.Build(want, hand);
            var t = go.transform;
            if (Rig != null && Rig.BonesFound)
            {
                t.localPosition = rightHand ? Rig.RightHandLocalOffset : Rig.LeftHandLocalOffset;
                t.localRotation = rightHand ? Rig.RightHandBindToProp : Rig.LeftHandBindToProp;
                // Hanging props (buckets/baskets/bags) stay upright: handled per frame in PlaceCarry.
                float ls = hand.lossyScale.x;
                t.localScale = Vector3.one * (ls > 1e-4f ? Root.transform.lossyScale.x / ls : 1f);
            }
            else
            {
                t.localPosition = new Vector3(0, -0.6f, 0.05f);
                t.localRotation = Quaternion.Euler(90, 0, 0);
            }
            if (want == PropKind.Rod) _rodTip = t.Find("Tip");
        }

        private void PlaceCarry()
        {
            var rot = Quaternion.Euler(0, Yaw, 0);
            var t = _carry.transform;
            float s = Root.transform.lossyScale.x;
            if (Rig != null && Rig.BonesFound)
            {
                switch (_carryKind)
                {
                    case PropKind.Log:
                    case PropKind.Logs:
                    {
                        var shoulder = Rig.RightUpperArm != null ? Rig.RightUpperArm.position : Rig.Neck != null ? Rig.Neck.position + rot * Vector3.right * 0.15f : Position + Vector3.up * 1.45f;
                        t.position = shoulder + Vector3.up * 0.13f * s - rot * Vector3.right * 0.03f * s;
                        t.rotation = rot * Quaternion.Euler(-10f, 4f, 0);
                        break;
                    }
                    case PropKind.Sack:
                    {
                        var chest = Rig.Chest != null ? Rig.Chest.position : Position + Vector3.up * 1.2f;
                        t.position = chest - rot * Vector3.forward * 0.24f * s + Vector3.up * 0.05f * s;
                        t.rotation = rot * Quaternion.Euler(12f, 0, 0);
                        break;
                    }
                }
            }
            else
            {
                t.localPosition = _carryKind == PropKind.Sack ? new Vector3(0, 1.25f, -0.25f) : new Vector3(0.22f, 1.55f, 0);
                t.localRotation = Quaternion.Euler(-10, 0, 0);
            }
            // Buckets/baskets in hands hang plumb whatever the hand does.
            KeepUpright(_right, _rightKind);
            KeepUpright(_left, _leftKind);
        }

        private void KeepUpright(GameObject go, PropKind kind)
        {
            if (go == null) return;
            if (kind == PropKind.Bucket || kind == PropKind.WaterBucket || kind == PropKind.Basket || kind == PropKind.SeedBag)
                go.transform.rotation = Quaternion.Euler(0, Yaw, 0);
        }

        private void UpdateFishingLine(WorldState w)
        {
            if (_rodTip == null) return;
            if (_line == null)
            {
                var go = new GameObject("FishingLine");
                go.transform.SetParent(Root.transform, false);
                _line = go.AddComponent<LineRenderer>();
                _line.positionCount = 8;
                _line.widthMultiplier = 0.012f;
                _line.useWorldSpace = true;
                _line.sharedMaterial = MaterialLibrary.Overlay(new Color(0.9f, 0.9f, 0.85f, 0.8f));
                _line.shadowCastingMode = ShadowCastingMode.Off;
                var bob = MeshKit.Spawn(MeshKit.Sphere, MaterialLibrary.Lit(new Color(0.85f, 0.2f, 0.15f), 0.4f), go.transform, Vector3.zero, Quaternion.identity, Vector3.one * 0.09f, false, "Bobber");
                bob.name = "Bobber";
                var fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
                var probe = Position + fwd * 3.2f;
                if (GameServices.Presenter != null && GameServices.Presenter.Water != null && GameServices.Presenter.Water.NearestWater(probe, out var wp, out _))
                {
                    var toWater = wp - Position; toWater.y = 0;
                    _bobber = Position + Vector3.ClampMagnitude(toWater, 4.5f);
                    _bobber.y = wp.y + 0.02f;
                }
                else _bobber = probe + Vector3.down * 0.3f;
            }
            _line.enabled = true;
            var bobT = _line.transform.Find("Bobber");
            var bp = _bobber + Vector3.up * Mathf.Sin(Time.time * 2.1f + Id.Value) * 0.025f;
            if (bobT != null) bobT.position = bp;
            var a = _rodTip.position;
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                var q = Vector3.Lerp(a, bp, t);
                q.y -= Mathf.Sin(t * Mathf.PI) * 0.35f;
                _line.SetPosition(i, q);
            }
        }

        // ------------------------------------------------------------------ sync & sound

        private void SyncActionEvents(WorldState w, Villager v, string state, float dt, bool paused)
        {
            if (paused) return;
            float phase;
            if (Anim != null && Anim.runtimeAnimatorController != null)
            {
                var info = Anim.GetCurrentAnimatorStateInfo(0);
                if (!info.IsName(state)) { _lastPhase = 0f; return; }
                phase = info.normalizedTime - Mathf.Floor(info.normalizedTime);
            }
            else phase = (_fbCycle / 1.2f) % 1f;

            bool Crossed(float at) => (_lastPhase < at && phase >= at) || (phase < _lastPhase && (at > _lastPhase || at <= phase));
            switch (state)
            {
                case VillagerAnim.Chop:
                    if (Crossed(_owner.ChopHitPhase)) _owner.Trees?.Hit(v.TargetId, Position, 1f);
                    break;
                case VillagerAnim.Hammer:
                    if (Crossed(0.45f)) Knock(v, 1f);
                    break;
                case VillagerAnim.Kneel:
                    if (v.Action == VillagerAction.Build && (Crossed(0.3f) || Crossed(0.72f))) Knock(v, 0.7f);
                    if (v.Action == VillagerAction.Sow && Crossed(0.5f)) GameServices.Fx?.Play(FxKind.Dust, Position + Root.transform.forward * 0.5f, 2, 0.4f);
                    break;
                case VillagerAnim.Harvest:
                    if (Crossed(0.5f))
                    {
                        GameServices.Fx?.Play(FxKind.Harvest, Position + Root.transform.forward * 0.6f + Vector3.up * 0.4f, 4, 0.6f);
                        GameServices.Audio?.PlayAt("harvest_cut", Position, 0.6f, Random.Range(0.9f, 1.1f));
                    }
                    break;
                case VillagerAnim.Interact:
                    if (v.Action == VillagerAction.DrawWater && Crossed(0.5f))
                    {
                        var p = Position + Root.transform.forward * 0.8f;
                        GameServices.Fx?.Play(FxKind.Splash, p, 6, 0.6f);
                        GameServices.Audio?.PlayAt("water_scoop", p, 0.7f, Random.Range(0.9f, 1.1f));
                    }
                    break;
                case VillagerAnim.Fish:
                    if (_stateTime > 1f && Random.value < dt * 0.15f && _line != null)
                    {
                        GameServices.Fx?.Play(FxKind.Water, _bobber, 4, 0.5f);
                        GameServices.Audio?.PlayAt("splash_small", _bobber, 0.4f, Random.Range(0.9f, 1.2f));
                    }
                    break;
            }
            _lastPhase = phase;
        }

        public void OnFishCaught()
        {
            if (_line == null) return;
            GameServices.Fx?.Play(FxKind.Splash, _bobber, 14, 1f);
            GameServices.Audio?.PlayAt("splash", _bobber, 0.9f, 1f);
            Celebrate(0.01f);
        }

        private void Knock(Villager v, float volume)
        {
            var b = GameServices.World?.FindBuilding(v.TargetId);
            var p = Position + Root.transform.forward * 0.7f + Vector3.up * 0.6f;
            GameServices.Audio?.PlayAt("hammer", p, volume, Random.Range(0.9f, 1.12f));
            if (Random.value < 0.35f) GameServices.Fx?.Play(FxKind.Chips, p, 3, 0.5f);
        }

        private void Footsteps(WorldState w, Villager v, string state, float dt, float simSpeed, bool paused)
        {
            if (state != VillagerAnim.Locomotion || paused) return;
            var cam = Camera.main;
            if (cam == null || (cam.transform.position - Position).sqrMagnitude > 34f * 34f) return;
            float gait = VillagerAnim.SolveGait(Mathf.Max(0.4f, Speed));
            float cycle = gait <= 1f ? Mathf.Lerp(VillagerAnim.WalkCycle, VillagerAnim.JogCycle, gait) : Mathf.Lerp(VillagerAnim.JogCycle, VillagerAnim.SprintCycle, gait - 1f);
            float mult = Mathf.Clamp(Mathf.Max(0.4f, Speed) / VillagerAnim.VisualSpeed(gait), 0.5f, 1.8f) * Mathf.Max(1f, simSpeed);
            _stepTimer -= dt * mult;
            if (_stepTimer > 0f) return;
            _stepTimer += cycle * 0.5f;
            var cell = w.Nav.WorldToCell(Position.x, Position.z);
            string key = w.Nav.IsBridge(cell.X, cell.Z) ? "step_wood" : (w.Nav.InBounds(cell.X, cell.Z) && w.Nav.Roadness(cell.X, cell.Z) > 0.45f ? "step_dirt" : "step_grass");
            GameServices.Audio?.PlayAt(key, Position, 0.35f, Random.Range(0.9f, 1.1f));
        }

        // ------------------------------------------------------------------ fallback animation (no rig)

        private void AnimateFallback(string state, float dt)
        {
            _fbCycle += dt;
            float t = _fbCycle;
            float swing = 0f, bob = 0f, lean = 0f, arm = 0f;
            switch (state)
            {
                case VillagerAnim.Locomotion: swing = Mathf.Sin(t * 7f) * 32f; bob = Mathf.Abs(Mathf.Sin(t * 7f)) * 0.05f; break;
                case VillagerAnim.Chop:
                case VillagerAnim.Hammer: arm = -60f - Mathf.Sin(t / 1.2f * Mathf.PI * 2f) * 60f; lean = 6f; break;
                case VillagerAnim.Kneel:
                case VillagerAnim.Harvest: lean = 28f; arm = -40f + Mathf.Sin(t * 5f) * 15f; bob = -0.25f; break;
                case VillagerAnim.Sit:
                case VillagerAnim.SitTalk: bob = -0.45f; break;
                case VillagerAnim.Lie: lean = 85f; bob = -0.85f; break;
                default: arm = Mathf.Sin(t * 1.3f) * 3f; break;
            }
            _fbBody.localPosition = new Vector3(0, 1.12f + bob, 0);
            _fbBody.localRotation = Quaternion.Euler(lean, 0, 0);
            _fbHead.localPosition = new Vector3(0, 1.62f + bob, Mathf.Sin(lean * Mathf.Deg2Rad) * 0.4f);
            _fbArmL.localRotation = Quaternion.Euler(-swing, 0, 0);
            _fbArmR.localRotation = Quaternion.Euler(swing + arm, 0, 0);
            _fbLegL.localRotation = Quaternion.Euler(swing, 0, 0);
            _fbLegR.localRotation = Quaternion.Euler(-swing, 0, 0);
            _fbArmL.localPosition = new Vector3(-0.25f, 1.4f + bob, 0);
            _fbArmR.localPosition = new Vector3(0.25f, 1.4f + bob, 0);
        }

        public Vector3 HeadPosition => Rig != null && Rig.Head != null ? Rig.Head.position : Position + Vector3.up * 1.7f;

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }
}
