using System;
using System.Collections.Generic;
using System.Text;
using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.UI;
using Selectable = GenesisValley.Presentation.Selectable;

namespace GenesisValley.UI
{
    /// <summary>
    /// Right-hand detail card for the selected villager / building / tree / camp / pile. Everything shown is
    /// read straight from the simulation (GDD §6: facts, never invented drama).
    /// </summary>
    public sealed class Inspector : MonoBehaviour
    {
        private RectTransform _card, _body;
        private CanvasGroup _group;
        private Image _icon;
        private Text _name, _sub;
        private UIButton _follow, _locate;
        private readonly List<Action> _updaters = new List<Action>();
        private Selectable _shown = Selectable.None;
        private SelectionController _sel;
        private float _refresh;
        private bool _visible;

        private static WorldState W => GameServices.World;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _card = UIFactory.Panel(rt, "Card", UIStyle.Panel, 16);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(1, 1);
            _card.anchoredPosition = new Vector2(-18, -100);
            _card.sizeDelta = new Vector2(390, 200);
            _group = UIFactory.Group(_card);
            UIFactory.VLayout(_card, 8, 20, 20, 16, 18);
            UIFactory.Fit(_card, false, true);

            var head = UIFactory.Rect(_card, "Head");
            UIFactory.Layout(head, -1, 56);
            var hl = UIFactory.HLayout(head, 12);
            var iconBg = UIFactory.Image(head, "IconBg", new Color(1, 1, 1, 0.07f), UIStyle.Circle(26));
            UIFactory.Layout(iconBg, 52, 52);
            _icon = UIFactory.Image(iconBg.transform, "Icon", UIStyle.Accent, null);
            _icon.preserveAspect = true;
            UIFactory.Place(_icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(30, 30), Vector2.zero);
            var titles = UIFactory.Rect(head, "Titles");
            UIFactory.VLayout(titles, -2);
            UIFactory.Layout(titles, 230, 54, 1);
            _name = UIFactory.Label(titles, "", 26, UIStyle.Text, TextAnchor.LowerLeft, true, UIStyle.TitleFont);
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_name, -1, 32);
            _sub = UIFactory.Label(titles, "", 15, UIStyle.TextFaint, TextAnchor.UpperLeft);
            _sub.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_sub, -1, 20);
            var close = UIFactory.IconButton(head, "Close", "✕", () => _sel?.Clear(), 34, "关闭（Esc）");
            close.Normal = new Color(1, 1, 1, 0.03f); close.Refresh();

            var actions = UIFactory.Rect(_card, "Actions");
            UIFactory.Layout(actions, -1, 38);
            UIFactory.HLayout(actions, 8);
            _follow = UIFactory.Button(actions, "跟随", ToggleFollow, 120, 36, "Follow", UIStyle.FontSmall);
            _follow.Tooltip = "镜头跟随这位村民（移动镜头即可解除）";
            _locate = UIFactory.Button(actions, "定位", Locate, 120, 36, "Target", UIStyle.FontSmall);
            _locate.Tooltip = "把镜头移到这里";
            UIFactory.Divider(_card, -1, 0.2f);

            _body = UIFactory.Rect(_card, "Body");
            UIFactory.VLayout(_body, 6);
            _card.gameObject.SetActive(false);
        }

        private void Bind()
        {
            if (_sel != null) _sel.SelectionChanged -= OnSelection;
            _sel = GameServices.Selection;
            if (_sel != null) { _sel.SelectionChanged += OnSelection; OnSelection(_sel.Selected); }
        }

        private void OnDestroy() { if (_sel != null) _sel.SelectionChanged -= OnSelection; }

        public bool HandleEscape() => false;

        private void OnSelection(Selectable s)
        {
            if (s.IsNone) { Hide(); return; }
            _shown = s;
            Rebuild();
            if (!_visible)
            {
                _visible = true;
                _card.gameObject.SetActive(true);
                _group.alpha = 0f;
                _card.anchoredPosition = new Vector2(10, -100);
                UITween.Fade(_group, 1f, 0.16f);
                UITween.Move(_card, new Vector2(-18, -100), 0.24f, UITween.OutCubic);
            }
            else UITween.Scale(_card, Vector3.one * 0.985f, Vector3.one, 0.15f, UITween.OutCubic);
        }

        public void Hide(bool immediate = false)
        {
            _shown = Selectable.None;
            if (!_visible) return;
            _visible = false;
            if (immediate) { _card.gameObject.SetActive(false); return; }
            UITween.Fade(_group, 0f, 0.12f, () => { if (!_visible) _card.gameObject.SetActive(false); });
            UITween.Move(_card, new Vector2(10, -100), 0.14f, UITween.OutCubic);
        }

        private void ToggleFollow()
        {
            if (_sel == null || _shown.IsNone) return;
            if (_sel.IsFollowing) _sel.StopFollow(); else _sel.Follow(_shown);
        }

        private void Locate()
        {
            if (_sel == null || _shown.IsNone) return;
            var a = _sel.Anchor(_shown);
            if (a.HasValue) GameServices.CameraRig?.FlyTo(a.Value, Mathf.Min(GameServices.CameraRig.Distance, 55f), 0.9f);
        }

        // ================================================================== building blocks

        private Text Line(string text = "", int size = UIStyle.FontSmall, Color? color = null)
        {
            var t = UIFactory.Label(_body, text, size, color ?? UIStyle.TextDim, TextAnchor.UpperLeft);
            UIFactory.Layout(t, 350, -1);
            return t;
        }

        private Text Status()
        {
            var t = UIFactory.Label(_body, "", UIStyle.FontBody, UIStyle.Accent, TextAnchor.MiddleLeft, true);
            UIFactory.Layout(t, 350, 30);
            return t;
        }

        private UIBar BarRow(string label, string icon, out Text valueText)
        {
            var row = UIFactory.Rect(_body, "Bar_" + label);
            UIFactory.Layout(row, -1, 26);
            UIFactory.HLayout(row, 8);
            var ic = UIFactory.Image(row, "Icon", UIStyle.TextDim, UIStyle.Icon(icon));
            ic.preserveAspect = true;
            UIFactory.Layout(ic, 18, 18);
            var l = UIFactory.Label(row, label, UIStyle.FontSmall, UIStyle.TextDim);
            UIFactory.Layout(l, 64, 24);
            var bar = UIFactory.Bar(row, "Bar", 180, 10, UIStyle.Good);
            valueText = UIFactory.Label(row, "", 15, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Layout(valueText, 60, 24);
            return bar;
        }

        private void Header(string text)
        {
            UIFactory.Spacer(_body, 2);
            var t = UIFactory.Label(_body, text, 15, UIStyle.Accent, TextAnchor.LowerLeft);
            UIFactory.Layout(t, -1, 22);
        }

        private void Rebuild()
        {
            UIFactory.Clear(_body);
            _updaters.Clear();
            var w = W;
            if (w == null) return;
            _follow.gameObject.SetActive(_shown.Kind == SelectionKind.Villager);
            switch (_shown.Kind)
            {
                case SelectionKind.Villager: BuildVillager(w); break;
                case SelectionKind.Building: BuildBuilding(w); break;
                case SelectionKind.Tree: BuildTree(w); break;
                case SelectionKind.Camp: BuildStorage(w, true); break;
                case SelectionKind.Pile: BuildPile(w); break;
            }
            Refresh();
        }

        // ------------------------------------------------------------------ villager

        private static string Trait(float v, string high, string low) => v > 0.68f ? high : v < 0.32f ? low : null;

        private void BuildVillager(WorldState w)
        {
            var id = _shown.Id;
            _icon.sprite = UIStyle.Icon("Population");
            var status = Status();
            var bars = new[]
            {
                BarRow("饱腹", "Food", out var t0), BarRow("水分", "Water", out var t1),
                BarRow("精力", "Sleep", out var t2), BarRow("健康", "Heart", out var t3),
            };
            var texts = new[] { t0, t1, t2, t3 };
            Header("关于");
            var info = Line();
            Header("最近");
            var recent = Line("", UIStyle.FontSmall, UIStyle.TextDim);
            recent.lineSpacing = 1.2f;
            _updaters.Add(() =>
            {
                var v = w.FindVillager(id);
                if (v == null) { Gone(); return; }
                _name.text = v.Name;
                string mood = HudUtil.Mood(v, out var mc);
                _sub.text = (v.Gender == 1 ? "女" : "男") + " · 偏好" + HudUtil.RoleName(v.Role) + " · " + UIStyle.Colorize(mood, mc);
                _sub.supportRichText = true;
                status.text = HudUtil.Doing(w, v);
                float food = Mathf.Clamp01(1f - v.Hunger / 2f), water = Mathf.Clamp01(1f - v.Thirst / 2f);
                float[] vals = { food, water, Mathf.Clamp01(v.Energy), Mathf.Clamp01(v.Health) };
                for (int i = 0; i < 4; i++)
                {
                    float x = vals[i];
                    bars[i].Set(x, x < 0.25f ? UIStyle.Bad : x < 0.5f ? UIStyle.Warn : i == 1 ? UIStyle.Water : UIStyle.Good);
                    texts[i].text = Mathf.RoundToInt(x * 100) + "%";
                }
                var sb = new StringBuilder();
                var home = w.FindBuilding(v.HomeId);
                sb.Append("住所：").Append(home != null ? WorldState.KindName(home.Kind) + "（与 " + Mathf.Max(0, home.Residents.Count - 1) + " 人同住）" : UIStyle.Colorize("还没有家，夜里睡在营地", UIStyle.Warn)).Append('\n');
                sb.Append("携带：");
                if (v.Carry == null || v.Carry.IsEmpty) sb.Append("空手");
                else
                {
                    bool first = true;
                    for (int i = 0; i < Inventory.ItemTypeCount; i++)
                    {
                        int n = v.Carry.Get((ItemType)i); if (n <= 0) continue;
                        if (!first) sb.Append("、"); first = false;
                        sb.Append(HudUtil.ItemName((ItemType)i)).Append(" ×").Append(n);
                    }
                }
                sb.Append('\n');
                float workedHours = v.WorkedSecondsToday / w.Config.SecondsPerGameDay * 24f;
                sb.Append("今日劳作：").Append(workedHours.ToString("0.0")).Append(" 小时\n");
                sb.Append("来到山谷：").Append(HudUtil.When(w, v.ArrivedTick));
                var traits = new List<string>();
                var a = Trait(v.Diligence, "勤快", "慢性子"); if (a != null) traits.Add(a);
                var b = Trait(v.Curiosity, "好奇", "恋家"); if (b != null) traits.Add(b);
                var c = Trait(v.Sociability, "爱热闹", "安静"); if (c != null) traits.Add(c);
                if (traits.Count > 0) sb.Append("\n性格：").Append(string.Join("、", traits));
                info.text = sb.ToString();
                sb.Length = 0;
                int shown = 0;
                for (int i = v.Recent.Count - 1; i >= 0 && shown < 5; i--, shown++)
                {
                    var e = v.Recent[i];
                    if (shown > 0) sb.Append('\n');
                    sb.Append(UIStyle.Colorize(HudUtil.Ago(w, e.Tick), UIStyle.TextFaint)).Append("  ").Append(e.Text);
                }
                recent.text = shown == 0 ? UIStyle.Colorize("刚到这里，还没有什么故事。", UIStyle.TextFaint) : sb.ToString();
                _follow.Selected = _sel != null && _sel.IsFollowing;
                _follow.Label.text = _follow.Selected ? "停止跟随" : "跟随";
            });
        }

        // ------------------------------------------------------------------ buildings

        private void BuildBuilding(WorldState w)
        {
            var id = _shown.Id;
            var b0 = w.FindBuilding(id);
            if (b0 == null) { Gone(); return; }
            _icon.sprite = UIStyle.Icon(HudUtil.KindIcon(b0.Kind));
            var status = Status();
            var progress = BarRow("进度", "Hammer", out var progressText);
            var wood = BarRow("木材", "Wood", out var woodText);
            var waiting = Line("", UIStyle.FontSmall, UIStyle.Warn);
            Header("详情");
            var details = Line();
            UIBar crop = null; Text cropText = null;
            if (b0.Kind == BuildingKind.Field) crop = BarRow("长势", "Food", out cropText);
            _updaters.Add(() =>
            {
                var b = w.FindBuilding(id);
                if (b == null) { Gone(); return; }
                _name.text = WorldState.KindName(b.Kind);
                bool site = !b.IsComplete;
                _sub.text = site ? "工地 · 建于" + HudUtil.When(w, b.CreatedTick) : "建成于" + HudUtil.When(w, b.CompletedTick);
                progress.Root.parent.gameObject.SetActive(site);
                wood.Root.parent.gameObject.SetActive(site);
                if (site)
                {
                    status.text = "正在" + ConstructionSystem.StageName(b.Kind, b.Stage);
                    progress.Set(b.Progress, UIStyle.Accent);
                    progressText.text = Mathf.RoundToInt(b.Progress * 100) + "%";
                    int have = b.WoodConsumed + (b.Site != null ? b.Site.Get(ItemType.Wood) : 0);
                    wood.Set(b.WoodRequired > 0 ? (float)have / b.WoodRequired : 1f, b.WoodShortfall > 0 ? UIStyle.Warn : UIStyle.Good);
                    woodText.text = have + "/" + b.WoodRequired;
                }
                else status.text = CompleteStatus(w, b);
                string reason = site ? b.WaitingReason : null;
                waiting.gameObject.SetActive(!string.IsNullOrEmpty(reason));
                if (!string.IsNullOrEmpty(reason)) waiting.text = "⚠ " + reason;

                var sb = new StringBuilder();
                switch (b.Kind)
                {
                    case BuildingKind.House:
                        sb.Append("床位：").Append(b.Beds).Append("  ·  住户：").Append(b.Residents.Count).Append('\n');
                        if (b.Residents.Count > 0)
                        {
                            sb.Append("住在这里：");
                            for (int i = 0; i < b.Residents.Count; i++) { if (i > 0) sb.Append("、"); sb.Append(w.NameOf(b.Residents[i])); }
                            sb.Append('\n');
                        }
                        break;
                    case BuildingKind.Storehouse:
                        var st = w.FindStorage(b.StorageId);
                        if (st != null) AppendInventory(sb, st.Inventory);
                        break;
                    case BuildingKind.Field:
                        sb.Append("已收获 ").Append(b.Harvests).Append(" 次 · 每季约 ").Append(w.Config.FieldHarvestFood).Append(" 份食物\n");
                        break;
                    case BuildingKind.FishingHut:
                        sb.Append("渔夫会在附近的水边垂钓，鱼获送回仓储。\n");
                        break;
                    case BuildingKind.Bridge:
                        sb.Append("跨度 ").Append(BuildingPresenter.BridgeLength(b).ToString("0")).Append(" 米 · 连通两岸的道路\n");
                        break;
                }
                int workers = 0; var names = new List<string>();
                foreach (var v in w.Villagers) if (v.TargetId.Equals(b.Id)) { workers++; if (names.Count < 4) names.Add(v.Name); }
                if (workers > 0) sb.Append(site ? "正在施工：" : "正在这里：").Append(string.Join("、", names)).Append(workers > names.Count ? " 等 " + workers + " 人" : "").Append('\n');
                else if (site) sb.Append(UIStyle.Colorize("暂时没有人在施工", UIStyle.TextFaint)).Append('\n');
                details.text = sb.ToString().TrimEnd('\n');
                details.gameObject.SetActive(details.text.Length > 0);
                if (crop != null)
                {
                    crop.Root.parent.gameObject.SetActive(b.IsComplete);
                    float g = b.Crop == CropState.Ripe ? 1f : b.Crop == CropState.Growing ? Mathf.Clamp01(b.CropGrowth) : 0f;
                    crop.Set(g, b.Crop == CropState.Ripe ? UIStyle.Accent : UIStyle.Good);
                    cropText.text = b.Crop == CropState.Ripe ? "成熟" : b.Crop == CropState.Growing ? Mathf.RoundToInt(g * 100) + "%" : "休耕";
                }
            });
        }

        private static string CompleteStatus(WorldState w, BuildingEntity b)
        {
            switch (b.Kind)
            {
                case BuildingKind.House: return b.Residents.Count >= b.Beds ? "住满了" : b.Residents.Count == 0 ? "空置，等待住户" : "还有 " + (b.Beds - b.Residents.Count) + " 张空床";
                case BuildingKind.Field: return b.Crop == CropState.Ripe ? "庄稼成熟，等待收割" : b.Crop == CropState.Growing ? "庄稼正在生长" : "等待播种";
                case BuildingKind.Storehouse: return "存放食物、木材与清水";
                case BuildingKind.FishingHut: return "渔屋";
                case BuildingKind.Bridge: return "可以通行";
            }
            return "已建成";
        }

        private static void AppendInventory(StringBuilder sb, Inventory inv)
        {
            sb.Append("库存：");
            for (int i = 0; i < Inventory.ItemTypeCount; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(HudUtil.ItemName((ItemType)i)).Append(' ').Append(inv.Get((ItemType)i));
            }
            sb.Append("\n容量：").Append(inv.Total).Append(" / ").Append(inv.Capacity).Append('\n');
        }

        // ------------------------------------------------------------------ nature & storage

        private void BuildTree(WorldState w)
        {
            var id = _shown.Id;
            _icon.sprite = UIStyle.Icon("Tree");
            var status = Status();
            var growth = BarRow("生长", "Forest", out var growthText);
            var details = Line();
            _updaters.Add(() =>
            {
                var t = w.FindTree(id);
                if (t == null) { Gone(); return; }
                _name.text = t.IsStump ? "树桩" : t.IsSapling ? "树苗" : HudUtil.SpeciesName(t.Species);
                _sub.text = HudUtil.SpeciesName(t.Species) + (t.PlantedByTransaction != 0 ? " · 由你种下" : " · 野生");
                growth.Root.parent.gameObject.SetActive(t.IsSapling);
                if (t.IsSapling)
                {
                    growth.Set(t.Growth, UIStyle.Good);
                    growthText.text = Mathf.RoundToInt(t.Growth * 100) + "%";
                    status.text = "正在生长，长成后才能砍伐";
                }
                else if (t.IsStump) status.text = "已被砍伐，会慢慢腐朽";
                else status.text = t.ReservedBy.IsValid ? w.NameOf(t.ReservedBy) + " 正准备砍伐它" : "可以砍伐";
                int woodLeft = t.Stock != null ? t.Stock.Get(ItemType.Wood) : 0;
                details.text = t.IsStump ? "树桩会在几天后消失，周围可能长出新的树苗。" : "可产木材：" + woodLeft;
            });
        }

        private void BuildStorage(WorldState w, bool camp)
        {
            var id = _shown.Id;
            _icon.sprite = UIStyle.Icon(camp ? "Storage" : "Storage");
            var status = Status();
            var food = BarRow("食物", "Food", out var tf);
            var wood = BarRow("木材", "Wood", out var tw);
            var water = BarRow("清水", "Water", out var tv);
            var details = Line();
            _updaters.Add(() =>
            {
                var s = w.FindStorage(id) ?? w.Camp;
                if (s == null) { Gone(); return; }
                _name.text = s.Kind == StorageKind.Camp ? "营地" : "仓库";
                _sub.text = "仓储 " + s.Inventory.Total + " / " + s.Inventory.Capacity;
                int sitting = 0;
                foreach (var v in w.Villagers) if (v.Action == VillagerAction.Sit || v.Action == VillagerAction.Eat || v.Action == VillagerAction.Drink) sitting++;
                status.text = s.Kind == StorageKind.Camp ? (sitting > 0 ? sitting + " 人在篝火边" : "篝火静静燃烧") : "存放物资";
                float cap = Mathf.Max(1, s.Inventory.Capacity);
                food.Set(s.Inventory.Get(ItemType.Food) / cap * 3f, UIStyle.Accent); tf.text = s.Inventory.Get(ItemType.Food).ToString();
                wood.Set(s.Inventory.Get(ItemType.Wood) / cap * 3f, new Color(0.8f, 0.6f, 0.4f)); tw.text = s.Inventory.Get(ItemType.Wood).ToString();
                water.Set(s.Inventory.Get(ItemType.Water) / cap * 3f, UIStyle.Water); tv.text = s.Inventory.Get(ItemType.Water).ToString();
                details.text = s.Kind == StorageKind.Camp
                    ? "村民在这里吃饭、喝水、存放物资；没有家的人夜里也睡在这里。\n食物可维持 " + HudUtil.Fmt(w.FoodDays) + " 天 · 清水可维持 " + HudUtil.Fmt(w.WaterDays) + " 天"
                    : "仓库扩大了整座村庄的储量。";
            });
        }

        private void BuildPile(WorldState w)
        {
            var id = _shown.Id;
            _icon.sprite = UIStyle.Icon("Wood");
            var status = Status();
            var details = Line();
            _updaters.Add(() =>
            {
                var p = w.FindPile(id);
                if (p == null) { Gone(); return; }
                _name.text = "物资堆";
                var sb = new StringBuilder();
                for (int i = 0; i < Inventory.ItemTypeCount; i++)
                {
                    int n = p.Inventory.Get((ItemType)i); if (n <= 0) continue;
                    if (sb.Length > 0) sb.Append("、");
                    sb.Append(HudUtil.ItemName((ItemType)i)).Append(" ×").Append(n);
                }
                _sub.text = sb.ToString();
                status.text = p.ReservedBy.IsValid ? w.NameOf(p.ReservedBy) + " 正来搬运" : "等待有人搬回仓储";
                details.text = "散落的物资会被村民陆续搬走。";
            });
        }

        private void Gone()
        {
            _sel?.Clear();
        }

        private void Update()
        {
            if (_sel != GameServices.Selection) Bind();
            if (!_visible) return;
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh > 0f) return;
            _refresh = 0.2f;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _updaters.Count; i++)
            {
                if (_shown.IsNone) return;
                _updaters[i]();
            }
        }
    }
}
