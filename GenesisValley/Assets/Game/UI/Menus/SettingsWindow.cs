using System;
using System.Collections.Generic;
using GenesisValley.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace GenesisValley.UI
{
    /// <summary>设置: 画面 / 声音 / 操作 / 按键 / 游戏. Changes apply live and persist when the window closes.</summary>
    public sealed class SettingsWindow : UIWindow
    {
        private enum Tab { Graphics, Audio, Controls, Keys, Game }
        private static readonly string[] TabNames = { "画面", "声音", "操作", "按键", "游戏" };

        private Tab _tab;
        private RectTransform _page;
        private ScrollRect _scroll;
        private readonly List<UIButton> _tabButtons = new List<UIButton>();
        private bool _dirty;

        // key capture
        private GameAction? _capturing;
        private readonly Dictionary<GameAction, UIButton> _keyButtons = new Dictionary<GameAction, UIButton>();
        private static KeyCode[] _allKeys;

        public override bool PausesGame => true;
        public override bool Muffles => false;

        private static GameSettings S => GameServices.Settings;

        protected override void Build()
        {
            var body = BuildFrame("设置", 980, 800);
            var tabs = UIFactory.Rect(body, "Tabs");
            tabs.anchorMin = new Vector2(0, 1); tabs.anchorMax = new Vector2(1, 1); tabs.pivot = new Vector2(0.5f, 1);
            tabs.offsetMin = new Vector2(0, -46); tabs.offsetMax = Vector2.zero;
            UIFactory.HLayout(tabs, 8);
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                _tabButtons.Add(UIFactory.Button(tabs, TabNames[i], () => Show(t), 130, 44));
            }
            _page = UIFactory.ScrollList(body, "Page", out _scroll, 6);
            UIFactory.Stretch((RectTransform)_scroll.transform, 0, 0, 62, 70);

            var foot = UIFactory.Rect(body, "Footer");
            foot.anchorMin = new Vector2(0, 0); foot.anchorMax = new Vector2(1, 0); foot.pivot = new Vector2(0.5f, 0);
            foot.sizeDelta = new Vector2(0, 54); foot.anchoredPosition = Vector2.zero;
            UIFactory.HLayout(foot, 12, 0, 0, 0, 0, TextAnchor.MiddleRight);
            var reset = UIFactory.Button(foot, "恢复本页默认", ResetPage, 190, 50, null, UIStyle.FontSmall);
            var done = UIFactory.Button(foot, "完成", Close, 170, 50);
            done.Normal = UIStyle.AccentDark; done.Refresh();
        }

        protected override void OnOpen() => Show(_tab);

        protected override void OnClose()
        {
            EndCapture();
            if (_dirty && S != null) { S.Save(); _dirty = false; }
        }

        public override bool HandleEscape()
        {
            if (_capturing.HasValue) { EndCapture(); return true; }
            Close();
            return true;
        }

        private void Changed()
        {
            _dirty = true;
            S?.Apply();
            var post = FindObjectOfType<PostFxController>();
            if (post != null) post.ApplyCameraSettings();
        }

        private void Show(Tab t)
        {
            EndCapture();
            _tab = t;
            for (int i = 0; i < _tabButtons.Count; i++) _tabButtons[i].Selected = i == (int)t;
            UIFactory.Clear(_page);
            _keyButtons.Clear();
            var s = S;
            if (s == null) return;
            switch (t)
            {
                case Tab.Graphics: BuildGraphics(s); break;
                case Tab.Audio: BuildAudio(s); break;
                case Tab.Controls: BuildControls(s); break;
                case Tab.Keys: BuildKeys(); break;
                case Tab.Game: BuildGame(s); break;
            }
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void Section(string title)
        {
            var l = UIFactory.Label(_page, title, UIStyle.FontBody, UIStyle.Accent, TextAnchor.LowerLeft, false, UIStyle.TitleFont);
            UIFactory.Layout(l, -1, 40);
        }

        private void Note(string text)
        {
            var l = UIFactory.Label(_page, text, 14, UIStyle.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Layout(l, -1, 24);
        }

        private static string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";

        // ------------------------------------------------------------------ pages

        private static string QualityName(string n)
        {
            switch (n)
            {
                case "Performant": case "Low": return "流畅";
                case "Balanced": case "Medium": return "均衡";
                case "High Fidelity": case "High": return "高画质";
                case "Very Low": return "最低";
                case "Very High": return "很高";
                case "Ultra": return "极致";
            }
            return n;
        }

        private void BuildGraphics(GameSettings s)
        {
            Section("显示");
            var res = Screen.resolutions;
            var resNames = new List<string>();
            int current = s.ResolutionIndex;
            for (int i = 0; i < res.Length; i++)
            {
                resNames.Add(res[i].width + " × " + res[i].height + "  " + RefreshText(res[i]));
                if (current < 0 && res[i].width == Screen.width && res[i].height == Screen.height) current = i;
            }
            if (resNames.Count == 0) { resNames.Add(Screen.width + " × " + Screen.height); current = 0; }
            UIFactory.ChoiceRow(_page, "分辨率", resNames.ToArray(), Mathf.Max(0, current), i => { if (i < res.Length) { s.ResolutionIndex = i; Changed(); } });
            UIFactory.ToggleRow(_page, "全屏", s.Fullscreen, v => { s.Fullscreen = v; Changed(); });
            UIFactory.ToggleRow(_page, "垂直同步", s.VSync, v => { s.VSync = v; Changed(); }, "开启后帧率与显示器刷新率同步，画面不撕裂。");
            int[] caps = { 30, 60, 90, 120, 144, 240 };
            string[] capNames = { "30", "60", "90", "120", "144", "240" };
            int capIndex = Array.IndexOf(caps, s.FrameCap); if (capIndex < 0) capIndex = 3;
            UIFactory.ChoiceRow(_page, "帧率上限（关闭垂直同步时）", capNames, capIndex, i => { s.FrameCap = caps[i]; Changed(); });
            UIFactory.SliderRow(_page, "界面缩放", 0.75f, 1.5f, s.UiScale, Pct, v => { s.UiScale = Mathf.Round(v * 20f) / 20f; _dirty = true; });

            Section("画质");
            var names = QualitySettings.names;
            var qn = new string[names.Length];
            for (int i = 0; i < names.Length; i++) qn[i] = QualityName(names[i]);
            int q = s.Quality >= 0 ? s.Quality : QualitySettings.GetQualityLevel();
            if (qn.Length > 0) UIFactory.ChoiceRow(_page, "画质预设", qn, Mathf.Clamp(q, 0, qn.Length - 1), i => { s.Quality = i; Changed(); });
            UIFactory.SliderRow(_page, "阴影距离", 60f, 300f, s.ShadowDistance, v => v.ToString("0") + " 米", v => { s.ShadowDistance = v; Changed(); }, true);
            UIFactory.SliderRow(_page, "植被与碎石密度", 0.2f, 1.5f, s.GrassDensity, Pct, v => { s.GrassDensity = v; _dirty = true; });
            UIFactory.ToggleRow(_page, "微缩景深", s.TiltShift, v => { s.TiltShift = v; Changed(); }, "近距离观察时轻微虚化远景，营造模型般的微缩感。");
            UIFactory.ToggleRow(_page, "精简粒子特效", s.ReducedEffects, v => { s.ReducedEffects = v; Changed(); }, "减少烟尘、水花等粒子数量，适合低配电脑。");
        }

        private static string RefreshText(Resolution r)
        {
#if UNITY_2022_2_OR_NEWER
            return Mathf.RoundToInt((float)r.refreshRateRatio.value) + " Hz";
#else
            return r.refreshRate + " Hz";
#endif
        }

        private void BuildAudio(GameSettings s)
        {
            Section("音量");
            UIFactory.SliderRow(_page, "主音量", 0f, 1f, s.MasterVolume, Pct, v => { s.MasterVolume = v; _dirty = true; });
            UIFactory.SliderRow(_page, "音乐", 0f, 1f, s.MusicVolume, Pct, v => { s.MusicVolume = v; _dirty = true; });
            UIFactory.SliderRow(_page, "音效", 0f, 1f, s.SfxVolume, Pct, v => { s.SfxVolume = v; _dirty = true; if (Time.frameCount % 6 == 0) GameServices.Audio?.PlayUI("ui_click", 0.6f); });
            UIFactory.SliderRow(_page, "环境声", 0f, 1f, s.AmbienceVolume, Pct, v => { s.AmbienceVolume = v; _dirty = true; });
            Note("环境声包括风声、流水、篝火与夜晚的虫鸣。");
        }

        private void BuildControls(GameSettings s)
        {
            Section("镜头");
            UIFactory.SliderRow(_page, "平移速度", 0.4f, 2.5f, s.CameraPanSpeed, Pct, v => { s.CameraPanSpeed = v; _dirty = true; });
            UIFactory.SliderRow(_page, "旋转速度", 0.4f, 2.5f, s.CameraRotateSpeed, Pct, v => { s.CameraRotateSpeed = v; _dirty = true; });
            UIFactory.ToggleRow(_page, "屏幕边缘平移", s.EdgePan, v => { s.EdgePan = v; _dirty = true; }, "鼠标移到屏幕边缘时镜头跟着移动。");
            UIFactory.ToggleRow(_page, "反转滚轮缩放", s.InvertZoom, v => { s.InvertZoom = v; _dirty = true; });
            Note("中键拖动大地 · 右键旋转 · 滚轮缩放 · Shift 加速。快捷键可在「按键」页重新设置。");
        }

        private void BuildKeys()
        {
            Section("快捷键");
            Note("点击右侧按键后按下新的键位；Esc 取消。与其他功能冲突时会自动互换。");
            foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
            {
                if (!InputBindings.Labels.TryGetValue(a, out var label)) label = a.ToString();
                var row = UIFactory.Rect(_page, "Key_" + a);
                UIFactory.Layout(row, -1, 44, 1);
                var l = UIFactory.Label(row, label, UIStyle.FontBody, UIStyle.TextDim);
                l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(0.6f, 1);
                l.rectTransform.offsetMin = new Vector2(4, 0); l.rectTransform.offsetMax = Vector2.zero;
                var action = a;
                var b = UIFactory.Button(row, InputBindings.KeyName(InputBindings.Get(a)), () => BeginCapture(action), 200, 38, null, UIStyle.FontSmall);
                var brt = (RectTransform)b.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 0.5f);
                brt.anchoredPosition = new Vector2(-8, 0);
                _keyButtons[a] = b;
            }
        }

        private void BuildGame(GameSettings s)
        {
            Section("存档");
            UIFactory.ToggleRow(_page, "自动存档", s.AutoSave, v => { s.AutoSave = v; _dirty = true; }, "每隔几分钟以及退出时自动保存到「自动存档」。");
            var open = UIFactory.Button(_page, "打开存档文件夹", () =>
            {
                System.IO.Directory.CreateDirectory(GameBootstrap.SaveDirectory);
                Application.OpenURL("file:///" + GameBootstrap.SaveDirectory.Replace('\\', '/'));
            }, 240, 42, "Load", UIStyle.FontSmall);
            UIFactory.Layout(open, 240, 42);
            Section("界面");
            UIFactory.ToggleRow(_page, "显示村民名字", s.ShowVillagerNames, v => { s.ShowVillagerNames = v; _dirty = true; });
            UIFactory.ToggleRow(_page, "显示提示与心愿", s.ShowHints, v => { s.ShowHints = v; _dirty = true; }, "新手提示、目标面板与操作小贴士。");
            Section("其他");
            UIFactory.ToggleRow(_page, "切出窗口时暂停", s.PauseOnFocusLoss, v => { s.PauseOnFocusLoss = v; _dirty = true; });
            Note("存档位置：" + GameBootstrap.SaveDirectory);
        }

        private void ResetPage()
        {
            var s = S;
            if (s == null) return;
            var d = new GameSettings();
            switch (_tab)
            {
                case Tab.Graphics:
                    s.Quality = d.Quality; s.Fullscreen = d.Fullscreen; s.ResolutionIndex = d.ResolutionIndex; s.VSync = d.VSync; s.FrameCap = d.FrameCap;
                    s.UiScale = d.UiScale; s.ShadowDistance = d.ShadowDistance; s.GrassDensity = d.GrassDensity; s.TiltShift = d.TiltShift; s.ReducedEffects = d.ReducedEffects;
                    break;
                case Tab.Audio:
                    s.MasterVolume = d.MasterVolume; s.MusicVolume = d.MusicVolume; s.SfxVolume = d.SfxVolume; s.AmbienceVolume = d.AmbienceVolume;
                    break;
                case Tab.Controls:
                    s.CameraPanSpeed = d.CameraPanSpeed; s.CameraRotateSpeed = d.CameraRotateSpeed; s.EdgePan = d.EdgePan; s.InvertZoom = d.InvertZoom;
                    break;
                case Tab.Keys:
                    InputBindings.ResetAll();
                    break;
                case Tab.Game:
                    s.AutoSave = d.AutoSave; s.ShowVillagerNames = d.ShowVillagerNames; s.ShowHints = d.ShowHints; s.PauseOnFocusLoss = d.PauseOnFocusLoss;
                    break;
            }
            Changed();
            Show(_tab);
            GameUI.Instance.Notify("已恢复「" + TabNames[(int)_tab] + "」的默认设置");
        }

        // ------------------------------------------------------------------ key capture

        private void BeginCapture(GameAction a)
        {
            EndCapture();
            _capturing = a;
            if (_keyButtons.TryGetValue(a, out var b)) { b.Label.text = "请按下新按键…"; b.Selected = true; }
            GameUI.Instance.KeyCaptureActive = true;
            GameUI.Instance.RecomputeBlocking();
        }

        private void EndCapture()
        {
            if (!_capturing.HasValue) return;
            var a = _capturing.Value;
            _capturing = null;
            if (_keyButtons.TryGetValue(a, out var b)) b.Selected = false;
            RefreshKeys();
            if (GameUI.Instance != null) { GameUI.Instance.KeyCaptureActive = false; GameUI.Instance.RecomputeBlocking(); }
        }

        private void RefreshKeys()
        {
            foreach (var kv in _keyButtons) kv.Value.Label.text = InputBindings.KeyName(InputBindings.Get(kv.Key));
        }

        private void Update()
        {
            if (!_capturing.HasValue) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { EndCapture(); return; }
            if (_allKeys == null) _allKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            foreach (var k in _allKeys)
            {
                if (k == KeyCode.None || k == KeyCode.Escape || k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6 || k >= KeyCode.JoystickButton0) continue;
                if (!Input.GetKeyDown(k)) continue;
                var a = _capturing.Value;
                InputBindings.Set(a, k);
                GameServices.Audio?.PlayUI("ui_click", 0.7f);
                EndCapture();
                return;
            }
        }
    }
}
