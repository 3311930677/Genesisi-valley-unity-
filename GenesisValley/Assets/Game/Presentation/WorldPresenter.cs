using System;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// Owns every world view (terrain, water, trees, buildings, camp, piles, decor, villagers) and routes simulation
    /// facts to them (GDD §10: presentation consumes WorldState + events, never writes gameplay state). Everything here
    /// can be rebuilt from a snapshot, which is exactly what happens after loading a save.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class WorldPresenter : MonoBehaviour
    {
        public TerrainPresenter Terrain { get; private set; }
        public WaterPresenter Water { get; private set; }
        public TreePresenter Trees { get; private set; }
        public BuildingPresenter Buildings { get; private set; }
        public CampPresenter Camp { get; private set; }
        public PilePresenter Piles { get; private set; }
        public DecorPresenter Decor { get; private set; }
        public VillagerPresenter Villagers { get; private set; }
        public WetnessOverlay Wetness { get; private set; }

        public WorldState World { get; private set; }

        /// <summary>Every simulation event, after the views have reacted (UI story feed, toasts, objectives).</summary>
        public event Action<SimEvent> EventRaised;
        /// <summary>Raised after <see cref="Bind"/> finished building all views for a new/loaded world.</summary>
        public event Action<WorldState> WorldBound;

        private void Awake()
        {
            GameServices.Presenter = this;
            Terrain = Child<TerrainPresenter>("Terrain");
            Water = Child<WaterPresenter>("Water");
            Trees = Child<TreePresenter>("Trees");
            Buildings = Child<BuildingPresenter>("Buildings");
            Camp = Child<CampPresenter>("Camp");
            Piles = Child<PilePresenter>("Piles");
            Decor = Child<DecorPresenter>("Decor");
            Villagers = Child<VillagerPresenter>("Villagers");
            Wetness = Child<WetnessOverlay>("WetnessOverlay");
            Villagers.Trees = Trees;
            Villagers.Camp = Camp;
        }

        private void OnDestroy() { if (GameServices.Presenter == this) GameServices.Presenter = null; }

        private T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        public void Bind(WorldState w)
        {
            World = w;
            // Terrain first: everything else snaps to its heights.
            // Each layer binds independently: a missing asset in one layer must never abort world loading.
            SafeBind("Terrain", () => Terrain.Bind(w));
            SafeBind("Water", () => Water.Bind(w));
            SafeBind("Trees", () => Trees.Bind(w));
            SafeBind("Buildings", () => Buildings.Bind(w));
            SafeBind("Camp", () => Camp.Bind(w));
            SafeBind("Piles", () => Piles.Bind(w));
            SafeBind("Decor", () => Decor.Bind(w));
            SafeBind("Villagers", () => Villagers.Bind(w));
            w.PresentationFullRefresh = false;
            // Events produced while generating/loading are history, not news.
            w.Events.Pending.Clear();
            WorldBound?.Invoke(w);
        }

        private static void SafeBind(string layer, System.Action bind)
        {
            try { bind(); }
            catch (System.Exception e)
            {
                Debug.LogError($"[GenesisValley] 绑定 {layer} 图层失败，已跳过该图层继续加载：{e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>Called by <see cref="TerrainPresenter"/> after heights were uploaded for a world-space rectangle.</summary>
        public void OnTerrainChanged(Rect r)
        {
            if (World == null) return;
            Trees.Resnap(r);
            Buildings.Resnap(r);
            Camp.Resnap(r);
            Decor.MarkDirty(r);
        }

        /// <summary>Repaints ground (soil, aprons, paths) around a footprint and refreshes dressing there.</summary>
        public void MarkFootprintDirty(float x, float z, float radius)
        {
            var r = new Rect(x - radius, z - radius, radius * 2f, radius * 2f);
            Terrain.MarkWorldRectDirty(r);
            Decor.MarkDirty(r);
        }

        /// <summary>Drains the sim's pending events (call once per frame after stepping).</summary>
        public void DispatchEvents()
        {
            var w = World;
            if (w == null) return;
            var pending = w.Events.Pending;
            if (pending.Count == 0) return;
            // Copy first: handlers may submit commands that emit more events.
            var batch = pending.ToArray();
            pending.Clear();
            foreach (var e in batch)
            {
                React(w, e);
                Villagers.OnEvent(e);
                GameServices.Audio?.OnSimEvent(e);
                try { EventRaised?.Invoke(e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private void React(WorldState w, in SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.SitePlanned:
                case SimEventType.BuildingCompleted:
                case SimEventType.SiteStageStarted:
                {
                    var b = w.FindBuilding(e.Target) ?? w.FindBuilding(e.Actor);
                    if (b != null) MarkFootprintDirty(b.X, b.Z, b.BoundingRadius + 5f);
                    else MarkFootprintDirty(e.X, e.Z, 14f);
                    break;
                }
                case SimEventType.SiteCancelled:
                    MarkFootprintDirty(e.X, e.Z, 16f);
                    break;
                case SimEventType.FieldSown:
                case SimEventType.FieldHarvested:
                    MarkFootprintDirty(e.X, e.Z, 10f);
                    break;
                case SimEventType.RiverCreated:
                case SimEventType.UndoApplied:
                    // Heights arrive through PresentationHeightDirty; water flags change the bank paint.
                    break;
                case SimEventType.ForestPlanted:
                    GameServices.Fx?.Play(FxKind.Leaves, new Vector3(e.X, w.Height.SampleHeight(e.X, e.Z) + 1.5f, e.Z), 18, 3f);
                    break;
            }
        }

        // ------------------------------------------------------------------ queries used by tools/UI

        public float HeightAt(float x, float z) => World != null ? Villagers.GroundHeight(x, z) : 0f;

        public Vector3 Ground(float x, float z) => new Vector3(x, HeightAt(x, z), z);

        /// <summary>World-space point above an entity for labels, follow and fly-to.</summary>
        public bool TryGetAnchor(EntityId id, out Vector3 p)
        {
            p = default;
            var w = World;
            if (w == null || !id.IsValid) return false;
            if (Villagers.TryGetPosition(id, out p)) { p += Vector3.up * 2.1f; return true; }
            var b = w.FindBuilding(id);
            if (b != null) { p = Buildings.LabelAnchor(b); return true; }
            var t = w.FindTree(id);
            if (t != null) { p = Ground(t.X, t.Z) + Vector3.up * Trees.CanopyHeight(id); return true; }
            var s = w.FindStorage(id);
            if (s != null) { p = Ground(s.X, s.Z) + Vector3.up * 3f; return true; }
            var pile = w.FindPile(id);
            if (pile != null) { p = Ground(pile.X, pile.Z) + Vector3.up * 1f; return true; }
            if (w.TryGetPosition(id, out float x, out float z)) { p = Ground(x, z) + Vector3.up; return true; }
            return false;
        }
    }
}
