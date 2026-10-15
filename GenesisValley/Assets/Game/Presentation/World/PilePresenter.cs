using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>Loose ground piles (dropped loads, leftover site stock) as small log/sack stacks.</summary>
    public sealed class PilePresenter : MonoBehaviour
    {
        private sealed class PileView { public GameObject Root; public int Wood = -1, Food = -1, Water = -1; public float Pop = 1f; }

        private WorldState _w;
        private readonly Dictionary<EntityId, PileView> _views = new Dictionary<EntityId, PileView>();
        private readonly HashSet<EntityId> _seen = new HashSet<EntityId>();
        private readonly List<EntityId> _remove = new List<EntityId>();

        public void Bind(WorldState w)
        {
            _w = w;
            foreach (var v in _views.Values) if (v.Root != null) Destroy(v.Root);
            _views.Clear();
        }

        private void LateUpdate()
        {
            if (_w == null) return;
            _seen.Clear();
            foreach (var p in _w.Piles)
            {
                _seen.Add(p.Id);
                if (!_views.TryGetValue(p.Id, out var v))
                {
                    v = new PileView { Root = new GameObject("Pile_" + p.Id.Value), Pop = 0f };
                    v.Root.transform.SetParent(transform, false);
                    v.Root.transform.SetPositionAndRotation(new Vector3(p.X, _w.Height.SampleHeight(p.X, p.Z), p.Z), Quaternion.Euler(0, Noise.Hash01(p.Id.Value, 1) * 360f, 0));
                    _views[p.Id] = v;
                }
                int wood = p.Inventory.Get(ItemType.Wood), food = p.Inventory.Get(ItemType.Food), water = p.Inventory.Get(ItemType.Water);
                if (wood != v.Wood || food != v.Food || water != v.Water)
                {
                    v.Wood = wood; v.Food = food; v.Water = water;
                    Rebuild(v);
                }
                if (v.Pop < 1f)
                {
                    v.Pop = Mathf.Min(1f, v.Pop + Time.deltaTime * 3f);
                    v.Root.transform.localScale = Vector3.one * Mathf.Max(0.01f, Ease.OutBack(v.Pop));
                }
            }
            _remove.Clear();
            foreach (var kv in _views) if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var id in _remove) { if (_views[id].Root != null) Destroy(_views[id].Root); _views.Remove(id); }
        }

        private static void Rebuild(PileView v)
        {
            foreach (Transform c in v.Root.transform) Destroy(c.gameObject);
            var t = v.Root.transform;
            int logs = Mathf.Clamp(Mathf.CeilToInt(v.Wood / 2f), 0, 10);
            for (int i = 0; i < logs; i++)
            {
                int row = i < 4 ? 0 : (i < 7 ? 1 : 2);
                int k = row == 0 ? i : (row == 1 ? i - 4 : i - 7);
                int inRow = 4 - row;
                MeshKit.SpawnLog(t, new Vector3((k - (inRow - 1) * 0.5f) * 0.28f, 0.13f + row * 0.23f, 0), Quaternion.Euler(0, (i * 37 % 11) - 5, 0), 1.5f, 0.25f);
            }
            int sacks = Mathf.Clamp(Mathf.CeilToInt(v.Food / 3f), 0, 6);
            var cloth = MaterialLibrary.ForKey("Cloth");
            for (int i = 0; i < sacks; i++)
                MeshKit.Spawn(MeshKit.Sphere, cloth, t, new Vector3(-0.9f + (i % 3) * 0.45f, 0.22f + (i / 3) * 0.38f, 0.6f), Quaternion.Euler(0, i * 40, 0), new Vector3(0.5f, 0.44f, 0.42f), true, "Sack");
            int buckets = Mathf.Clamp(Mathf.CeilToInt(v.Water / 2f), 0, 4);
            for (int i = 0; i < buckets; i++)
            {
                var b = PropKit.Build(PropKind.WaterBucket, t);
                b.transform.localPosition = new Vector3(0.9f + (i % 2) * 0.35f, 0.34f, -0.5f + (i / 2) * 0.35f);
            }
        }
    }
}
