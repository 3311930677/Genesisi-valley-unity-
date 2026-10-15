using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Main menu over the live valley (GDD §3): 继续 / 新建世界 / 加载存档 / 加入朋友 / 设置 / 致谢 / 退出.
    /// The left side is darkened by a soft gradient so the slowly orbiting village stays the hero.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private sealed class Item
        {
            public UIButton Button;
            public Image Bar;
            public Text Sub;
            public CanvasGroup Group;
            public RectTransform Rt;
        }

        private CanvasGroup _group;
        private RectTransform _menu;
        private readonly List<Item> _items = new List<Item>();
        private Item _continue;
        private Text _valley, _version;
        private CanvasGroup _logoGroup;
        private RectTransform _logo;
        private bool _shown;
        private float _t;
        private SaveSlotInfo _latest;

        private static GameBootstrap Game => GameServices.Game;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _group = UIFactory.Group(rt);

            var left = UIFactory.Image(rt, "LeftShade", new Color(0.04f, 0.03f, 0.02f, 0.82f), HorizontalFade());
            left.rectTransform.anchorMin = new Vector2(0, 0); left.rectTransform.anchorMax = new Vector2(0.62f, 1);
            left.rectTransform.offsetMin = left.rectTransform.offsetMax = Vector2.zero;
            var bottom = UIFactory.Image(rt, "BottomShade", new Color(0, 0, 0, 0.5f), UIStyle.Gradient(false, 1.8f));
            bottom.rectTransform.anchorMin = new Vector2(0, 0); bottom.rectTransform.anchorMax = new Vector2(1, 0.22f);
            bottom.rectTransform.offsetMin = bottom.rectTransform.offsetMax = Vector2.zero;
            bottom.rectTransform.localScale = new Vector3(1, -1, 1);

            _logo = UIFactory.Rect(rt, "Logo");
            UIFactory.Place(_logo, new Vector2(0, 1), new Vector2(760, 260), new Vector2(130, -110));
            _logoGroup = UIFactory.Group(_logo);
            var glow = UIFactory.Image(_logo, "Glow", new Color(1f, 0.75f, 0.4f, 0.13f), UIStyle.Glow());
            UIFactory.Place(glow.rectTransform, new Vector2(0, 1), new Vector2(760, 300), new Vector2(-150, 30));
            var title = UIFactory.Label(_logo, "创世纪谷", 116, UIStyle.Text, TextAnchor.UpperLeft, true, UIStyle.TitleFont);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(760, 140), Vector2.zero);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.06f, 0.02f, 0.35f);
            outline.effectDistance = new Vector2(2, -2);
            var en = UIFactory.Label(_logo, "G E N E S I S    V A L L E Y", 21, UIStyle.Accent, TextAnchor.UpperLeft);
            UIFactory.Place(en.rectTransform, new Vector2(0, 1), new Vector2(760, 30), new Vector2(8, -146));
            var rule = UIFactory.Image(_logo, "Rule", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.55f), UIStyle.Divider());
            UIFactory.Place(rule.rectTransform, new Vector2(0, 1), new Vector2(420, 10), new Vector2(4, -182));
            var tag = UIFactory.Label(_logo, "塑造大地，看一座村庄自己生长。", 26, UIStyle.TextDim, TextAnchor.UpperLeft, true, UIStyle.TitleFont);
            UIFactory.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(760, 40), new Vector2(6, -204));

            _menu = UIFactory.Rect(rt, "Menu");
            UIFactory.Place(_menu, new Vector2(0, 1), new Vector2(460, 520), new Vector2(118, -430));
            UIFactory.VLayout(_menu, 4);

            _continue = AddItem("继续", Continue, "Play");
            AddItem("新建世界", () => GameUI.Instance.NewWorld.Open(), "Star");
            AddItem("加载存档", () => GameUI.Instance.SaveLoad.OpenLoad(), "Load");
            var join = AddItem("加入朋友", null, "Population");
            join.Button.Interactable = false;
            join.Button.Tooltip = "多人联机将在后续阶段开放。\n当前版本专注于单人山谷的完整体验。";
            join.Sub.text = "即将开放";
            join.Sub.gameObject.SetActive(true);
            AddItem("设置", () => GameUI.Instance.Settings.Open(), "Gear");
            AddItem("致谢", () => GameUI.Instance.Credits.Open(), "Heart");
            AddItem("退出游戏", () => GameUI.Instance.Ask("退出游戏", "确定要离开山谷吗？", "退出", () => Game.Quit()), "Exit");

            _valley = UIFactory.Label(rt, "", UIStyle.FontSmall, new Color(1, 0.95f, 0.88f, 0.75f), TextAnchor.LowerRight, true);
            UIFactory.Place(_valley.rectTransform, new Vector2(1, 0), new Vector2(700, 60), new Vector2(-48, 36));
            _version = UIFactory.Label(rt, "", 15, UIStyle.TextFaint, TextAnchor.LowerLeft);
            UIFactory.Place(_version.rectTransform, new Vector2(0, 0), new Vector2(700, 40), new Vector2(130, 30));
            _version.text = "版本 " + Application.version + " · 单人原型 · Tuanjie / Unity URP";

            gameObject.SetActive(false);
        }

        private static Sprite _hfade;
        private static Sprite HorizontalFade()
        {
            if (_hfade != null) return _hfade;
            const int w = 256;
            var tex = new Texture2D(w, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < w; x++)
            {
                float t = x / (w - 1f);
                float a = 1f - Mathf.SmoothStep(0.15f, 1f, t);
                for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply(false, true);
            _hfade = Sprite.Create(tex, new Rect(0, 0, w, 4), new Vector2(0.5f, 0.5f));
            return _hfade;
        }

        private Item AddItem(string text, System.Action onClick, string icon)
        {
            var b = UIFactory.Button(_menu, text, onClick, 460, 62, null, 30, 8);
            b.Normal = new Color(1, 1, 1, 0f);
            b.Hover = new Color(1, 0.9f, 0.75f, 0.07f);
            b.Pressed = new Color(1, 0.9f, 0.75f, 0.12f);
            b.HoverScale = 1.0f;
            b.Outline.gameObject.SetActive(false);
            b.Outline = null;
            b.Label.font = UIStyle.TitleFont;
            b.Label.alignment = TextAnchor.MiddleLeft;
            UIFactory.Stretch(b.Label.rectTransform, 34, 10, 0, 0);
            b.Label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.5f);
            var bar = UIFactory.Image(b.transform, "Bar", UIStyle.Accent, UIStyle.Rounded(2));
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(0, 0.5f); brt.pivot = new Vector2(0, 0.5f);
            brt.sizeDelta = new Vector2(4, 0); brt.anchoredPosition = new Vector2(12, 0);
            var sub = UIFactory.Label(b.transform, "", UIStyle.FontSmall, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Stretch(sub.rectTransform, 10, 18, 0, 0);
            sub.gameObject.SetActive(false);
            b.Refresh();
            var item = new Item { Button = b, Bar = bar, Sub = sub, Rt = (RectTransform)b.transform, Group = UIFactory.Group(b) };
            _items.Add(item);
            return item;
        }

        public void SetShown(bool show)
        {
            if (show == _shown) { if (show) RefreshContinue(); return; }
            _shown = show;
            if (show)
            {
                gameObject.SetActive(true);
                _group.blocksRaycasts = true;
                _group.alpha = 0f;
                UITween.Fade(_group, 1f, 0.8f, null, 0.3f);
                _t = 0f;
                RefreshContinue();
                _logoGroup.alpha = 0f;
                UITween.Run(_logo, 1.4f, k => { _logoGroup.alpha = k; _logo.anchoredPosition = new Vector2(130 - 24 * (1 - k), -110); }, UITween.OutCubic, null, 0.4f);
                for (int i = 0; i < _items.Count; i++)
                {
                    var it = _items[i];
                    it.Group.alpha = 0f;
                    UITween.Run(it, 0.55f, k => { it.Group.alpha = k; it.Button.Label.rectTransform.offsetMin = new Vector2(34 - 22 * (1 - k), 0); }, UITween.OutCubic, null, 0.7f + i * 0.06f);
                }
            }
            else
            {
                _group.blocksRaycasts = false;
                UITween.Fade(_group, 0f, 0.35f, () => { if (!_shown) gameObject.SetActive(false); });
            }
        }

        private void RefreshContinue()
        {
            _latest = Game != null ? Game.LatestSave() : null;
            bool has = _latest != null;
            _continue.Rt.gameObject.SetActive(has);
            if (has)
            {
                var h = _latest.Header;
                _continue.Sub.text = (h != null ? h.WorldName + " · 第 " + h.Day + " 天" : _latest.Slot);
                _continue.Sub.gameObject.SetActive(true);
            }
        }

        private void Continue()
        {
            if (_latest == null) { RefreshContinue(); return; }
            Game.Load(_latest.Slot);
        }

        private void OnEnable() { if (Game != null) Game.SavesChanged += RefreshContinue; }
        private void OnDisable() { if (Game != null) Game.SavesChanged -= RefreshContinue; }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            foreach (var it in _items)
            {
                bool hot = it.Button.IsHovered && it.Button.Interactable;
                var s = it.Bar.rectTransform.sizeDelta;
                s.y = Mathf.MoveTowards(s.y, hot ? 30f : 0f, Time.unscaledDeltaTime * 260f);
                it.Bar.rectTransform.sizeDelta = s;
                it.Button.Label.color = !it.Button.Interactable ? new Color(UIStyle.TextFaint.r, UIStyle.TextFaint.g, UIStyle.TextFaint.b, 0.6f) : hot ? UIStyle.Accent : UIStyle.Text;
            }
            var w = GameServices.World;
            if (w != null && Time.frameCount % 15 == 0)
                _valley.text = "此刻的山谷 · 第 " + w.Day + " 天 " + w.ClockText + " · " + w.Population + " 位村民 · " + w.Buildings.Count + " 处建筑";
        }
    }

    /// <summary>新建世界: name + seed (numbers or any text) + intro toggle.</summary>
    public sealed class NewWorldDialog : UIWindow
    {
        private static readonly string[] Names = { "青禾谷", "溪月谷", "栖云谷", "白石谷", "枫渡", "柳岸", "落霞川", "鹿鸣谷", "听泉谷", "稻香原", "长歌谷", "桃溪" };
        private InputField _name, _seed;
        private UIToggleRow _intro;
        private Text _seedHint;

        protected override void Build()
        {
            var body = BuildFrame("新建世界", 720, 520);
            UIFactory.VLayout(body, 14);
            var intro = UIFactory.Label(body, "每个种子都会生成一座独一无二的山谷：山脊、谷地、溪流与森林。", UIStyle.FontBody, UIStyle.TextDim);
            UIFactory.Layout(intro, -1, 56);

            UIFactory.Layout(UIFactory.Label(body, "山谷的名字", UIStyle.FontSmall, UIStyle.TextFaint), -1, 24);
            var nameRow = UIFactory.Rect(body, "NameRow");
            UIFactory.HLayout(nameRow, 10);
            UIFactory.Layout(nameRow, -1, 48);
            _name = UIFactory.Input(nameRow, "为你的山谷取个名字", "", 520, 48);
            _name.characterLimit = 16;
            UIFactory.Button(nameRow, "换一个", () => _name.text = Names[Random.Range(0, Names.Length)], 120, 48, null, UIStyle.FontSmall);

            UIFactory.Layout(UIFactory.Label(body, "世界种子", UIStyle.FontSmall, UIStyle.TextFaint), -1, 24);
            var seedRow = UIFactory.Rect(body, "SeedRow");
            UIFactory.HLayout(seedRow, 10);
            UIFactory.Layout(seedRow, -1, 48);
            _seed = UIFactory.Input(seedRow, "数字或任意文字", "", 520, 48);
            _seed.characterLimit = 24;
            _seed.onValueChanged.AddListener(_ => UpdateSeedHint());
            UIFactory.Button(seedRow, "随机", RandomSeed, 120, 48, null, UIStyle.FontSmall);
            _seedHint = UIFactory.Label(body, "", 15, UIStyle.TextFaint);
            UIFactory.Layout(_seedHint, -1, 20);

            _intro = UIFactory.ToggleRow(body, "播放开场镜头", true, null, "约 7 秒的山谷俯瞰，可随时按任意键跳过。");

            UIFactory.Spacer(body, 6);
            var actions = UIFactory.Rect(body, "Actions");
            UIFactory.Layout(actions, -1, 56);
            var h = UIFactory.HLayout(actions, 12, 0, 0, 0, 0, TextAnchor.MiddleRight);
            UIFactory.Button(actions, "取消", Close, 160, 54);
            var go = UIFactory.Button(actions, "开始创世", Begin, 230, 54, "Play", UIStyle.FontLarge);
            go.Normal = UIStyle.AccentDark; go.Hover = UIStyle.Accent * new Color(1, 1, 1, 0.95f); go.Refresh();
        }

        protected override void OnOpen()
        {
            _name.text = Names[Random.Range(0, Names.Length)];
            RandomSeed();
        }

        private void RandomSeed()
        {
            _seed.text = Random.Range(100000, 99999999).ToString();
            UpdateSeedHint();
        }

        private void UpdateSeedHint()
        {
            _seedHint.text = "种子值：" + ParseSeed(_seed.text) + "（相同的种子总会生成相同的地形）";
        }

        public static ulong ParseSeed(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return 20261015UL;
            if (ulong.TryParse(s, out var v)) return v;
            // FNV-1a so word seeds ("春天") are stable across machines.
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h % 1000000000UL;
        }

        private void Begin()
        {
            var g = GameServices.Game;
            if (g == null) return;
            string name = string.IsNullOrWhiteSpace(_name.text) ? "创世纪谷" : _name.text.Trim();
            ulong seed = ParseSeed(_seed.text);
            bool intro = _intro.Value;
            Close();
            g.NewGame(name, seed, intro);
        }
    }

    /// <summary>致谢: credits for the open-licensed art and audio that make the valley.</summary>
    public sealed class CreditsWindow : UIWindow
    {
        protected override void Build()
        {
            var body = BuildFrame("致谢", 860, 720);
            var list = UIFactory.ScrollList(body, "List", out _, 10);
            UIFactory.Stretch((RectTransform)list.parent.parent);
            void Section(string head) { var t = UIFactory.Label(list, head, UIStyle.FontLarge, UIStyle.Accent, TextAnchor.MiddleLeft, false, UIStyle.TitleFont); UIFactory.Layout(t, -1, 40); }
            void Line(string a, string b)
            {
                var t = UIFactory.Label(list, "<b>" + a + "</b>\n<color=#" + UIStyle.Hex(UIStyle.TextFaint) + ">" + b + "</color>", UIStyle.FontBody, UIStyle.Text, TextAnchor.UpperLeft);
                UIFactory.Layout(t, -1, 58);
            }
            Section("游戏");
            Line("创世纪谷 Genesis Valley", "设计与开发：谈 世钊 · 引擎：Tuanjie / Unity（URP）");
            Section("美术");
            Line("Medieval Village MegaKit", "Quaternius · CC0 公共领域");
            Line("Stylized Nature MegaKit", "Quaternius · CC0 公共领域");
            Line("Universal Base Characters · Modular Character Outfits – Fantasy", "Quaternius · CC0 公共领域");
            Line("Universal Animation Library", "Quaternius · CC0 公共领域");
            Line("地表材质与天空 HDRI", "Poly Haven · CC0 公共领域");
            Line("界面图标", "Kenney Game Icons · CC0 公共领域");
            Section("声音");
            Line("Impact Sounds · Interface Sounds · RPG Audio", "Kenney · CC0 公共领域");
            Line("环境声与配乐", "程序合成（项目内工具生成）");
            Section("字体");
            Line("思源黑体 / 思源宋体（Noto Sans SC / Noto Serif SC）", "Google & Adobe · SIL Open Font License 1.1");
            UIFactory.Spacer(list, 10);
            var thanks = UIFactory.Label(list, "感谢所有把作品慷慨分享给世界的创作者。", UIStyle.FontBody, UIStyle.TextDim, TextAnchor.MiddleCenter, false, UIStyle.TitleFont);
            UIFactory.Layout(thanks, -1, 60);
        }
    }

    /// <summary>F2: controls overview (always reflects current key bindings).</summary>
    public sealed class HelpWindow : UIWindow
    {
        private Text _left, _right;
        public override bool PausesGame => true;

        protected override void Build()
        {
            var body = BuildFrame("操作说明", 980, 660);
            var cols = UIFactory.Rect(body, "Cols");
            UIFactory.Stretch(cols);
            UIFactory.HLayout(cols, 40);
            var hl = cols.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = true; hl.childAlignment = TextAnchor.UpperLeft;
            _left = UIFactory.Label(cols, "", UIStyle.FontBody, UIStyle.Text, TextAnchor.UpperLeft);
            _right = UIFactory.Label(cols, "", UIStyle.FontBody, UIStyle.Text, TextAnchor.UpperLeft);
            _left.lineSpacing = _right.lineSpacing = 1.25f;
        }

        protected override void OnOpen()
        {
            string K(GameAction a) => UIStyle.Colorize("[" + InputBindings.KeyName(InputBindings.Get(a)) + "]", UIStyle.Accent);
            string head(string t) => "<size=24>" + UIStyle.Colorize(t, UIStyle.Accent) + "</size>\n";
            _left.text = head("镜头") +
                K(GameAction.PanForward) + K(GameAction.PanLeft) + K(GameAction.PanBack) + K(GameAction.PanRight) + " 或 方向键  平移\n" +
                "按住鼠标中键  拖动大地\n按住鼠标右键  旋转 / 俯仰\n滚轮  缩放（朝鼠标位置）\n" +
                K(GameAction.RotateLeft) + K(GameAction.RotateRight) + "  旋转   " + K(GameAction.ZoomIn) + K(GameAction.ZoomOut) + "  缩放\nShift  加速平移\n" +
                K(GameAction.FocusCamp) + "  回到营地\n\n" +
                head("时间") +
                K(GameAction.Pause) + "  暂停 / 继续\n" + K(GameAction.Speed1) + K(GameAction.Speed2) + K(GameAction.Speed3) + "  1× / 2× / 4× 速度\n\n" +
                head("界面") +
                K(GameAction.ToggleUI) + "  隐藏界面    " + K(GameAction.PhotoMode) + "  拍照模式\n" +
                K(GameAction.Overlay) + "  湿润度图层    [L]  山谷编年史\n" +
                K(GameAction.QuickSave) + "  快速存档    " + K(GameAction.QuickLoad) + "  快速读档\n[F3]  性能信息    [Esc]  取消 / 菜单";
            _right.text = head("神力") +
                K(GameAction.ToolObserve) + " 观察  " + K(GameAction.ToolRaise) + " 抬升  " + K(GameAction.ToolLower) + " 降低  " + K(GameAction.ToolFlatten) + " 平整\n" +
                K(GameAction.ToolRiver) + " 河流  " + K(GameAction.ToolForest) + " 森林  " + K(GameAction.ToolSummon) + " 召唤  " + K(GameAction.ToolBridge) + " 桥梁\n" +
                K(GameAction.BrushSmaller) + K(GameAction.BrushLarger) + " 或 Ctrl + 滚轮  笔刷大小\n地形工具按住 Ctrl  反向\n" +
                K(GameAction.Undo) + " 或 Ctrl+Z  撤销上一步\n右键 / Esc  取消当前操作\n\n" +
                head("村庄如何生长") +
                "村民完全自主：他们会砍树、运木头、建房、种田、捕鱼、打水。\n" +
                "· 平坦、靠近营地的土地会被用来建房与仓库。\n" +
                "· 湿润的土地适合开垦农田；河边会出现渔屋。\n" +
                "· 食物和床位充足时，新村民会主动迁入。\n" +
                "· 你塑造土地，他们书写故事。";
        }
    }
}
