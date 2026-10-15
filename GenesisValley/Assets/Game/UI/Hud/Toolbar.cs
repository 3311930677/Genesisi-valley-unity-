using System;
using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>
    /// Bottom-centre "divine powers" bar (GDD §4): eight tools with hotkey badges, an options card for the active
    /// tool (brush size, strength, river width, villager count) and an undo button.
    /// </summary>
    public sealed class Toolbar : MonoBehaviour
    {
        private static readonly string[] Icons = { "Observe", "Raise", "Lower", "Flatten", "River", "Forest", "Summon", "Bridge" };

        private sealed class Binding
        {
            public UISliderRow Row;
            public Func<float> Get;
        }

        private readonly UIButton[] _buttons = new UIButton[8];
        private RectTransform _options, _optionsBody;
        private CanvasGroup _optionsGroup;
        private Text _optTitle, _optHint, _optKey;
        private Image _optIcon;
        private readonly List<Binding> _bindings = new List<Binding>();
        private ToolController _tools;
        private ToolKind _builtFor = (ToolKind)(-1);
        private RectTransform _selector;
        private RectTransform _bar;

        private void Awake()
        {
            var rt = (RectTransform)transform;
            _bar = UIFactory.Panel(rt, "Bar", UIStyle.Panel, 16);
            _bar.anchorMin = _bar.anchorMax = _bar.pivot = new Vector2(0.5f, 0);
            _bar.anchoredPosition = new Vector2(0, 18);
            UIFactory.HLayout(_bar, 6, 12, 12, 10, 10, TextAnchor.MiddleCenter);
            UIFactory.Fit(_bar, true, false);
            _bar.sizeDelta = new Vector2(0, 86);

            _selector = UIFactory.Image(_bar, "Selector", new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.9f), UIStyle.Rounded(2)).rectTransform;
            _selector.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _selector.anchorMin = _selector.anchorMax = new Vector2(0, 0);
            _selector.pivot = new Vector2(0.5f, 0);
            _selector.sizeDelta = new Vector2(30, 4);

            for (int i = 0; i < 8; i++)
            {
                var k = (ToolKind)i;
                var key = InputBindings.KeyName(InputBindings.Get(ToolController.ToolAction(k)));
                var b = UIFactory.IconButton(_bar, Icons[i], ToolController.ToolName(k).Substring(0, 1), () => Select(k), 64, null, key);
                b.TooltipProvider = () => "<b>" + ToolController.ToolName(k) + "</b>  " + UIStyle.Colorize("[" + InputBindings.KeyName(InputBindings.Get(ToolController.ToolAction(k))) + "]", UIStyle.Accent) + "\n" + ToolController.ToolHint(k);
                b.ClickSound = null; // the tool controller plays its own select sound
                _buttons[i] = b;
                if (i == 0 || i == 3 || i == 5)
                {
                    var sep = UIFactory.Image(_bar, "Sep", new Color(1, 0.92f, 0.8f, 0.12f));
                    UIFactory.Layout(sep, 1.5f, 46);
                }
            }
            var undo = UIFactory.IconButton(_bar, "Undo", "↶", () => _tools?.Undo(), 54);
            undo.TooltipProvider = () => "撤销上一步（" + InputBindings.KeyName(InputBindings.Get(GameAction.Undo)) + " 或 Ctrl+Z）\n已经被村民使用的成果无法撤销。";

            // options card
            _options = UIFactory.Panel(rt, "Options", UIStyle.Panel, 14);
            _options.anchorMin = _options.anchorMax = _options.pivot = new Vector2(0.5f, 0);
            _options.anchoredPosition = new Vector2(0, 116);
            _options.sizeDelta = new Vector2(560, 100);
            _optionsGroup = UIFactory.Group(_options);
            var v = UIFactory.VLayout(_options, 4, 20, 20, 14, 14);
            UIFactory.Fit(_options, false, true);
            var head = UIFactory.Rect(_options, "Head");
            UIFactory.HLayout(head, 10);
            UIFactory.Layout(head, -1, 34);
            _optIcon = UIFactory.Image(head, "Icon", UIStyle.Accent, null);
            _optIcon.preserveAspect = true;
            UIFactory.Layout(_optIcon, 28, 28);
            _optTitle = UIFactory.Label(head, "", UIStyle.FontLarge, UIStyle.Text, TextAnchor.MiddleLeft, true, UIStyle.TitleFont);
            _optTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_optTitle, 300, 34, 1);
            _optKey = UIFactory.Label(head, "", UIStyle.FontSmall, UIStyle.TextFaint, TextAnchor.MiddleRight);
            UIFactory.Layout(_optKey, 160, 34);
            _optHint = UIFactory.Label(_options, "", UIStyle.FontSmall, UIStyle.TextDim, TextAnchor.UpperLeft);
            UIFactory.Layout(_optHint, 520, -1);
            _optionsBody = UIFactory.Rect(_options, "Body");
            UIFactory.VLayout(_optionsBody, 0);
            _options.gameObject.SetActive(false);
        }

        private void Select(ToolKind k)
        {
            if (_tools == null) return;
            _tools.SetTool(_tools.Current == k && k != ToolKind.Observe ? ToolKind.Observe : k);
        }

        private void Bind()
        {
            if (_tools != null) _tools.ToolChanged -= OnToolChanged;
            _tools = GameServices.Tools;
            if (_tools != null) { _tools.ToolChanged += OnToolChanged; OnToolChanged(_tools.Current); }
        }

        private void OnDestroy() { if (_tools != null) _tools.ToolChanged -= OnToolChanged; }

        private void OnToolChanged(ToolKind k)
        {
            for (int i = 0; i < 8; i++) _buttons[i].Selected = i == (int)k;
            if (k == ToolKind.Observe) { HideOptions(); return; }
            if (_builtFor != k) BuildOptions(k);
            ShowOptions();
            var b = _buttons[(int)k];
            UITween.Scale(b.transform, Vector3.one * 1.18f, Vector3.one, 0.3f, UITween.OutBack);
        }

        private void ShowOptions()
        {
            if (!_options.gameObject.activeSelf)
            {
                _options.gameObject.SetActive(true);
                _optionsGroup.alpha = 0f;
                _options.anchoredPosition = new Vector2(0, 104);
            }
            UITween.Fade(_optionsGroup, 1f, 0.15f);
            UITween.Move(_options, new Vector2(0, 116), 0.2f, UITween.OutCubic);
        }

        private void HideOptions()
        {
            if (!_options.gameObject.activeSelf) return;
            UITween.Fade(_optionsGroup, 0f, 0.12f, () => { if (_tools == null || _tools.Current == ToolKind.Observe) _options.gameObject.SetActive(false); });
        }

        private void BuildOptions(ToolKind k)
        {
            _builtFor = k;
            UIFactory.Clear(_optionsBody);
            _bindings.Clear();
            _optTitle.text = ToolController.ToolName(k);
            _optIcon.sprite = UIStyle.Icon(Icons[(int)k]);
            _optKey.text = "快捷键 " + InputBindings.KeyName(InputBindings.Get(ToolController.ToolAction(k)));
            _optHint.text = ToolController.ToolHint(k);
            var c = GameServices.World != null ? GameServices.World.Config : Core.SimConfig.CreateDefault();
            switch (k)
            {
                case ToolKind.Raise:
                case ToolKind.Lower:
                case ToolKind.Flatten:
                    Slider("笔刷大小", c.BrushMinRadius, c.BrushMaxRadius, () => _tools.Radius(k), v => _tools.SetRadius(k, v), v => v.ToString("0") + " 米");
                    if (k != ToolKind.Flatten) Slider("力度", 0.1f, 1f, () => _tools.Strength, v => _tools.Strength = v, v => Mathf.RoundToInt(v * 100) + "%");
                    break;
                case ToolKind.Forest:
                    Slider("种植范围", 5f, 25f, () => _tools.Radius(k), v => _tools.SetRadius(k, v), v => v.ToString("0") + " 米");
                    break;
                case ToolKind.River:
                    Slider("河道宽度", c.RiverMinWidth, c.RiverMaxWidth, () => _tools.RiverWidth, v => _tools.SetRadius(ToolKind.River, v), v => v.ToString("0.0") + " 米");
                    break;
                case ToolKind.Summon:
                    Slider("召唤人数", 1, c.SummonMaxPerBatch, () => _tools.SummonCount, v => _tools.SummonCount = Mathf.RoundToInt(v), v => v.ToString("0") + " 人", true);
                    break;
            }
            if (_bindings.Count > 0)
            {
                var foot = UIFactory.Label(_optionsBody, "Ctrl + 滚轮 或 [ ] 调整大小 · 右键 / Esc 取消", 14, UIStyle.TextFaint, TextAnchor.MiddleLeft);
                UIFactory.Layout(foot, -1, 22);
            }
        }

        private void Slider(string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> fmt, bool whole = false)
        {
            var row = UIFactory.SliderRow(_optionsBody, label, min, max, get(), fmt, set, whole);
            _bindings.Add(new Binding { Row = row, Get = get });
        }

        private void Update()
        {
            if (_tools != GameServices.Tools) Bind();
            if (_tools == null) return;
            foreach (var b in _bindings)
            {
                float v = b.Get();
                if (Mathf.Abs(v - b.Row.Get()) > 0.001f) b.Row.Set(v);
            }
            // selection underline glides between buttons
            var target = (RectTransform)_buttons[(int)_tools.Current].transform;
            float x = target.anchoredPosition.x;
            var p = _selector.anchoredPosition;
            p.x = Mathf.Lerp(p.x, x, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            p.y = 6f;
            _selector.anchoredPosition = p;
        }
    }
}
