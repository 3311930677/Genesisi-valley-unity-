using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Simulation;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>
    /// The founding camp: campfire (particles + flickering light that carries the village at night), log benches with
    /// seats, tents, a banner, a wagon, and stock piles whose size follows the camp inventory so the player can read
    /// food/wood/water at a glance (GDD §7.2: readable structure over decoration).
    /// </summary>
    public sealed class CampPresenter : MonoBehaviour
    {
        private WorldState _w;
        private EntityId _campId;
        private GameObject _root;
        private Transform _woodStack, _foodStack, _waterStack, _flag;
        private int _shownWood = -1, _shownFood = -1, _shownWater = -1;
        private Light _fireLight;
        private ParticleSystem _fire, _embers;
        private readonly List<Pose> _seats = new List<Pose>();
        private float _yaw;
        private bool _initial;

        public int SeatCount => _seats.Count;
        public Pose Seat(int i) => _seats[i];
        public Vector3 FirePosition { get; private set; }
        public bool HasCamp => _root != null;

        public void Bind(WorldState w)
        {
            _w = w;
            Clear();
            _initial = true;
            Sync();
            _initial = false;
        }

        private void Clear()
        {
            if (_root != null) Destroy(_root);
            _root = null; _campId = EntityId.None; _seats.Clear();
            _shownWood = _shownFood = _shownWater = -1;
        }

        private void LateUpdate()
        {
            if (_w == null) return;
            Sync();
            if (_root == null) return;
            float night = GameServices.Sky != null ? GameServices.Sky.NightFactor : 0f;
            float t = Time.time;
            float flicker = 1f + 0.12f * Mathf.Sin(t * 9.1f) + 0.08f * Mathf.Sin(t * 15.7f + 1.3f) + 0.05f * Mathf.Sin(t * 23.3f);
            if (_fireLight != null)
            {
                _fireLight.intensity = Mathf.Lerp(0.35f, 3.2f, night) * flicker;
                _fireLight.range = Mathf.Lerp(8f, 17f, night);
                _fireLight.transform.localPosition = new Vector3(0.05f * Mathf.Sin(t * 5f), 1.2f + 0.05f * Mathf.Sin(t * 7f), 0.05f * Mathf.Cos(t * 6f));
            }
            if (_fire != null)
            {
                var em = _fire.emission;
                em.rateOverTimeMultiplier = Mathf.Lerp(14f, 26f, night);
            }
            if (_flag != null)
            {
                float wind = GameServices.Sky != null ? GameServices.Sky.WindStrength : 0.5f;
                _flag.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 1.7f) * 12f * wind, Mathf.Sin(t * 3.1f) * 3f);
                _flag.localScale = new Vector3(1f, 1f, 1f + 0.06f * Mathf.Sin(t * 4.3f));
            }
        }

        private void Sync()
        {
            var camp = _w.Camp;
            if (camp == null || camp.Kind != StorageKind.Camp) { if (_root != null) Clear(); return; }
            if (camp.Id != _campId || _root == null)
            {
                Clear();
                Build(camp);
                if (!_initial)
                {
                    GameServices.Fx?.Play(FxKind.Poof, FirePosition + Vector3.up, 30, 4f);
                    GameServices.Fx?.Play(FxKind.Sparkle, FirePosition + Vector3.up * 2f, 50, 5f);
                }
            }
            int wood = camp.Inventory.Get(ItemType.Wood), food = camp.Inventory.Get(ItemType.Food), water = camp.Inventory.Get(ItemType.Water);
            if (wood != _shownWood) { _shownWood = wood; RebuildWood(wood); }
            if (food != _shownFood) { _shownFood = food; RebuildFood(food); }
            if (water != _shownWater) { _shownWater = water; RebuildWater(water); }
        }

        private Vector3 Local(float angleDeg, float radius, float yOffset = 0f)
        {
            float a = (angleDeg + _yaw) * Mathf.Deg2Rad;
            float x = _w.Camp.X + Mathf.Sin(a) * radius, z = _w.Camp.Z + Mathf.Cos(a) * radius;
            return new Vector3(x, _w.Height.SampleHeight(x, z) + yOffset, z);
        }

        private Quaternion Facing(Vector3 from) { var d = FirePosition - from; d.y = 0; return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : Quaternion.identity; }

        private void Build(StorageEntity camp)
        {
            _campId = camp.Id;
            _yaw = Noise.Hash01(camp.Id.Value, 77) * 360f;
            _root = new GameObject("Camp_" + camp.Id.Value);
            _root.transform.SetParent(transform, false);
            var rt = _root.transform;
            FirePosition = new Vector3(camp.X, _w.Height.SampleHeight(camp.X, camp.Z), camp.Z);

            // --- campfire
            var fire = new GameObject("Campfire").transform;
            fire.SetParent(rt, false);
            fire.position = FirePosition;
            var stone = MaterialLibrary.ForKey("Stone");
            var rockPrefab = GameServices.Art.Model("Rock_Medium_1");
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 0.85f, 0.05f, Mathf.Sin(a) * 0.85f);
                if (rockPrefab != null)
                {
                    var r = Instantiate(GameServices.Art.Model("Rock_Medium_" + (1 + i % 3)) ?? rockPrefab, fire, false);
                    r.transform.localPosition = p + Vector3.down * 0.05f;
                    r.transform.localRotation = Quaternion.Euler(0, i * 47f, 0);
                    r.transform.localScale *= 0.15f + 0.04f * Noise.Hash01(i, 3);
                    foreach (var c in r.GetComponentsInChildren<Collider>()) Destroy(c);
                }
                else MeshKit.Spawn(MeshKit.Sphere, stone, fire, p, Quaternion.Euler(0, i * 40, 0), new Vector3(0.38f, 0.26f, 0.32f), true, "Stone");
            }
            for (int i = 0; i < 4; i++)
                MeshKit.SpawnLog(fire, new Vector3(0, 0.16f + i * 0.05f, 0), Quaternion.Euler(-18f + (i % 2) * 6f, i * 50f, 0), 1.1f, 0.14f);
            MeshKit.Spawn(MeshKit.Cylinder, MaterialLibrary.Lit(new Color(0.12f, 0.1f, 0.09f), 0.02f), fire, new Vector3(0, 0.02f, 0), Quaternion.identity, new Vector3(1.4f, 0.03f, 1.4f), false, "Ash");
            if (GameServices.Fx != null)
            {
                _fire = GameServices.Fx.CreateFire(fire, new Vector3(0, 0.25f, 0));
                _embers = GameServices.Fx.CreateEmbers(fire, new Vector3(0, 0.5f, 0));
                var smoke = GameServices.Fx.CreateChimneySmoke(fire, new Vector3(0, 1.4f, 0));
                if (smoke != null) { var m = smoke.main; m.startSizeMultiplier *= 0.8f; }
            }
            var lgo = new GameObject("FireLight");
            lgo.transform.SetParent(fire, false);
            _fireLight = lgo.AddComponent<Light>();
            _fireLight.type = LightType.Point;
            _fireLight.color = new Color(1f, 0.62f, 0.3f);
            _fireLight.shadows = LightShadows.Soft;
            _fireLight.shadowStrength = 0.6f;

            // --- benches (logs) with two seats each, facing the fire
            _seats.Clear();
            for (int i = 0; i < 4; i++)
            {
                float ang = 45f + i * 90f + (Noise.Hash01(camp.Id.Value, i) - 0.5f) * 14f;
                var c = Local(ang, 3.1f);
                var face = Facing(c);
                var bench = new GameObject("Bench").transform;
                bench.SetParent(rt, false);
                bench.SetPositionAndRotation(c, face);
                MeshKit.SpawnLog(bench, new Vector3(0, 0.22f, 0), Quaternion.Euler(0, 90, 0), 2.1f, 0.42f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var sp = bench.TransformPoint(new Vector3(s * 0.5f, 0, -0.18f));
                    sp.y = c.y + 0.02f;
                    _seats.Add(new Pose(sp, face));
                }
            }

            // --- tents
            var tentCloth = new[] { new Color(0.86f, 0.8f, 0.66f), new Color(0.74f, 0.62f, 0.48f), new Color(0.8f, 0.72f, 0.6f) };
            float[] tentAngles = { 20f, 150f, 255f };
            for (int i = 0; i < 3; i++)
            {
                var p = Local(tentAngles[i], 7.6f);
                var tent = new GameObject("Tent").transform;
                tent.SetParent(rt, false);
                tent.SetPositionAndRotation(p, Facing(p));
                var cloth = MaterialLibrary.Lit(tentCloth[i], 0.05f);
                MeshKit.Spawn(MeshKit.Tent, cloth, tent, Vector3.zero, Quaternion.identity, new Vector3(2.6f, 1.9f, 3.2f), true, "Canvas");
                var wood = MaterialLibrary.ForKey("DarkWood");
                MeshKit.Spawn(MeshKit.Cylinder, wood, tent, new Vector3(0, 1.0f, 1.62f), Quaternion.identity, new Vector3(0.07f, 2.05f, 0.07f), true, "Pole");
                MeshKit.Spawn(MeshKit.Cylinder, wood, tent, new Vector3(0, 1.0f, -1.62f), Quaternion.identity, new Vector3(0.07f, 2.05f, 0.07f), true, "Pole");
                MeshKit.Spawn(MeshKit.Cylinder, wood, tent, new Vector3(0, 1.93f, 0), Quaternion.Euler(90, 0, 0), new Vector3(0.06f, 3.4f, 0.06f), true, "Ridge");
                var rope = MaterialLibrary.ForKey("Rope");
                foreach (var side in new[] { -1f, 1f })
                {
                    MeshKit.Spawn(MeshKit.Box, rope, tent, new Vector3(side * 1.8f, 0.5f, 1.9f), Quaternion.Euler(0, 0, side * 52f), new Vector3(0.015f, 1.4f, 0.015f), false, "GuyRope");
                    MeshKit.Spawn(MeshKit.Box, MaterialLibrary.ForKey("Wood"), tent, new Vector3(side * 2.3f, 0.1f, 1.9f), Quaternion.identity, new Vector3(0.05f, 0.25f, 0.05f), true, "Peg");
                }
                // Bedroll peeking out of the doorway.
                MeshKit.Spawn(MeshKit.Box, MaterialLibrary.Lit(new Color(0.5f, 0.33f, 0.27f), 0.05f), tent, new Vector3(0.2f, 0.05f, 0.9f), Quaternion.Euler(0, 8, 0), new Vector3(0.75f, 0.08f, 1.6f), false, "Bedroll");
            }

            // --- banner
            {
                var p = Local(-30f, 5.6f);
                var pole = new GameObject("Banner").transform;
                pole.SetParent(rt, false);
                pole.SetPositionAndRotation(p, Quaternion.Euler(0, _yaw + 90f, 0));
                MeshKit.Spawn(MeshKit.Cylinder, MaterialLibrary.ForKey("DarkWood"), pole, new Vector3(0, 2.1f, 0), Quaternion.identity, new Vector3(0.1f, 4.2f, 0.1f), true, "Pole");
                MeshKit.Spawn(MeshKit.Sphere, MaterialLibrary.ForKey("Wood"), pole, new Vector3(0, 4.25f, 0), Quaternion.identity, Vector3.one * 0.16f, true, "Knob");
                _flag = new GameObject("FlagPivot").transform;
                _flag.SetParent(pole, false);
                _flag.localPosition = new Vector3(0, 3.6f, 0);
                var flagMat = MaterialLibrary.Lit(new Color(0.62f, 0.27f, 0.2f), 0.05f);
                MeshKit.Spawn(MeshKit.Box, flagMat, _flag, new Vector3(0, -0.35f, 0.62f), Quaternion.identity, new Vector3(0.03f, 0.8f, 1.2f), true, "Flag");
                MeshKit.Spawn(MeshKit.Box, MaterialLibrary.Lit(new Color(0.9f, 0.78f, 0.45f), 0.05f), _flag, new Vector3(0.02f, -0.35f, 0.62f), Quaternion.identity, new Vector3(0.035f, 0.18f, 0.5f), false, "Emblem");
            }

            // --- wagon & crates
            var wagon = GameServices.Art.Model("Prop_Wagon");
            if (wagon != null)
            {
                var p = Local(320f, 9.4f);
                var g = Instantiate(wagon, rt, false);
                g.transform.SetPositionAndRotation(p, Quaternion.Euler(0, _yaw + 70f, 0) * wagon.transform.rotation);
                foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
            }

            // --- stock piles (rebuilt from inventory)
            _woodStack = Group("WoodStock", Local(95f, 5.4f), _yaw + 95f);
            _foodStack = Group("FoodStock", Local(205f, 5.2f), _yaw + 205f);
            _waterStack = Group("WaterStock", Local(290f, 4.9f), _yaw + 290f);
        }

        private Transform Group(string name, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root.transform, false);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return t;
        }

        private static void ClearChildren(Transform t) { foreach (Transform c in t) Destroy(c.gameObject); }

        private void RebuildWood(int wood)
        {
            ClearChildren(_woodStack);
            int logs = Mathf.Clamp(Mathf.CeilToInt(wood / 2f), 0, 36);
            int i = 0;
            for (int row = 0; i < logs && row < 6; row++)
            {
                int inRow = 7 - row;
                for (int k = 0; k < inRow && i < logs; k++, i++)
                {
                    float x = (k - (inRow - 1) * 0.5f) * 0.32f;
                    MeshKit.SpawnLog(_woodStack, new Vector3(x, 0.15f + row * 0.27f, 0), Quaternion.Euler(0, (Noise.Hash01(i, 11) - 0.5f) * 8f, 0), 2.2f + Noise.Hash01(i, 5) * 0.3f, 0.3f);
                }
            }
            if (logs > 0)
            {
                // Chopping block + axe stuck in it.
                MeshKit.Spawn(MeshKit.Cylinder, new[] { MaterialLibrary.ForKey("Bark"), MaterialLibrary.ForKey("EndGrain") }, _woodStack, new Vector3(1.8f, 0.3f, 0.6f), Quaternion.identity, new Vector3(0.6f, 0.6f, 0.6f), true, "Block");
                var axe = PropKit.Build(PropKind.Axe, _woodStack);
                axe.transform.localPosition = new Vector3(1.8f, 0.85f, 0.55f);
                axe.transform.localRotation = Quaternion.Euler(-25, 30, 180);
            }
        }

        private void RebuildFood(int food)
        {
            ClearChildren(_foodStack);
            int units = Mathf.Clamp(Mathf.CeilToInt(food / 4f), 0, 24);
            var crate = GameServices.Art.Model("Prop_Crate");
            var sack = MaterialLibrary.ForKey("Cloth");
            var rope = MaterialLibrary.ForKey("Rope");
            for (int i = 0; i < units; i++)
            {
                bool isCrate = i % 3 == 2 && crate != null;
                int layer = i / 8, slot = i % 8;
                float x = (slot % 4 - 1.5f) * 0.72f, z = (slot / 4 - 0.5f) * 0.72f;
                float y = layer * 0.55f;
                if (isCrate)
                {
                    var c = Instantiate(crate, _foodStack, false);
                    c.transform.localPosition = new Vector3(x, y, z);
                    c.transform.localRotation = Quaternion.Euler(0, Noise.Hash01(i, 2) * 30f, 0) * crate.transform.localRotation;
                    c.transform.localScale = crate.transform.localScale * 0.62f;
                    foreach (var col in c.GetComponentsInChildren<Collider>()) Destroy(col);
                }
                else
                {
                    var s = MeshKit.Spawn(MeshKit.Sphere, sack, _foodStack, new Vector3(x, y + 0.27f, z), Quaternion.Euler(0, Noise.Hash01(i, 7) * 90f, Noise.Hash01(i, 8) * 10f), new Vector3(0.62f, 0.55f, 0.5f), true, "Sack");
                    MeshKit.Spawn(MeshKit.Cylinder, rope, s, new Vector3(0, 0.48f, 0), Quaternion.identity, new Vector3(0.25f, 0.12f, 0.25f), false, "Tie");
                }
            }
            if (units > 0)
            {
                // A basket of produce on top.
                var b = PropKit.Build(PropKind.Basket, _foodStack);
                b.transform.localPosition = new Vector3(1.9f, 0.36f, 0.2f);
                b.transform.localScale = Vector3.one * 1.6f;
            }
        }

        private void RebuildWater(int water)
        {
            ClearChildren(_waterStack);
            int barrels = Mathf.Clamp(Mathf.CeilToInt(water / 5f), 0, 10);
            var wood = MaterialLibrary.ForKey("Wood");
            var metal = MaterialLibrary.ForKey("Metal");
            var waterMat = MaterialLibrary.ForKey("Water");
            for (int i = 0; i < barrels; i++)
            {
                int layer = i < 6 ? 0 : 1;
                int slot = layer == 0 ? i : i - 6;
                float x = layer == 0 ? (slot % 3 - 1f) * 0.68f : (slot % 2 - 0.5f) * 0.68f;
                float z = layer == 0 ? (slot / 3 - 0.5f) * 0.68f : (slot / 2 - 0.5f) * 0.68f;
                var root = new GameObject("Barrel").transform;
                root.SetParent(_waterStack, false);
                root.localPosition = new Vector3(x, layer * 0.82f, z);
                MeshKit.Spawn(MeshKit.Cylinder, new[] { wood, waterMat }, root, new Vector3(0, 0.4f, 0), Quaternion.identity, new Vector3(0.6f, 0.8f, 0.6f), true, "Staves");
                MeshKit.Spawn(MeshKit.Cylinder, metal, root, new Vector3(0, 0.18f, 0), Quaternion.identity, new Vector3(0.62f, 0.05f, 0.62f), false, "Hoop");
                MeshKit.Spawn(MeshKit.Cylinder, metal, root, new Vector3(0, 0.62f, 0), Quaternion.identity, new Vector3(0.62f, 0.05f, 0.62f), false, "Hoop");
            }
        }

        public void Resnap(Rect r)
        {
            if (_root == null || _w.Camp == null) return;
            if (!r.Overlaps(Rect.MinMaxRect(_w.Camp.X - 12, _w.Camp.Z - 12, _w.Camp.X + 12, _w.Camp.Z + 12))) return;
            Clear();
            Sync();
        }
    }
}
