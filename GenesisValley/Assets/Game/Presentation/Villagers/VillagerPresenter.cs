using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>Creates/destroys <see cref="VillagerView"/>s, owns tick interpolation snapshots, seats and bridge decks.</summary>
    public sealed class VillagerPresenter : MonoBehaviour
    {
        [Range(0, 1)] public float ChopHitPhase = 0.42f;

        private WorldState _w;
        private readonly Dictionary<EntityId, VillagerView> _views = new Dictionary<EntityId, VillagerView>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly List<EntityId> _remove = new List<EntityId>();
        private readonly List<BuildingEntity> _bridges = new List<BuildingEntity>();
        private readonly Dictionary<int, EntityId> _seatOwner = new Dictionary<int, EntityId>();
        private bool _initial;

        public TreePresenter Trees;
        public CampPresenter Camp;

        public IReadOnlyDictionary<EntityId, VillagerView> Views => _views;
        public bool TryGetView(EntityId id, out VillagerView v) => _views.TryGetValue(id, out v);

        public void Bind(WorldState w)
        {
            _w = w;
            foreach (var v in _views.Values) v.Destroy();
            _views.Clear();
            _seatOwner.Clear();
            _initial = true;
            Sync();
            _initial = false;
        }

        /// <summary>Call right before every SimulationRunner.Step so views can interpolate the last tick.</summary>
        public void BeforeStep()
        {
            if (_w == null) return;
            foreach (var v in _w.Villagers)
            {
                if (_views.TryGetValue(v.Id, out var view)) { view.PrevX = v.X; view.PrevZ = v.Z; }
            }
        }

        private void Sync()
        {
            _seen.Clear();
            foreach (var v in _w.Villagers)
            {
                _seen.Add(v.Id);
                if (!_views.TryGetValue(v.Id, out var view))
                {
                    var prefab = v.Gender == 1 ? GameServices.Art.femaleVillager : GameServices.Art.maleVillager;
                    if (prefab == null) prefab = GameServices.Art.maleVillager != null ? GameServices.Art.maleVillager : GameServices.Art.femaleVillager;
                    view = new VillagerView(this, v, prefab, transform);
                    _views[v.Id] = view;
                    if (!_initial)
                    {
                        view.PlaySpawn();
                        GameServices.Fx?.Play(FxKind.Sparkle, view.Position + Vector3.up, 12, 0.8f);
                        GameServices.Fx?.Play(FxKind.Poof, view.Position + Vector3.up * 0.4f, 6, 0.7f);
                    }
                }
                view.CurX = v.X; view.CurZ = v.Z;
                // Teleports (relocation after terrain edits) snap instead of sliding across the map.
                if ((view.PrevX - v.X) * (view.PrevX - v.X) + (view.PrevZ - v.Z) * (view.PrevZ - v.Z) > 36f) { view.PrevX = v.X; view.PrevZ = v.Z; }
            }
            _remove.Clear();
            foreach (var kv in _views) if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var id in _remove)
            {
                var view = _views[id];
                GameServices.Fx?.Play(FxKind.Poof, view.Position + Vector3.up * 0.5f, 10, 0.8f);
                view.Destroy();
                _views.Remove(id);
                ReleaseSeat(id);
            }
        }

        private void LateUpdate()
        {
            if (_w == null) return;
            Sync();
            _bridges.Clear();
            foreach (var b in _w.Buildings) if (b.Kind == BuildingKind.Bridge && b.IsComplete) _bridges.Add(b);
            var game = GameServices.Game;
            float alpha = game != null ? game.InterpolationAlpha : 1f;
            float speed = game != null ? game.SpeedMultiplier : 1f;
            bool paused = game != null && game.IsPaused;
            float dt = Time.deltaTime;
            foreach (var v in _w.Villagers)
            {
                if (!_views.TryGetValue(v.Id, out var view)) continue;
                UpdateSeat(v, view);
                view.Tick(_w, v, alpha, dt, speed, paused);
            }
        }

        // ------------------------------------------------------------------ seats around the campfire

        private void UpdateSeat(Villager v, VillagerView view)
        {
            bool wants = !v.IsMoving && (v.Action == VillagerAction.Sit || v.Action == VillagerAction.Eat) && Camp != null;
            if (!wants) { if (view.SeatIndex >= 0) ReleaseSeat(v.Id); view.SeatIndex = -1; return; }
            if (view.SeatIndex >= 0) return;
            int best = -1; float bd = 4.5f * 4.5f;
            for (int i = 0; i < Camp.SeatCount; i++)
            {
                if (_seatOwner.ContainsKey(i)) continue;
                var p = Camp.Seat(i).position;
                float d = (p.x - v.X) * (p.x - v.X) + (p.z - v.Z) * (p.z - v.Z);
                if (d < bd) { bd = d; best = i; }
            }
            if (best >= 0) { _seatOwner[best] = v.Id; view.SeatIndex = best; }
        }

        private void ReleaseSeat(EntityId id)
        {
            int found = -1;
            foreach (var kv in _seatOwner) if (kv.Value == id) { found = kv.Key; break; }
            if (found >= 0) _seatOwner.Remove(found);
        }

        public Pose? SeatPose(int i) => Camp != null && i >= 0 && i < Camp.SeatCount ? Camp.Seat(i) : (Pose?)null;

        public bool HasNeighbour(VillagerView self, float radius)
        {
            float r2 = radius * radius;
            foreach (var v in _views.Values)
            {
                if (v == self || !v.Visible) continue;
                if ((v.Position - self.Position).sqrMagnitude < r2) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ ground

        public float GroundHeight(float x, float z)
        {
            float h = _w != null ? _w.Height.SampleHeight(x, z) : 0f;
            foreach (var b in _bridges)
            {
                float ex = b.BX - b.AX, ez = b.BZ - b.AZ;
                float l2 = ex * ex + ez * ez;
                if (l2 < 0.01f) continue;
                float t = ((x - b.AX) * ex + (z - b.AZ) * ez) / l2;
                if (t < -0.05f || t > 1.05f) continue;
                float px = b.AX + ex * t, pz = b.AZ + ez * t;
                if ((x - px) * (x - px) + (z - pz) * (z - pz) > 1.5f * 1.5f) continue;
                float tc = Mathf.Clamp01(t);
                float deck = Mathf.Lerp(b.AY, b.BY, tc) + 0.18f + BuildingPresenter.BridgeRise(b) * 4f * tc * (1f - tc) + 0.1f;
                h = Mathf.Max(h, deck);
            }
            return h;
        }

        // ------------------------------------------------------------------ events

        public void OnEvent(in SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.FishCaught:
                    if (_views.TryGetValue(e.Actor, out var fisher)) fisher.OnFishCaught();
                    break;
                case SimEventType.Milestone:
                case SimEventType.BuildingCompleted:
                {
                    // People nearby stop for a little celebration.
                    var p = new Vector3(e.X, 0, e.Z);
                    foreach (var v in _views.Values)
                    {
                        var q = v.Position; q.y = 0;
                        if ((q - p).sqrMagnitude < 22f * 22f) v.Celebrate(e.Type == SimEventType.Milestone ? 3.2f : 1.8f);
                    }
                    break;
                }
                case SimEventType.VillagersArrived:
                    GameServices.Fx?.SummonBeam(new Vector3(e.X, GroundHeight(e.X, e.Z), e.Z));
                    break;
            }
        }

        public bool TryGetPosition(EntityId id, out Vector3 p)
        {
            if (_views.TryGetValue(id, out var v)) { p = v.Position; return true; }
            p = default; return false;
        }
    }
}
