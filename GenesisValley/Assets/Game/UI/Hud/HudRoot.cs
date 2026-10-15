using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.Presentation;
using GenesisValley.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>In-game HUD container: top bar, speed controls, toolbar, inspector, story feed, goals, banners.</summary>
    public sealed class HudRoot : MonoBehaviour
    {
        public TopBar Top { get; private set; }
        public Toolbar Toolbar { get; private set; }
        public Inspector Inspector { get; private set; }
        public StoryFeed Feed { get; private set; }
        public Objectives Goals { get; private set; }
        public MilestoneBanner Banner { get; private set; }
        public CursorTip Tip { get; private set; }

        private CanvasGroup _group;
        private bool _shown;
        private WorldPresenter _presenter;
        private Text _paused;
        private CanvasGroup _pausedGroup;
        private bool _welcomed;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _group = UIFactory.Group(rt);
            _group.alpha = 0f;
            Goals = Part<Objectives>("Objectives");
            Feed = Part<StoryFeed>("StoryFeed");
            Toolbar = Part<Toolbar>("Toolbar");
            Inspector = Part<Inspector>("Inspector");
            Top = Part<TopBar>("TopBar");
            Banner = Part<MilestoneBanner>("Banner");
            Tip = Part<CursorTip>("CursorTip");

            _paused = UIFactory.Label(rt, "", UIStyle.FontLarge, UIStyle.Text, TextAnchor.MiddleCenter, true, UIStyle.TitleFont);
            UIFactory.Place(_paused.rectTransform, new Vector2(0.5f, 1), new Vector2(600, 40), new Vector2(0, -96));
            _pausedGroup = UIFactory.Group(_paused);
            _pausedGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        private T Part<T>(string name) where T : Component
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(transform, name));
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = true;
            return rt.gameObject.AddComponent<T>();
        }

        public void SetShown(bool show)
        {
            if (show == _shown) return;
            _shown = show;
            if (show)
            {
                gameObject.SetActive(true);
                _group.blocksRaycasts = true;
                UITween.Fade(_group, 1f, 0.45f);
            }
            else
            {
                _group.blocksRaycasts = false;
                UITween.Fade(_group, 0f, 0.25f, () => { if (!_shown) gameObject.SetActive(false); });
            }
        }

        public void OnWorldInstalled(WorldState w)
        {
            Feed.ResetFor(w);
            Goals.ResetFor(w);
            Banner.ResetQueue();
            Inspector.Hide(true);
        }

        public void OnEnterPlay()
        {
            BindPresenter();
            var g = GameServices.Game;
            if (!_welcomed && g != null && !g.IsTitleWorld)
            {
                _welcomed = true;
                var s = GameServices.Settings;
                if (s == null || s.ShowHints)
                    GameUI.Instance.Notify("按 F2 查看操作说明 · 数字键 1–8 切换神力", ToastKind.Info);
            }
        }

        private void BindPresenter()
        {
            var p = GameServices.Presenter;
            if (p == _presenter) return;
            if (_presenter != null) _presenter.EventRaised -= OnSimEvent;
            _presenter = p;
            if (_presenter != null) _presenter.EventRaised += OnSimEvent;
        }

        private void OnDestroy() { if (_presenter != null) _presenter.EventRaised -= OnSimEvent; }

        private void OnSimEvent(SimEvent e)
        {
            var g = GameServices.Game;
            if (g == null || g.IsTitleWorld) return;
            Feed.OnEvent(e);
            Goals.OnEvent(e);
            if (e.Type == SimEventType.Milestone) Banner.Show(e.Message);
            var ui = GameUI.Instance;
            switch (e.Type)
            {
                case SimEventType.FoodShortage:
                case SimEventType.WaterShortage:
                    ui.Notify(e.Message, ToastKind.Warning); break;
                case SimEventType.UndoRejected:
                    if (!string.IsNullOrEmpty(e.Message)) ui.Notify(e.Message, ToastKind.Error); break;
                case SimEventType.Hint:
                    var s = GameServices.Settings;
                    if (s == null || s.ShowHints) ui.Notify(e.Message, ToastKind.Info);
                    break;
            }
        }

        public void ToggleOverlay()
        {
            var p = GameServices.Presenter;
            if (p == null || p.Wetness == null) return;
            p.Wetness.Visible = !p.Wetness.Visible;
            GameServices.Audio?.PlayUI("ui_click", 0.5f);
            GameUI.Instance.Notify(p.Wetness.Visible ? "湿润度图层：蓝色越深越湿润，适合开垦农田" : "已关闭湿润度图层");
        }

        public bool OverlayVisible => GameServices.Presenter != null && GameServices.Presenter.Wetness != null && GameServices.Presenter.Wetness.Visible;

        /// <summary>Esc inside the HUD (collapse expanded panels). Returns true if consumed.</summary>
        public bool HandleEscape() => Inspector.HandleEscape();

        private void Update()
        {
            if (_presenter != GameServices.Presenter) BindPresenter();
            var g = GameServices.Game;
            bool userPaused = g != null && g.UserPaused && !g.MenuPaused;
            float target = userPaused ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f) : 0f;
            _pausedGroup.alpha = Mathf.MoveTowards(_pausedGroup.alpha, target, Time.unscaledDeltaTime * 4f);
            if (userPaused) _paused.text = "已暂停 · 按 " + InputBindings.KeyName(InputBindings.Get(GameAction.Pause)) + " 继续";
        }
    }

    /// <summary>Top-left world clock + resource chips, top-right speed and menu buttons.</summary>
    public sealed class TopBar : MonoBehaviour
    {
        private sealed class Chip
        {
            public RectTransform Rt;
            public Image Icon;
            public Text Value, Caption;
            public float Pulse;
            public string Last;
        }

        private Text _world, _clock;
        private Image _timeIcon;
        private Chip _pop, _food, _water, _wood, _storage;
        private UIButton _pause, _x1, _x2, _x4, _overlay;
        private float _refresh;
        private Sprite _sun, _moon;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            var bar = UIFactory.Panel(rt, "Bar", UIStyle.Panel, 14);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0, 1);
            bar.anchoredPosition = new Vector2(18, -16);
            var h = UIFactory.HLayout(bar, 6, 14, 18, 8, 8);
            h.childForceExpandHeight = false;
            var fit = UIFactory.Fit(bar, true, false);
            bar.sizeDelta = new Vector2(0, 66);

            _sun = UIStyle.Icon("Sun"); _moon = UIStyle.Icon("Moon");
            _timeIcon = UIFactory.Image(bar, "TimeIcon", UIStyle.Accent, _sun);
            _timeIcon.preserveAspect = true;
            UIFactory.Layout(_timeIcon, 34, 34);
            var clock = UIFactory.Rect(bar, "Clock");
            UIFactory.VLayout(clock, -2, 6, 6, 0, 0);
            UIFactory.Layout(clock, 196, 50);
            _world = UIFactory.Label(clock, "", 22, UIStyle.Text, TextAnchor.LowerLeft, true, UIStyle.TitleFont);
            _world.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_world, -1, 28);
            _clock = UIFactory.Label(clock, "", 16, UIStyle.TextDim, TextAnchor.UpperLeft);
            UIFactory.Layout(_clock, -1, 22);
            Separator(bar);
            _pop = MakeChip(bar, "Population", "人口 / 床位", PopTip);
            _food = MakeChip(bar, "Food", "食物", FoodTip);
            _water = MakeChip(bar, "Water", "清水", WaterTip);
            _wood = MakeChip(bar, "Wood", "木材", WoodTip);
            _storage = MakeChip(bar, "Storage", "仓储", StorageTip);

            // right side
            var right = UIFactory.Panel(rt, "Controls", UIStyle.Panel, 14);
            right.anchorMin = right.anchorMax = right.pivot = new Vector2(1, 1);
            right.anchoredPosition = new Vector2(-18, -16);
            UIFactory.HLayout(right, 6, 10, 10, 8, 8);
            UIFactory.Fit(right, true, false);
            right.sizeDelta = new Vector2(0, 66);
            string K(GameAction a) => InputBindings.KeyName(InputBindings.Get(a));
            _pause = UIFactory.IconButton(right, "Pause", "II", () => GameServices.Game?.TogglePause(), 50);
            _pause.TooltipProvider = () => (GameServices.Game != null && GameServices.Game.UserPaused ? "继续" : "暂停") + "（" + K(GameAction.Pause) + "）";
            _x1 = UIFactory.IconButton(right, "Play", "1×", () => SetSpeed(1), 50, null, "1×");
            _x1.TooltipProvider = () => "正常速度（" + K(GameAction.Speed1) + "）";
            _x2 = UIFactory.IconButton(right, "Fast", "2×", () => SetSpeed(2), 50, null, "2×");
            _x2.TooltipProvider = () => "2 倍速（" + K(GameAction.Speed2) + "）";
            _x4 = UIFactory.IconButton(right, "Faster", "4×", () => SetSpeed(4), 50, null, "4×");
            _x4.TooltipProvider = () => "4 倍速（" + K(GameAction.Speed3) + "）";
            Separator(right);
            var chron = UIFactory.IconButton(right, "Scroll", "史", () => GameUI.Instance.Chronicle.Open(), 50);
            chron.Tooltip = "山谷编年史（L）";
            _overlay = UIFactory.IconButton(right, "Water", "湿", () => GameUI.Instance.Hud.ToggleOverlay(), 50);
            _overlay.TooltipProvider = () => "湿润度图层（" + K(GameAction.Overlay) + "）\n显示哪里的土地适合耕种";
            var photo = UIFactory.IconButton(right, "Camera", "拍", () => GameUI.Instance.Photo.Enter(), 50);
            photo.TooltipProvider = () => "拍照模式（" + K(GameAction.PhotoMode) + "）";
            var help = UIFactory.IconButton(right, "Info", "?", () => GameUI.Instance.Help.Open(), 50);
            help.Tooltip = "操作说明（F2）";
            var menu = UIFactory.IconButton(right, "Menu", "≡", () => GameUI.Instance.Pause.Open(), 50);
            menu.Tooltip = "菜单（Esc）";
        }

        private static void SetSpeed(float s)
        {
            var g = GameServices.Game;
            if (g == null) return;
            if (g.UserPaused) g.SetPaused(false);
            g.SetSpeed(s);
        }

        private static void Separator(Transform parent)
        {
            var s = UIFactory.Image(parent, "Sep", new Color(1, 0.92f, 0.8f, 0.14f));
            UIFactory.Layout(s, 1.5f, 40);
            UIFactory.Spacer(parent, 1, 4);
        }

        private Chip MakeChip(Transform parent, string icon, string caption, System.Func<string> tip)
        {
            var rt = UIFactory.Rect(parent, "Chip_" + icon);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.001f);
            hit.raycastTarget = true;
            UIFactory.HLayout(rt, 8, 8, 8, 0, 0);
            UIFactory.Layout(rt, 128, 50);
            var ic = UIFactory.Image(rt, "Icon", UIStyle.Text, UIStyle.Icon(icon));
            ic.preserveAspect = true;
            UIFactory.Layout(ic, 28, 28);
            var col = UIFactory.Rect(rt, "Col");
            UIFactory.VLayout(col, -4);
            UIFactory.Layout(col, 84, 46);
            var v = UIFactory.Label(col, "—", 21, UIStyle.Text, TextAnchor.LowerLeft, true);
            v.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(v, -1, 26);
            var c = UIFactory.Label(col, caption, 13, UIStyle.TextFaint, TextAnchor.UpperLeft);
            c.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(c, -1, 18);
            var tt = rt.gameObject.AddComponent<TooltipTrigger>();
            tt.Provider = tip;
            return new Chip { Rt = rt, Icon = ic, Value = v, Caption = c };
        }

        private static WorldState W => GameServices.World;

        private string PopTip()
        {
            var w = W; if (w == null) return null;
            int homeless = 0; foreach (var v in w.Villagers) if (!v.HomeId.IsValid) homeless++;
            return "<b>人口 " + w.Population + "</b> / 上限 " + w.Config.PopulationCap + "\n床位 " + w.Beds + (homeless > 0 ? UIStyle.Colorize("\n" + homeless + " 人还没有自己的家", UIStyle.Warn) : "\n每个人都有地方睡") +
                   "\n\n食物与床位充足时，会有新的村民迁入。";
        }

        private string FoodTip()
        {
            var w = W; if (w == null) return null;
            float perDay = w.Population * w.Config.FoodPerAdultPerDay;
            int fields = w.CountBuildings(BuildingKind.Field, false), huts = w.CountBuildings(BuildingKind.FishingHut, false);
            return "<b>食物 " + w.Stored(ItemType.Food) + "</b>（仓储中）\n每天消耗约 " + perDay.ToString("0") + "，可维持 " + HudUtil.Fmt(w.FoodDays) + " 天\n" +
                   "农田 " + fields + " 块 · 渔屋 " + huts + " 座\n\n湿润的土地适合开垦农田，河边会有人建渔屋。";
        }

        private string WaterTip()
        {
            var w = W; if (w == null) return null;
            float perDay = w.Population * w.Config.WaterPerAdultPerDay;
            return "<b>清水 " + w.Stored(ItemType.Water) + "</b>（仓储中）\n每天消耗约 " + perDay.ToString("0") + "，可维持 " + HudUtil.Fmt(w.WaterDays) + " 天\n\n村民会去最近的水边打水——河离营地越近越好。";
        }

        private string WoodTip()
        {
            var w = W; if (w == null) return null;
            int sites = 0, need = 0;
            foreach (var b in w.Buildings) if (!b.IsComplete) { sites++; need += b.WoodShortfall; }
            int grown = 0, saplings = 0;
            foreach (var t in w.Trees) { if (t.IsSapling) saplings++; else if (t.Harvestable) grown++; }
            return "<b>木材 " + w.Stored(ItemType.Wood) + "</b>（仓储中）\n" + (sites > 0 ? "工地 " + sites + " 处，还需运送 " + need + "\n" : "") +
                   "可砍伐的树 " + grown + " 棵 · 树苗 " + saplings + " 棵\n\n用「森林」种下树苗，长大后才能砍伐。";
        }

        private string StorageTip()
        {
            var w = W; if (w == null) return null;
            return "<b>仓储 " + w.StorageUsed + " / " + w.StorageCapacity + "</b>\n营地与仓库共 " + w.Storages.Count + " 处\n\n仓储将满时，村民会自己建起新的仓库。";
        }

        private void Update()
        {
            var w = W;
            var g = GameServices.Game;
            if (w == null || g == null) return;
            float dt = Time.unscaledDeltaTime;
            foreach (var c in new[] { _pop, _food, _water, _wood, _storage })
            {
                c.Pulse = Mathf.MoveTowards(c.Pulse, 0f, dt * 2.2f);
                c.Value.transform.localScale = Vector3.one * (1f + 0.16f * Mathf.Sin(c.Pulse * Mathf.PI));
            }
            _refresh -= dt;
            if (_refresh > 0f) return;
            _refresh = 0.2f;

            _world.text = w.WorldName;
            float hour = w.Hour;
            _clock.text = "第 " + w.Day + " 天 · " + w.ClockText + " " + HudUtil.PartOfDay(hour);
            bool night = w.IsNight;
            _timeIcon.sprite = night && _moon != null ? _moon : _sun;
            _timeIcon.color = night ? new Color(0.75f, 0.82f, 1f) : UIStyle.Accent;

            int homeless = 0; foreach (var v in w.Villagers) if (!v.HomeId.IsValid) homeless++;
            Set(_pop, w.Population + "/" + w.Beds, homeless > 0 && w.Population > w.Beds ? UIStyle.Warn : UIStyle.Text);
            Set(_food, w.Stored(ItemType.Food).ToString(), DaysColor(w.FoodDays), "食物 · " + HudUtil.Fmt(w.FoodDays) + " 天");
            Set(_water, w.Stored(ItemType.Water).ToString(), DaysColor(w.WaterDays), "清水 · " + HudUtil.Fmt(w.WaterDays) + " 天");
            Set(_wood, w.Stored(ItemType.Wood).ToString(), UIStyle.Text);
            float fill = w.StorageCapacity > 0 ? (float)w.StorageUsed / w.StorageCapacity : 0f;
            Set(_storage, Mathf.RoundToInt(fill * 100f) + "%", fill > 0.92f ? UIStyle.Warn : UIStyle.Text);

            float speed = g.SpeedMultiplier;
            _pause.Selected = g.UserPaused;
            if (_pause.Icon != null) { var ps = UIStyle.Icon(g.UserPaused ? "Play" : "Pause"); if (ps != null) _pause.Icon.sprite = ps; }
            _x1.Selected = !g.UserPaused && speed < 1.5f;
            _x2.Selected = !g.UserPaused && speed >= 1.5f && speed < 3f;
            _x4.Selected = !g.UserPaused && speed >= 3f;
            _overlay.Selected = GameUI.Instance.Hud.OverlayVisible;
        }

        private static Color DaysColor(float days) => days < 1f ? UIStyle.Bad : days < 2f ? UIStyle.Warn : UIStyle.Text;

        private static void Set(Chip c, string value, Color color, string caption = null)
        {
            if (c.Last != null && c.Last != value) c.Pulse = 1f;
            c.Last = value;
            c.Value.text = value;
            c.Value.color = color;
            if (caption != null) c.Caption.text = caption;
            c.Icon.color = color == UIStyle.Text ? UIStyle.Text : color;
        }
    }
}
