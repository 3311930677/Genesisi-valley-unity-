using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Selectable = GenesisValley.Presentation.Selectable;

namespace GenesisValley.UI
{
    /// <summary>Bottom-left "valley story" ticker: recent story/milestone/warning events; click to look.</summary>
    public sealed class StoryFeed : MonoBehaviour
    {
        private sealed class Entry
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public float Born;
            public bool Dying;
        }

        private const int MaxEntries = 5;
        private const float Lifetime = 18f;
        private RectTransform _list;
        private readonly List<Entry> _entries = new List<Entry>();
        private Text _header;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            var area = UIFactory.Rect(rt, "Area");
            area.anchorMin = area.anchorMax = area.pivot = new Vector2(0, 0);
            area.anchoredPosition = new Vector2(18, 18);
            area.sizeDelta = new Vector2(440, 330);

            var head = UIFactory.Rect(area, "Head");
            head.anchorMin = new Vector2(0, 0); head.anchorMax = new Vector2(1, 0); head.pivot = new Vector2(0, 0);
            head.sizeDelta = new Vector2(0, 34); head.anchoredPosition = Vector2.zero;
            var btn = UIFactory.Button(head, "山谷编年史", () => GameUI.Instance.Chronicle.Open(), 170, 32, "Scroll", 15, 8);
            UIFactory.Place((RectTransform)btn.transform, new Vector2(0, 0), new Vector2(170, 32), Vector2.zero);
            btn.Normal = new Color(0.1f, 0.085f, 0.07f, 0.72f); btn.Refresh();
            btn.Tooltip = "查看山谷发生过的所有事（L）";
            _header = btn.Label;

            _list = UIFactory.Rect(area, "List");
            _list.anchorMin = new Vector2(0, 0); _list.anchorMax = new Vector2(1, 0); _list.pivot = new Vector2(0, 0);
            _list.anchoredPosition = new Vector2(0, 42);
            var v = UIFactory.VLayout(_list, 6);
            v.childAlignment = TextAnchor.LowerLeft;
            UIFactory.Fit(_list, false, true);
        }


        public void ResetFor(WorldState w)
        {
            foreach (var e in _entries) if (e.Rt != null) Destroy(e.Rt.gameObject);
            _entries.Clear();
        }

        public void OnEvent(SimEvent e)
        {
            if (e.Importance < EventImportance.Story || string.IsNullOrEmpty(e.Message)) return;
            if (e.Type == SimEventType.Hint || e.Type == SimEventType.CommandRejected || e.Type == SimEventType.UndoRejected || e.Type == SimEventType.Saved) return;
            Add(e);
        }

        private void Add(SimEvent e)
        {
            var w = GameServices.World;
            var color = HudUtil.EventColor(e);
            var row = UIFactory.Button(_list, null, () => Look(e), 430, 40, null, UIStyle.FontSmall, 10);
            row.name = "Entry";
            row.HoverScale = 1.01f;
            row.Normal = e.Importance == EventImportance.Milestone ? new Color(0.32f, 0.23f, 0.11f, 0.86f) : e.Importance == EventImportance.Warning ? new Color(0.3f, 0.16f, 0.1f, 0.86f) : new Color(0.1f, 0.085f, 0.07f, 0.8f);
            row.Hover = row.Normal + new Color(0.06f, 0.05f, 0.04f, 0.1f);
            row.ClickSound = "ui_click";
            var rt = (RectTransform)row.transform;
            var h = UIFactory.HLayout(rt, 10, 12, 12, 8, 8);
            var ic = UIFactory.Image(rt, "Icon", color, UIStyle.Icon(HudUtil.EventIcon(e.Type)));
            ic.preserveAspect = true;
            UIFactory.Layout(ic, 20, 20);
            var t = UIFactory.Label(rt, e.Message, 17, UIStyle.Text, TextAnchor.MiddleLeft);
            UIFactory.Layout(t, 300, -1, 1);
            var time = UIFactory.Label(rt, w != null ? w.ClockText : "", 13, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Layout(time, 44, 20);
            var le = rt.GetComponent<LayoutElement>();
            le.preferredHeight = -1; le.minHeight = 40;
            row.Tooltip = "点击查看位置";
            var g = UIFactory.Group(row);
            g.alpha = 0f;
            UITween.Fade(g, 1f, 0.35f);
            UITween.Scale(rt, new Vector3(0.94f, 0.94f, 1f), Vector3.one, 0.3f, UITween.OutBack);
            _entries.Add(new Entry { Rt = rt, Group = g, Born = Time.unscaledTime });
            while (_entries.Count > MaxEntries) Kill(_entries[0], true);
        }

        private void Kill(Entry e, bool immediate)
        {
            _entries.Remove(e);
            if (e.Rt == null) return;
            if (immediate) { Destroy(e.Rt.gameObject); return; }
            e.Dying = true;
            var go = e.Rt.gameObject;
            UITween.Fade(e.Group, 0f, 0.6f, () => { if (go != null) Destroy(go); });
        }

        private static void Look(SimEvent e)
        {
            var rig = GameServices.CameraRig;
            var p = GameServices.Presenter;
            if (rig == null || p == null) return;
            if (e.X == 0 && e.Z == 0) return;
            rig.FlyTo(p.Ground(e.X, e.Z), Mathf.Min(rig.Distance, 60f), 1.0f);
            var sel = GameServices.Selection;
            var w = GameServices.World;
            if (sel != null && w != null && w.FindVillager(e.Actor) != null) sel.Select(new Selectable(SelectionKind.Villager, e.Actor));
        }

        private void Update()
        {
            // pause expiry while the mouse is over any entry
            bool over = false;
            foreach (var e in _entries)
            {
                var b = e.Rt != null ? e.Rt.GetComponent<UIButton>() : null;
                if (b != null && b.IsHovered) { over = true; break; }
            }
            if (over) { foreach (var e in _entries) e.Born += Time.unscaledDeltaTime; return; }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (!e.Dying && Time.unscaledTime - e.Born > Lifetime) Kill(e, false);
            }
        }
    }

    /// <summary>山谷编年史: the full story log grouped by day, with filters and a stats summary.</summary>
    public sealed class ChronicleWindow : UIWindow
    {
        private enum Filter { All, Milestones, Warnings, Building, Nature }
        private RectTransform _list;
        private ScrollRect _scroll;
        private Text _stats;
        private Filter _filter;
        private readonly List<UIButton> _tabs = new List<UIButton>();

        public override bool BlocksHotkeys => true;

        protected override void Build()
        {
            var body = BuildFrame("山谷编年史", 960, 780);
            _stats = UIFactory.Label(body, "", UIStyle.FontSmall, UIStyle.TextDim, TextAnchor.UpperLeft);
            var srt = _stats.rectTransform;
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(0.5f, 1);
            srt.offsetMin = new Vector2(0, -56); srt.offsetMax = new Vector2(0, 0);

            var tabs = UIFactory.Rect(body, "Tabs");
            tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(0.5f, 1);
            tabs.offsetMin = new Vector2(0, -106); tabs.offsetMax = new Vector2(0, -62);
            UIFactory.HLayout(tabs, 8);
            string[] names = { "全部", "里程碑", "提醒", "建设", "自然" };
            for (int i = 0; i < names.Length; i++)
            {
                var f = (Filter)i;
                var b = UIFactory.Button(tabs, names[i], () => SetFilter(f), 120, 40, null, UIStyle.FontSmall);
                _tabs.Add(b);
            }

            _list = UIFactory.ScrollList(body, "List", out _scroll, 4);
            var lrt = (RectTransform)_scroll.transform;
            UIFactory.Stretch(lrt, 0, 0, 118, 0);
        }

        protected override void OnOpen() => Populate();

        private void SetFilter(Filter f)
        {
            _filter = f;
            Populate();
        }

        private bool Pass(SimEvent e)
        {
            switch (_filter)
            {
                case Filter.Milestones: return e.Importance == EventImportance.Milestone;
                case Filter.Warnings: return e.Importance == EventImportance.Warning;
                case Filter.Building:
                    return e.Type == SimEventType.SitePlanned || e.Type == SimEventType.SiteStageStarted || e.Type == SimEventType.BuildingCompleted ||
                           e.Type == SimEventType.BridgePlanned || e.Type == SimEventType.RegionConnected || e.Type == SimEventType.VillagerMovedIn || e.Type == SimEventType.SiteWaiting;
                case Filter.Nature:
                    return e.Type == SimEventType.RiverCreated || e.Type == SimEventType.ForestPlanted || e.Type == SimEventType.SaplingMatured ||
                           e.Type == SimEventType.FieldHarvested || e.Type == SimEventType.FieldSown || e.Type == SimEventType.FishCaught || e.Type == SimEventType.TreeFelled;
            }
            return true;
        }

        private void Populate()
        {
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].Selected = i == (int)_filter;
            UIFactory.Clear(_list);
            var w = GameServices.World;
            if (w == null) return;
            var st = w.Stats;
            int reached = 0, total = 0;
            foreach (Milestones m in System.Enum.GetValues(typeof(Milestones))) { if (m == Milestones.None) continue; total++; if ((st.Reached & m) != 0) reached++; }
            _stats.text = w.WorldName + " · 第 " + w.Day + " 天 · 人口 " + w.Population + " · 建成 " + st.BuildingsCompleted + " 座建筑 · 里程碑 " + reached + "/" + total + "\n" +
                          "砍伐 " + st.TreesFelled + " 棵树 · 运回木材 " + st.WoodDelivered + " · 收获 " + st.FoodHarvested + " 份粮食 · 捕鱼 " + st.FishCaught + " · 打水 " + st.WaterFetched + " · 迁入 " + st.Arrivals + " 人";

            var story = w.Events.Story;
            int lastDay = -1, count = 0;
            for (int i = story.Count - 1; i >= 0; i--)
            {
                var e = story[i];
                if (!Pass(e) || string.IsNullOrEmpty(e.Message)) continue;
                int day = HudUtil.DayOf(w, e.Tick);
                if (day != lastDay)
                {
                    lastDay = day;
                    var head = UIFactory.Label(_list, "第 " + day + " 天", UIStyle.FontLarge, UIStyle.Accent, TextAnchor.LowerLeft, false, UIStyle.TitleFont);
                    UIFactory.Layout(head, -1, 44);
                }
                var ev = e;
                var row = UIFactory.Button(_list, null, () => { Close(); LookAt(ev); }, 860, 40, null, UIStyle.FontSmall, 8);
                row.Normal = new Color(1, 1, 1, i % 2 == 0 ? 0.035f : 0.015f);
                row.Hover = new Color(1, 0.9f, 0.75f, 0.09f);
                row.HoverScale = 1f;
                row.Refresh();
                var rt = (RectTransform)row.transform;
                UIFactory.HLayout(rt, 12, 12, 12, 6, 6);
                var le = rt.GetComponent<LayoutElement>(); le.preferredHeight = -1; le.minHeight = 40;
                var time = UIFactory.Label(rt, HudUtil.ClockOf(w, e.Tick), 15, UIStyle.TextFaint);
                UIFactory.Layout(time, 54, 26);
                var ic = UIFactory.Image(rt, "Icon", HudUtil.EventColor(e), UIStyle.Icon(HudUtil.EventIcon(e.Type)));
                ic.preserveAspect = true;
                UIFactory.Layout(ic, 20, 20);
                var t = UIFactory.Label(rt, e.Message, UIStyle.FontBody, e.Importance >= EventImportance.Milestone ? HudUtil.EventColor(e) : UIStyle.Text);
                UIFactory.Layout(t, 700, -1, 1);
                count++;
            }
            if (count == 0)
            {
                var empty = UIFactory.Label(_list, "这里还没有记录。山谷的故事才刚刚开始。", UIStyle.FontBody, UIStyle.TextFaint, TextAnchor.MiddleCenter);
                UIFactory.Layout(empty, -1, 120);
            }
            _scroll.verticalNormalizedPosition = 1f;
        }

        private static void LookAt(SimEvent e)
        {
            var rig = GameServices.CameraRig;
            var p = GameServices.Presenter;
            if (rig == null || p == null || (e.X == 0 && e.Z == 0)) return;
            rig.FlyTo(p.Ground(e.X, e.Z), Mathf.Min(rig.Distance, 60f), 1.1f);
        }
    }
}
